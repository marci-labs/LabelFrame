using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
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
        builder.Services.AddSingleton<IPrinterStatusProvider>(sp =>
            sp.GetRequiredService<IPrintTransport>() as IPrinterStatusProvider ?? new UnsupportedStatusProvider());
        builder.Services.AddHostedService<JobPrintWorker>();

        var templateStore = new TemplateStore(options.TemplatesDbPath);
        await templateStore.InitializeAsync();
        builder.Services.AddSingleton(templateStore);
        builder.Services.AddSingleton<LabelPreviewRenderer>();

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

        app.MapGet("/healthz", () => Results.Ok(new { service = "LabelFrame.WinHost", status = "ok", transport = options.Transport.ToString() }));

        // ---- 模板管理（单机 CRUD + 导入导出 + 预览）----
        app.MapPost("/api/templates", async (Api.TemplatePackageDto? dto, TemplateStore templateStore, CancellationToken ct) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || dto.Contract is null || dto.Layout is null)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少模板 name / contract / layout。"));
            }

            await templateStore.SaveAsync(new TemplatePackage
            {
                Name = dto.Name,
                Group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group,
                Contract = dto.Contract,
                Layout = dto.Layout,
            }, ct);
            return Results.Ok(new { name = dto.Name, group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group });
        });

        app.MapGet("/api/templates", async (string? group, TemplateStore templateStore, CancellationToken ct) =>
            Results.Ok(await templateStore.ListAsync(group, ct)));

        app.MapGet("/api/templates/{name}", async (string name, TemplateStore templateStore, CancellationToken ct) =>
        {
            var package = await templateStore.GetAsync(name, ct);
            return package is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"模板不存在:{name}。"))
                : Results.Ok(package);
        });

        app.MapDelete("/api/templates/{name}", async (string name, TemplateStore templateStore, CancellationToken ct) =>
        {
            await templateStore.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/templates/{name}/export", async (string name, TemplateStore templateStore, CancellationToken ct) =>
        {
            var package = await templateStore.GetAsync(name, ct);
            return package is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"模板不存在:{name}。"))
                : Results.File(TemplatePackageSerializer.Export(package), "application/zip", $"{name}.lfpkg");
        });

        app.MapPost("/api/templates/import", async (IFormFile file, TemplateStore templateStore, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少模板包文件。"));
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            try
            {
                var package = TemplatePackageSerializer.Import(memory.ToArray());
                await templateStore.SaveAsync(package, ct);
                return Results.Ok(package.Name);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, ex.Message));
            }
        });

        app.MapPost("/api/templates/{name}/preview", async (string name, Api.PreviewRequest? request, TemplateStore templateStore, LabelPreviewRenderer renderer, CancellationToken ct) =>
        {
            var package = await templateStore.GetAsync(name, ct);
            if (package is null)
            {
                return Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"模板不存在:{name}。"));
            }

            var document = new LabelDocument
            {
                Layout = package.Layout,
                Data = request?.Data ?? new Dictionary<string, string>(),
            };
            var png = renderer.RenderPng(document, dpi: 203, package.Images);
            return Results.File(png, "image/png");
        });

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

        app.MapPost("/api/jobs/{jobId}/items/{itemIndex:int}/retry", async (string jobId, int itemIndex, LabelJobQueue queue, CancellationToken ct) =>
        {
            try
            {
                var job = await queue.RetryItemAsync(jobId, itemIndex, ct);
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
        });

        // ---- 打印机测试页 / 在线状态 ----
        app.MapGet("/api/printer/status", async (IPrinterStatusProvider provider, CancellationToken ct) =>
            Results.Ok(await provider.GetStatusAsync(ct)));

        app.MapPost("/api/printer/test", async (IPrintTransport transport, CancellationToken ct) =>
        {
            const string testZpl =
                "^XA^FO40,40^A0N,64,64^FDLabelFrame Test^FS" +
                "^FO40,120^BY2,3^BCN,80,Y,N,N^FDLABELFRAME-TEST^FS^XZ";
            await transport.SendAsync(testZpl, ct);
            return Results.Ok(new { sent = true, bytes = System.Text.Encoding.UTF8.GetByteCount(testZpl) });
        });

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

    private sealed class UnsupportedStatusProvider : IPrinterStatusProvider
    {
        public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
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