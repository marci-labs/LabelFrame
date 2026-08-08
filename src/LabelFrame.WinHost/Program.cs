using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Rendering;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost;

/// <summary>WinHost：本地打印服务（作业队列 + HTTP API + 打印 Worker）。</summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task Main(string[] args)
    {
        // 内容根固定为程序目录，保证从任意工作目录启动都能读到 appsettings.json
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        var options = new HostOptions();
        builder.Configuration.GetSection("WinHost").Bind(options);
        options.ApplyEnvironmentOverrides();
        builder.WebHost.UseUrls(options.ListenUrl);

        var store = new SqliteLabelJobStore(options.DatabasePath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNameCaseInsensitive = true;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            json.SerializerOptions.Converters.Add(new LabelElementJsonConverter());
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILabelJobStore>(store);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton<IZplEncoder>(new ZplEncoder());
        builder.Services.AddSingleton<ITextRasterizer>(new GdiTextRasterizer(options.FontFamily, options.FontFilePath));
        builder.Services.AddSingleton(sp => new JobSubmissionService(queue, sp.GetRequiredService<IZplEncoder>(), sp.GetRequiredService<ITextRasterizer>(), options.Dpi));
        builder.Services.AddSingleton<IPrintTransport>(CreateTransport(options));
        builder.Services.AddHostedService<JobPrintWorker>();

        if (!string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            builder.Services.AddSingleton(sp => new Routing.ServerJobPoller(
                new HttpClient(),
                options.ServerUrl!,
                options.DeviceId,
                options.DeviceName));
            builder.Services.AddHostedService(sp => new Routing.ServerRoutingWorker(
                sp.GetRequiredService<Routing.ServerJobPoller>(),
                sp.GetRequiredService<JobSubmissionService>(),
                queue,
                TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)),
                sp.GetRequiredService<ILogger<Routing.ServerRoutingWorker>>()));
        }

        var app = builder.Build();

        app.MapGet("/healthz", () => Results.Ok(new { service = "LabelFrame.WinHost", status = "ok" }));

        app.MapPost("/api/jobs", async (SubmitJobRequest? request, JobSubmissionService service, CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "请求体不能为空。"));
            }

            var result = await service.SubmitAsync(request, ct);
            if (result.Job is null)
            {
                return Results.BadRequest(new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
            }

            return result.Created
                ? Results.Accepted((string?)null, JobViews.From(result.Job))
                : Results.Ok(JobViews.From(result.Job));
        });

        app.MapGet("/api/jobs/{jobId}", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
        {
            var job = await queue.GetAsync(jobId, ct);
            return job is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。"))
                : Results.Ok(JobViews.From(job));
        });

        app.MapPost("/api/jobs/{jobId}/suspend", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.SuspendAsync, ct));

        app.MapPost("/api/jobs/{jobId}/resume", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.ResumeAsync, ct));

        app.MapPost("/api/jobs/{jobId}/cancel", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.CancelAsync, ct));

        await app.RunAsync();
    }

    private static async Task<IResult> TransitionAsync(
        string jobId,
        Func<string, CancellationToken, Task<LabelJob>> action,
        CancellationToken ct)
    {
        try
        {
            var job = await action(jobId, ct);
            return Results.Ok(JobViews.From(job));
        }
        catch (LabelJobException ex) when (ex.Code == JobErrorCodes.JobNotFound)
        {
            return Results.NotFound(new ErrorView(ex.Code, ex.Message));
        }
        catch (LabelJobException ex)
        {
            return Results.Conflict(new ErrorView(ex.Code, ex.Message));
        }
    }

    private static IPrintTransport CreateTransport(HostOptions options) => options.Transport switch
    {
        TransportMode.Log => new LogPrintTransport(Console.Out),
        TransportMode.Tcp => new Tcp9100PrintTransport(options.TcpHost, options.TcpPort),
        TransportMode.WindowsDriver => new RawPrinterTransport(options.PrinterName),
        TransportMode.Zebra => new ZebraPrinterTransport(
            options.ZebraKind,
            options.TcpHost,
            options.TcpPort,
            options.PrinterName,
            options.ZebraUsbName),
        _ => throw new InvalidOperationException($"不支持的传输模式：{options.Transport}。"),
    };
}