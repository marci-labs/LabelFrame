using System.Text.Json;
using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Logs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport.Plugins.Package;
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
if (!string.IsNullOrWhiteSpace(serverOptions.LogFilePath))
{
    builder.Logging.AddProvider(new FileLoggerProvider(serverOptions.LogFilePath));
}
builder.WebHost.UseUrls(serverOptions.ListenUrl);
// 迭代 23（决策 5A）：插件包上传端点大小上限 64MB（Kestrel 默认约 30MB，超出会返回 413 且无错误体）
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = PluginPackageLimits.MaxBytes);
// 迭代 19 反馈：缩短停机超时（默认 30s），避免客户端长轮询请求拖慢 Windows 服务停止 / 卸载 / 升级。
builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
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
var clientPackages = new ClientPackagesService(serverOptions.ClientPackagesPath);
var pluginPackages = new PluginPackagesService(serverOptions.PluginPackagesPath);

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
builder.Services.AddSingleton(clientPackages);
builder.Services.AddSingleton(pluginPackages);
builder.Services.AddHostedService(sp => new DataCleanupService(db, logStore, serverOptions, sp.GetRequiredService<ILogger<DataCleanupService>>()));
// Skia 渲染器实例单例：DI 与共享端点（模板预览 / 调试出图）共用同一实例
var skiaRenderer = new SkiaLabelRenderer();
builder.Services.AddSingleton<ILabelBitmapRenderer>(skiaRenderer);
// 本地工具服务：地址由用户配置（可跨机器 / 跨端口），启用宽松 CORS
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

// ---- 服务端管理界面插件（迭代 20）：静态前端包目录，目录存在即托管、移除即无头 ----
// 中间件常驻注册（FileProvider 指向插件目录）；目录出现 / 移除即时生效，无需重启。
if (!string.IsNullOrWhiteSpace(serverOptions.WebUiPath))
{
    // PhysicalFileProvider 要求根目录存在：启动时确保插件目录存在（空目录仍为无头，放入 index.html 即生效）；
    // 目录创建失败（如权限不足）降级为无头，不影响 API 启动。
    try
    {
        Directory.CreateDirectory(serverOptions.WebUiPath);
        var pluginFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(serverOptions.WebUiPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = pluginFileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = pluginFileProvider });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LabelFrame] 插件目录初始化失败（{serverOptions.WebUiPath}）：{ex.Message}，保持无头。");
    }
}

app.MapGet("/healthz", () => Results.Ok(new { service = "LabelFrame.Server", status = "ok" }));

// ---- 服务端信息（迭代 20：调试 / Server UI 探测用）----
app.MapGet("/api/server/info", (ServerOptions options) =>
    Results.Ok(new
    {
        listenUrl = options.ListenUrl,
        uiEnabled = ServerPluginUi.IsEnabled(options.WebUiPath),
        version = ServerOptions.ProductVersion,
    }));

// ---- 设备注册 / 目录 ----
app.MapPost("/api/devices", async (RegisterDeviceRequest? request, HttpContext context, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.RegisterDeviceAsync(request.DeviceId, request.Name, ServerService.NormalizeRemoteIp(context.Connection.RemoteIpAddress), ct));
    }
    catch (ServerException ex)
    {
        return Results.BadRequest(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/devices", async (ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListDevicesAsync(ct)));

app.MapGet("/api/devices/by-ip/{ip}", async (string ip, ServerService svc, CancellationToken ct) =>
{
    var device = await svc.FindDeviceByIpAsync(ip, ct);
    return device is null
        ? Results.NotFound(new ErrorView(ServerErrorCodes.DeviceNotFound, $"按 IP 未找到设备：{ip}。"))
        : Results.Ok(device);
});

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

app.MapGet("/api/jobs", async (int? limit, string? deviceId, ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListJobsAsync(limit ?? 100, deviceId, ct)));

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
app.MapGet("/api/devices/{deviceId}/jobs/notify", async (string deviceId, int? timeout, HttpContext context, ServerService svc, PendingJobNotifier notifier, CancellationToken ct) =>
{
    try
    {
        var seconds = Math.Clamp(timeout ?? 20, 1, 30);
        await svc.TouchDeviceAsync(deviceId, DateTimeOffset.UtcNow, ServerService.NormalizeRemoteIp(context.Connection.RemoteIpAddress), ct);
        var hasPending = await notifier.WaitAsync(deviceId, TimeSpan.FromSeconds(seconds), ct);
        return Results.Ok(new { hasPending });
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/devices/{deviceId}/jobs/pending", async (string deviceId, HttpContext context, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.ClaimPendingJobsAsync(deviceId, ServerService.NormalizeRemoteIp(context.Connection.RemoteIpAddress), ct));
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

// ---- 模板库（迭代 16：服务端集中管理；端点实现与 WinHost 共享，见 LabelFrame.Api.Endpoints）----
app.MapTemplateApi(new TemplateApiOptions(
    templateStore,
    skiaRenderer,
    serverOptions.Dpi,
    ServerErrorCodes.InvalidRequest,
    ServerErrorCodes.TemplateNotFound));

// ---- 调试出图（迭代 16：服务端渲染，浏览器下载；打印以客户端渲染为准；图片资源=请求附带优先、按名回退模板库）----
app.MapRenderApi(new RenderApiOptions(templateStore, skiaRenderer, serverOptions.Dpi, ServerErrorCodes.InvalidRequest));

// ---- 客户端下载分发（迭代 22 §2.3 / §5.4，决策 #71：服务端统一分发客户端安装包）----
app.MapGet("/api/client-packages", (ClientPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/client-packages", async (IFormFile file, ClientPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的安装包文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, $"上传失败：{ex.Message}"));
    }
}).DisableAntiforgery();

app.MapGet("/api/client-packages/{fileName}", (string fileName, ClientPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.ClientPackageNotFound, "安装包不存在。"));
    }

    return Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

app.MapDelete("/api/client-packages/{fileName}", (string fileName, ClientPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.ClientPackageNotFound, "安装包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

// ---- 传输插件包（迭代 23 §2.1 / §5.1，决策 2A：插件包上传服务端，客户端安装用；列表含元数据与 valid 状态，路径穿越防护）----
app.MapGet("/api/plugin-packages", (PluginPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/plugin-packages", async (IFormFile file, PluginPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的插件包文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, $"插件包无效：{ex.Message}"));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, $"上传失败：{ex.Message}"));
    }
}).DisableAntiforgery();

app.MapGet("/api/plugin-packages/{fileName}", (string fileName, PluginPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PluginPackageNotFound, "插件包不存在。"));
    }

    return Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

app.MapDelete("/api/plugin-packages/{fileName}", (string fileName, PluginPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PluginPackageNotFound, "插件包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

// ---- Excel 模板生成 / 设备日志 / Excel 数据导入（端点实现与 WinHost 共享，见 LabelFrame.Api.Endpoints）----
app.MapImportApi(new ImportApiOptions(ServerErrorCodes.InvalidRequest));
app.MapLogApi(new LogApiOptions(logStore, ServerErrorCodes.InvalidRequest));

// 迭代 18：服务端无头化——不再托管 Web UI / 测试页，仅提供 API 与 /healthz。

// ---- SPA fallback（插件）：仅非 /api/* 与 /healthz 的路径回退 index.html；插件未启用保持 404 ----
app.MapFallback(async context =>
{
    var indexFile = ServerPluginUi.ResolveIndexFile(serverOptions.WebUiPath, context.Request.Path.Value ?? string.Empty);
    if (indexFile is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexFile);
});

await app.RunAsync();
