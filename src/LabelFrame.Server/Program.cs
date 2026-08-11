using System.Text.Json;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Logs;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var serverOptions = new ServerOptions();
builder.Configuration.GetSection("Server").Bind(serverOptions);
serverOptions.ApplyEnvironmentOverrides();
builder.WebHost.UseUrls(serverOptions.ListenUrl);
#if WINDOWS
// 迭代 18：以 Windows 服务运行（LabelFrameServer）；直接运行 exe 仍是控制台（开发用）。
builder.Host.UseWindowsService(options => options.ServiceName = "LabelFrameServer");
#endif

var db = new ServerDb(serverOptions.DatabasePath);
await db.InitializeAsync();
var templateStore = new TemplateStore(serverOptions.TemplatesDbPath);
await templateStore.InitializeAsync();
var logStore = new SqliteLogStore(serverOptions.LogsDbPath);
await logStore.InitializeAsync();
var notifier = new PendingJobNotifier();
var service = new ServerService(db, templateStore, notifier);

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNameCaseInsensitive = true;
    json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    json.SerializerOptions.Converters.Add(new LabelFrame.Core.Layout.LabelElementJsonConverter());
});
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(service);
builder.Services.AddSingleton(notifier);
builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton(templateStore);
builder.Services.AddSingleton(logStore);
builder.Services.AddHostedService(sp => new DataCleanupService(db, logStore, serverOptions, sp.GetRequiredService<ILogger<DataCleanupService>>()));
builder.Services.AddSingleton<ILabelBitmapRenderer>(new SkiaLabelRenderer());
// 本地工具服务：地址由用户配置（可跨机器 / 跨端口），启用宽松 CORS
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

app.MapGet("/healthz", () => Results.Ok(new { service = "LabelFrame.Server", status = "ok" }));

// ---- 设备注册 / 目录 ----
app.MapPost("/api/devices", async (RegisterDeviceRequest? request, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.RegisterDeviceAsync(request.DeviceId, request.Name, ct));
    }
    catch (ServerException ex)
    {
        return Results.BadRequest(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/devices", async (ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListDevicesAsync(ct)));

// ---- 作业提交 / 查询 ----
app.MapPost("/api/jobs", async (SubmitJobRequest? request, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        var job = await svc.SubmitJobAsync(request, ct);
        return job.Status == "Pending"
            ? Results.Accepted((string?)null, job)
            : Results.Ok(job);
    }
    catch (ServerException ex)
    {
        return ex.Code == ServerErrorCodes.DeviceNotFound
            ? Results.NotFound(new ErrorView(ex.Code, ex.Message))
            : Results.BadRequest(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/jobs", async (int? limit, ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListJobsAsync(limit ?? 100, ct)));

app.MapGet("/api/jobs/{jobId}", async (string jobId, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.GetJobAsync(jobId, ct));
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message));
    }
});

// ---- 设备领取 / 回报 ----
// 长轮询通知（迭代 18 联调反馈）：作业到达立即返回 hasPending=true（等效推送）；同时刷新心跳保活。
app.MapGet("/api/devices/{deviceId}/jobs/notify", async (string deviceId, int? timeout, ServerService svc, PendingJobNotifier notifier, CancellationToken ct) =>
{
    try
    {
        var seconds = Math.Clamp(timeout ?? 20, 1, 30);
        await svc.TouchDeviceAsync(deviceId, DateTimeOffset.UtcNow, ct);
        var hasPending = await notifier.WaitAsync(deviceId, TimeSpan.FromSeconds(seconds), ct);
        return Results.Ok(new { hasPending });
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/devices/{deviceId}/jobs/pending", async (string deviceId, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.ClaimPendingJobsAsync(deviceId, ct));
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapPost("/api/devices/{deviceId}/jobs/{jobId}/result", async (string deviceId, string jobId, ReportResultRequest? report, ServerService svc, CancellationToken ct) =>
{
    if (report is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.ReportResultAsync(deviceId, jobId, report, ct));
    }
    catch (ServerException ex)
    {
        return ex.Code switch
        {
            ServerErrorCodes.JobNotFound => Results.NotFound(new ErrorView(ex.Code, ex.Message)),
            ServerErrorCodes.NotJobOwner => Results.Forbid(),
            _ => Results.Conflict(new ErrorView(ex.Code, ex.Message)),
        };
    }
});

// ---- 模板库（迭代 16：服务端集中管理）----
app.MapPost("/api/templates", async (TemplatePackageDto? dto, TemplateStore templates, CancellationToken ct) =>
{
    if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || dto.Contract is null || dto.Layout is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少模板 name / contract / layout。"));
    }

    await templates.SaveAsync(new TemplatePackage
    {
        Name = dto.Name,
        Group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group,
        Contract = dto.Contract,
        Layout = dto.Layout,
        TestData = dto.TestData ?? new Dictionary<string, string>(),
    }, ct);
    return Results.Ok(new { name = dto.Name, group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group });
});

app.MapGet("/api/templates", async (string? group, TemplateStore templates, CancellationToken ct) =>
    Results.Ok(await templates.ListAsync(group, ct)));

app.MapGet("/api/templates/{name}", async (string name, TemplateStore templates, CancellationToken ct) =>
{
    var package = await templates.GetAsync(name, ct);
    return package is null
        ? Results.NotFound(new ErrorView(ServerErrorCodes.TemplateNotFound, $"模板不存在:{name}。"))
        : Results.Ok(package);
});

app.MapDelete("/api/templates/{name}", async (string name, TemplateStore templates, CancellationToken ct) =>
{
    await templates.DeleteAsync(name, ct);
    return Results.NoContent();
});

app.MapGet("/api/templates/{name}/export", async (string name, TemplateStore templates, CancellationToken ct) =>
{
    var package = await templates.GetAsync(name, ct);
    return package is null
        ? Results.NotFound(new ErrorView(ServerErrorCodes.TemplateNotFound, $"模板不存在:{name}。"))
        : Results.File(TemplatePackageSerializer.Export(package), "application/zip", $"{name}.lfpkg");
});

app.MapPost("/api/templates/import", async (IFormFile file, TemplateStore templates, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少模板包文件。"));
    }

    using var memory = new MemoryStream();
    await file.CopyToAsync(memory, ct);
    try
    {
        var package = TemplatePackageSerializer.Import(memory.ToArray());
        await templates.SaveAsync(package, ct);
        return Results.Ok(package.Name);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, ex.Message));
    }
}).DisableAntiforgery();

app.MapPost("/api/templates/{name}/preview", async (string name, PreviewRequest? request, TemplateStore templates, ILabelBitmapRenderer renderer, CancellationToken ct) =>
{
    var package = await templates.GetAsync(name, ct);
    if (package is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.TemplateNotFound, $"模板不存在:{name}。"));
    }

    var document = new LabelDocument
    {
        Layout = package.Layout,
        Data = request?.Data ?? package.TestData ?? new Dictionary<string, string>(),
    };
    var png = renderer.RenderLabelBitmapPng(document, serverOptions.Dpi, package.Images);
    return Results.File(png, "image/png");
});

// ---- 调试出图（迭代 16：服务端渲染，浏览器下载；打印以客户端渲染为准）----
app.MapPost("/api/print/render-image", async (SubmitJobRequest? request, ILabelBitmapRenderer renderer, CancellationToken ct) =>
{
    if (request?.Template?.Contract is null || request.Template.Layout is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 template（contract + layout）。"));
    }

    if (request.Labels is null || request.Labels.Count == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 labels（至少一张）。"));
    }

    var document = new LabelDocument
    {
        Layout = request.Template.Layout,
        Data = request.Labels[0].Data ?? new Dictionary<string, string>(),
    };
    var images = DecodeImages(request.Template.Images);
    var png = renderer.RenderLabelBitmapPng(document, serverOptions.Dpi, images);
    var fileName = $"{(string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name)}-print.png";
    return Results.File(png, "image/png", fileName);
});

app.MapPost("/api/print/render-images", async (SubmitJobRequest? request, ILabelBitmapRenderer renderer, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    if (request.Template?.Contract is null || request.Template.Layout is null || request.Labels is null || request.Labels.Count == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 template 或 labels。"));
    }

    var images = DecodeImages(request.Template.Images);
    using var stream = new MemoryStream();
    using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
    {
        for (var i = 0; i < request.Labels.Count; i++)
        {
            var document = new LabelDocument
            {
                Layout = request.Template.Layout,
                Data = request.Labels[i].Data ?? new Dictionary<string, string>(),
            };
            var bitmap = renderer.RenderLabelBitmap(document, serverOptions.Dpi, images);
            var entry = archive.CreateEntry($"label-{i + 1}.png");
            using var entryStream = entry.Open();
            var png = LabelBitmapPng.ToPng(bitmap);
            entryStream.Write(png);
        }
    }

    var name = string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name;
    var zipName = $"{name}-debug-{DateTime.Now:yyyyMMddHHmmss}.zip";
    return Results.File(stream.ToArray(), "application/zip", zipName);
});

// ---- 设备日志（客户端 / PDA 回传，服务端集中查看）----
app.MapPost("/api/logs", async (PushLogRequest? request, SqliteLogStore logs, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || request.Lines is null || request.Lines.Count == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 deviceId / lines。"));
    }

    await logs.AppendAsync(request.DeviceId, request.Lines, ct);
    return Results.Ok(new { received = request.Lines.Count });
});

app.MapGet("/api/logs", async (string? deviceId, DateTimeOffset? since, SqliteLogStore logs, CancellationToken ct) =>
{
    var entries = await logs.QueryAsync(deviceId, since, ct);
    return Results.Ok(entries.Select(e => new { e.DeviceId, Time = e.Time, e.Line }));
});

// ---- Excel 数据导入（解析表头 + 数据行，前端做列映射）----
app.MapPost("/api/import/excel", async (IFormFile file, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请上传 .xlsx 文件。"));
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var table = TemplateFrame.Excel.Simple.SimpleExcel.Read(stream);
        var headers = (table.Headers ?? []).Select(h => h ?? string.Empty).ToList();
        var rows = table.Rows
            .Select(row => row.Select(FormatExcelCell).ToList())
            .ToList();
        return Results.Ok(new { headers, rows });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, $"Excel 解析失败：{ex.Message}"));
    }
}).DisableAntiforgery();

// 迭代 18：服务端无头化——不再托管 Web UI / 测试页，仅提供 API 与 /healthz。

// 模板图片 base64 → 字节
static IReadOnlyDictionary<string, byte[]> DecodeImages(IReadOnlyDictionary<string, string>? images)
    => images?.ToDictionary(kv => kv.Key, kv => System.Convert.FromBase64String(kv.Value)) ?? new Dictionary<string, byte[]>();

static string FormatExcelCell(object? value) => value switch
{
    null => string.Empty,
    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
    IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
    _ => value.ToString() ?? string.Empty,
};

await app.RunAsync();
