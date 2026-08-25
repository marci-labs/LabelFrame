using System.Text.Json.Serialization;
using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.Core.Logs;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost;

/// <summary>
/// WinHost 应用装配：DI + 全部端点 + Web UI 静态托管。
/// 不含宿主层职责（Serilog 文件日志 / 托盘 / 浏览器拉起 / 强制退出）——集成测试用同一装配拉起
/// TestServer，保证测到的端点组合与生产完全一致。
/// </summary>
public static class WinHostApp
{
    /// <summary>装配并构建应用（含三库初始化与外部插件目录扫描）。</summary>
    /// <param name="options">宿主配置（路径与监听已就绪）。</param>
    /// <param name="hostInfo">宿主日志回调（Main 传 host.log 写入器；测试可传收集器）。</param>
    /// <param name="configureBuilder">Web 层扩展点（测试注入 UseTestServer 等），在最前执行。</param>
    /// <param name="configureServices">服务层扩展点（测试移除后台服务等），在全部服务注册完成后执行——顺序保证 RemoveAll 能生效。</param>
    public static async Task<WebApplication> BuildAsync(
        HostOptions options,
        TextWriter hostLogWriter,
        Action<string> hostInfo,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // 内容根固定为程序目录，保证从任意工作目录启动都能读到 appsettings.json
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseUrls(options.ListenUrl);
        // 插件包上传端点大小上限 64MB（Kestrel 默认约 30MB，超出会返回 413 且无错误体）
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = PluginPackageLimits.MaxBytes;
        });
        configureBuilder?.Invoke(builder);

        // 传输插件注册表 = Core 内置（log / tcp9100）+ WinHost 内置（winspool / zebra）+ 外部 DLL 目录扫描
        var transportRegistry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            transportRegistry.Register(plugin);
        }

        transportRegistry.Register(new WinspoolTransportPlugin());
        transportRegistry.Register(new ZebraTransportPlugin());
        var pluginContext = new TransportPluginContext(
            hostLogWriter,
            Path.GetDirectoryName(HostOptions.DefaultDatabasePath) ?? string.Empty);
        var pluginLoad = PluginDirectoryLoader.LoadWithErrors(options.PluginsPath, hostLogWriter);
        foreach (var (plugin, assemblyPath) in pluginLoad.Plugins)
        {
            // 外部插件不允许覆盖内置插件 ID（冲突时记日志跳过）
            if (transportRegistry.RegisterExternal(plugin, assemblyPath, hostLogWriter))
            {
                hostInfo($"已加载外部传输插件：{plugin.Id}（{plugin.DisplayName}，来自 {assemblyPath}）");
            }
        }

        var transportManager = new TransportManager(transportRegistry, pluginContext, options, hostLogWriter, options.ConnectionPath);

        var hostConfigStore = new HostConfigStore(options.ConfigPath);
        builder.Services.AddSingleton(hostConfigStore);
        // 批次作业设置（用户级持久化 + 内存单例，保存即生效；单例注入 JobPrintWorker）
        var printSettingsStore = new PrintSettingsStore(options.PrintSettingsPath);
        var printSettings = new PrintSettings();
        printSettings.Update(printSettingsStore.Load());
        builder.Services.AddSingleton(printSettingsStore);
        builder.Services.AddSingleton(printSettings);
        var machineServerUrl = hostConfigStore.LoadServerUrl();
        if (!string.IsNullOrWhiteSpace(machineServerUrl) && !string.Equals(machineServerUrl, options.ServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            options.ServerUrl = machineServerUrl;
            hostInfo($"已加载机器级配置：ServerUrl={options.ServerUrl}");
        }

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
        builder.Services.AddSingleton<ITransportManager>(transportManager);
        builder.Services.AddSingleton<ITransportPluginRegistry>(transportRegistry);
        builder.Services.AddSingleton(sp => new Transport.PluginInstaller(
            options.PluginsPath,
            sp.GetRequiredService<ITransportPluginRegistry>(),
            hostLogWriter,
            // 启动装配时的加载失败透出给已安装列表（「加载失败 err + 原因」）
            pluginLoad.Errors.ToDictionary(e => e.AssemblyPath, e => e.Error, StringComparer.OrdinalIgnoreCase)));
        builder.Services.AddSingleton<ZplImageEncoder>();
        builder.Services.AddHostedService<JobPrintWorker>();

        var templateStore = new TemplateStore(options.TemplatesDbPath);
        await templateStore.InitializeAsync();
        builder.Services.AddSingleton(templateStore);
        // Skia 渲染器实例单例：DI 与共享端点（模板预览 / 调试出图）共用同一实例（预览与打印同源）
        var skiaRenderer = new SkiaLabelRenderer();
        builder.Services.AddSingleton<ILabelBitmapRenderer>(skiaRenderer);
        builder.Services.AddSingleton(sp => new JobSubmissionService(
            queue,
            sp.GetRequiredService<ZplImageEncoder>(),
            options.Dpi,
            skiaRenderer,
            templateStore,
            transportManager,
            hostLogWriter));

        // 本地工具服务：地址由用户配置（可跨机器 / 跨端口），启用宽松 CORS
        builder.Services.AddCors(corsOptions => corsOptions.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        // 全局异常处理：未捕获异常统一 500 + ErrorView，不透出堆栈 / 内部路径
        builder.Services.AddLabelFrameExceptionHandler();

        var logStore = new SqliteLogStore(options.LogsDbPath);
        await logStore.InitializeAsync();
        builder.Services.AddSingleton(logStore);

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

        // 服务层扩展点：在全部注册完成后执行（测试 RemoveAll<IHostedService> 等需要看到完整注册列表）
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseCors();

        app.MapGet("/healthz", (ITransportManager transportManager, ITransportPluginRegistry registry) =>
            Results.Ok(new
            {
                service = "LabelFrame.WinHost",
                status = "ok",
                // 旧字段：连接方式（兼容旧前端徽标）；pluginId / displayText 精确透出当前插件
                transport = transportManager.CurrentConfig.Mode.ToString(),
                pluginId = transportManager.CurrentConfig.PluginId,
                displayText = registry.Describe(transportManager.CurrentConfig.PluginId, new TransportPluginParameters(transportManager.CurrentConfig.Params)),
            }));

        // ---- 模板库 / 调试出图（端点实现与 Server 共享，见 LabelFrame.Api.Endpoints）----
        app.MapTemplateApi(new TemplateApiOptions(
            templateStore,
            skiaRenderer,
            options.Dpi,
            JobErrorCodes.InvalidRequest,
            ApiErrorCodes.TemplateNotFound));

        // 图片资源解析统一：请求附带 base64 优先、按名回退本地模板库（两端点行为一致）
        app.MapRenderApi(new RenderApiOptions(templateStore, skiaRenderer, options.Dpi, JobErrorCodes.InvalidRequest));

        // ---- 宿主专属端点（分组实现见 Api/ 目录）----
        app.MapTransportApi();
        app.MapPluginApi();
        app.MapJobsApi();
        app.MapPrinterApi(options.Dpi);
        app.MapHostApi(hostInfo);

        MapWebUi(app, options);
        return app;
    }

    /// <summary>Web UI 静态托管：探测 web/dist（配置优先，含仓库开发路径），未找到仅提供 API。</summary>
    private static void MapWebUi(WebApplication app, HostOptions options)
    {
        var webUiPath = ResolveWebUiPath(options);
        if (webUiPath is null)
        {
            Console.WriteLine("[LabelFrame] 未找到 Web UI 构建产物（web/dist），仅提供 API。");
            return;
        }

        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webUiPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        // SPA fallback：未匹配 /api 的路径返回 index.html
        app.MapFallback(async context =>
        {
            var indexFile = Path.Combine(webUiPath, "index.html");
            if (!File.Exists(indexFile))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexFile);
        });
        Console.WriteLine($"[LabelFrame] Web UI: {webUiPath}");
    }

    private static string? ResolveWebUiPath(HostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.WebUiPath) && Directory.Exists(options.WebUiPath))
        {
            return options.WebUiPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "web", "dist"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "dist")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "web", "dist")),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
