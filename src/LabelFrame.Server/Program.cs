using System.Text.Json;
using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Logs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.Rendering;
using LabelFrame.Server;
using LabelFrame.Server.Api;
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
// 插件包上传端点大小上限 64MB（Kestrel 默认约 30MB，超出会返回 413 且无错误体）
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = PluginPackageLimits.MaxBytes);
// 缩短停机超时（默认 30s）：避免客户端长轮询请求拖慢服务停止 / 卸载 / 升级。
builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
#if WINDOWS
// 以 Windows 服务运行（LabelFrameServer）；直接运行 exe 仍是控制台（开发用）。
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
// 全局异常处理：未捕获异常统一 500 + ErrorView，不透出堆栈 / 内部路径
builder.Services.AddLabelFrameExceptionHandler();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();

// ---- 服务端管理界面插件：静态前端包目录，目录存在即托管、移除即无头 ----
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

// ---- 服务端信息（调试 / Server UI 探测用）----
app.MapGet("/api/server/info", (ServerOptions options) =>
    Results.Ok(new
    {
        listenUrl = options.ListenUrl,
        uiEnabled = ServerPluginUi.IsEnabled(options.WebUiPath),
        version = ServerOptions.ProductVersion,
    }));

// ---- 服务端专属端点（分组实现见 Api/ 目录；模板 / 出图 / Excel / 日志与 WinHost 共享，见 LabelFrame.Api.Endpoints）----
app.MapDevicesApi();
app.MapServerJobsApi();
app.MapPackagesApi();

// ---- 模板库（服务端集中管理；端点实现与 WinHost 共享，见 LabelFrame.Api.Endpoints）----
app.MapTemplateApi(new TemplateApiOptions(
    templateStore,
    skiaRenderer,
    serverOptions.Dpi,
    ServerErrorCodes.InvalidRequest,
    ServerErrorCodes.TemplateNotFound));

// ---- 调试出图（服务端渲染，浏览器下载；打印以客户端渲染为准；图片资源=请求附带优先、按名回退模板库）----
app.MapRenderApi(new RenderApiOptions(templateStore, skiaRenderer, serverOptions.Dpi, ServerErrorCodes.InvalidRequest));

// ---- Excel 模板生成 / 设备日志 / Excel 数据导入（端点实现与 WinHost 共享，见 LabelFrame.Api.Endpoints）----
app.MapImportApi(new ImportApiOptions(ServerErrorCodes.InvalidRequest));
app.MapLogApi(new LogApiOptions(logStore, ServerErrorCodes.InvalidRequest));

// 服务端无头化——不再托管 Web UI / 测试页，仅提供 API 与 /healthz。

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


// WebApplicationFactory 集成测试入口（宿主专属端点 HTTP 测试）
public partial class Program { }
