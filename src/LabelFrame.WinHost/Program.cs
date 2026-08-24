using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.Core.Logs;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Transport;
using Serilog;

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
        // 安装完成提示模式（迭代 20）：仅显示 TopMost 弹窗（MSI 原生弹窗会被 Windows 焦点策略挡到后台），
        // 选择「立即打开」后以普通模式重启宿主；不启动 Kestrel / 托盘。
        if (args.Contains(InstallFinishedPrompt.Flag))
        {
            InstallFinishedPrompt.RunAndMaybeLaunch();
            return;
        }

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
        // 迭代 23（决策 5A）：插件包上传端点大小上限 64MB（Kestrel 默认约 30MB，超出会返回 413 且无错误体）
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = PluginPackageLimits.MaxBytes;
        });
        // 迭代 24：Serilog 文件日志（ILogger 逐张日志落盘，供批间间隔冒烟验证；与 host.log 分开文件）
        // 文件名 app-20260818.log：Serilog.Sinks.File 的 {Date} 是字面量（不会替换），
        // 正确做法是 app-.log + RollingInterval.Day（Serilog 自动追加日期后缀），联调附五实证。
        var appLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "logs");
        builder.Host.UseSerilog((_, loggerConfig) => loggerConfig
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appLogDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

        var hostLogWriter = OpenHostLogWriter(options);

        // 迭代 22 传输插件化（决策 #67-69）：注册表 = Core 内置（log / tcp9100）+ WinHost 内置（winspool / zebra）+ 外部 DLL 目录扫描
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
            // 决策 6A：外部插件不允许覆盖内置插件 ID（冲突时 RegisterExternal 记日志跳过）
            if (transportRegistry.RegisterExternal(plugin, assemblyPath, hostLogWriter))
            {
                HostInfo($"已加载外部传输插件：{plugin.Id}（{plugin.DisplayName}，来自 {assemblyPath}）");
            }
        }

        var transportManager = new TransportManager(transportRegistry, pluginContext, options, hostLogWriter);
        void HostInfo(string message)
        {
            try
            {
                hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                hostLogWriter.Flush();
            }
            catch
            {
                // 日志写入失败不影响启动
            }
        }

        var hostConfigStore = new HostConfigStore(options.ConfigPath);
        builder.Services.AddSingleton(hostConfigStore);
        // 迭代 24：批次作业设置（用户级持久化 + 内存单例，保存即生效；单例注入 JobPrintWorker）
        var printSettingsStore = new PrintSettingsStore(options.PrintSettingsPath);
        var printSettings = new PrintSettings();
        printSettings.Update(printSettingsStore.Load());
        builder.Services.AddSingleton(printSettingsStore);
        builder.Services.AddSingleton(printSettings);
        var machineServerUrl = hostConfigStore.LoadServerUrl();
        if (!string.IsNullOrWhiteSpace(machineServerUrl) && !string.Equals(machineServerUrl, options.ServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            options.ServerUrl = machineServerUrl;
            HostInfo($"已加载机器级配置：ServerUrl={options.ServerUrl}");
        }

        HostInfo($"LabelFrame 启动：监听 {options.ListenUrl}，连接 {transportRegistry.Describe(transportManager.CurrentConfig.PluginId, new TransportPluginParameters(transportManager.CurrentConfig.Params))}，DPI {options.Dpi}，OpenBrowser={options.OpenBrowser}，ServerUrl={options.ServerUrl ?? "(未配置路由)"}");

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
            // 迭代 23 附二拍板：启动装配时的加载失败透出（已安装列表「加载失败 err + 原因」）
            pluginLoad.Errors.ToDictionary(e => e.AssemblyPath, e => e.Error, StringComparer.OrdinalIgnoreCase)));
        builder.Services.AddSingleton<ZplImageEncoder>();
        builder.Services.AddSingleton<IPrinterStatusProvider>(sp =>
            sp.GetRequiredService<ITransportManager>().CurrentTransport as IPrinterStatusProvider ?? new UnsupportedStatusProvider());
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
            sp.GetRequiredService<ILabelBitmapRenderer>(),
            sp.GetRequiredService<TemplateStore>(),
            sp.GetRequiredService<ITransportManager>(),
            hostLogWriter));

        // 本地工具服务：地址由用户配置（可跨机器 / 跨端口），启用宽松 CORS
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

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

        var app = builder.Build();

        app.UseCors();

        app.MapGet("/healthz", (ITransportManager transportManager, ITransportPluginRegistry registry) =>
            Results.Ok(new
            {
                service = "LabelFrame.WinHost",
                status = "ok",
                // 旧字段：连接方式（兼容旧前端徽标）；迭代 22 新增 pluginId / displayText 精确透出当前插件
                transport = transportManager.CurrentConfig.Mode.ToString(),
                pluginId = transportManager.CurrentConfig.PluginId,
                displayText = registry.Describe(transportManager.CurrentConfig.PluginId, new TransportPluginParameters(transportManager.CurrentConfig.Params)),
            }));

        // ---- 模板库 / 调试出图（端点实现与 Server 共享，见 LabelFrame.Api.Endpoints）----
        // 预览修复：DPI 取宿主配置（原硬编码 203，非 203 DPI 时预览与打印不一致）；渲染统一 Skia 同源；
        // 请求数据缺省时回退模板 testData（与 Server 一致）；模板不存在返回 LF_TPL_001（原误用 LF_JOB_001）。
        app.MapTemplateApi(new TemplateApiOptions(
            templateStore,
            skiaRenderer,
            options.Dpi,
            JobErrorCodes.InvalidRequest,
            ApiErrorCodes.TemplateNotFound));

        // 图片资源解析统一：请求附带 base64 优先、按名回退本地模板库（两端点行为一致）
        app.MapRenderApi(new RenderApiOptions(templateStore, skiaRenderer, options.Dpi, JobErrorCodes.InvalidRequest));

        // ---- 连接管理（迭代 15）：查询 / 切换 / 测试；单一连接生效，先测试后生效 ----
                // ---- 连接管理（迭代 22 传输插件化：pluginId + params + availablePlugins spec；旧字段 mode / availableModes 保留兼容）----
        app.MapGet("/api/transport", (ITransportManager transportManager, ITransportPluginRegistry registry) =>
            Results.Ok(ToTransportConfigDto(transportManager.CurrentConfig, registry)));

        // 已装配传输插件列表（含来源：内置 / 外部 DLL；排障用）
        app.MapGet("/api/transport/plugins", (ITransportPluginRegistry registry) =>
            Results.Ok(registry.ListPlugins().Select(ToTransportPluginDescriptorDto)));

        app.MapPost("/api/transport", async (Api.TransportApplyRequest? request, ITransportManager transportManager, ITransportPluginRegistry registry, CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.TransportInvalid, "请求体不能为空。"));
            }

            TransportConfig config;
            if (!string.IsNullOrWhiteSpace(request.PluginId))
            {
                // 新格式：pluginId + params 字典
                config = new TransportConfig
                {
                    PluginId = request.PluginId,
                    Params = request.Params is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(request.Params, StringComparer.OrdinalIgnoreCase),
                };
            }
            else if (!string.IsNullOrWhiteSpace(request.Mode))
            {
                // 旧格式兼容：mode + 平铺参数 → 迁移为 pluginId + params（决策 #69）
                if (!Enum.TryParse<TransportMode>(request.Mode, ignoreCase: true, out var mode))
                {
                    return Results.BadRequest(new ErrorView(ApiErrorCodes.TransportInvalid, $"不支持的连接方式：{request.Mode}。"));
                }

                config = new TransportConfig
                {
                    Mode = mode,
                    TcpHost = request.TcpHost ?? transportManager.CurrentConfig.TcpHost,
                    TcpPort = request.TcpPort ?? transportManager.CurrentConfig.TcpPort,
                    PrinterName = request.PrinterName ?? transportManager.CurrentConfig.PrinterName,
                    ZebraKind = request.ZebraKind is not null && Enum.TryParse<ZebraTransportKind>(request.ZebraKind, ignoreCase: true, out var zebraKind)
                        ? zebraKind
                        : transportManager.CurrentConfig.ZebraKind,
                    ZebraUsbName = request.ZebraUsbName ?? transportManager.CurrentConfig.ZebraUsbName,
                };
                config.MigrateFromLegacy();
            }
            else
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.TransportInvalid, "缺少 pluginId 或 mode。"));
            }

            var result = await transportManager.ApplyAsync(config, request.TestOnly ?? false, ct);
            return Results.Ok(new TransportApplyResponse(result.Ok, result.Message, ToTransportConfigDto(result.Config, registry)));
        });

        // ---- 插件安装 / 卸载（迭代 23 §5.2：插件包上传服务端 → 客户端下载安装 / 卸载；安装 / 卸载 = 写文件 + 重启生效）----
        app.MapGet("/api/plugins/installed", (Transport.PluginInstaller installer) => Results.Ok(installer.ListInstalled()));

        app.MapPost("/api/plugins/install", async (IFormFile file, Transport.PluginInstaller installer, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, "请选择要安装的插件包。"));
            }

            try
            {
                var view = await installer.InstallAsync(file.OpenReadStream(), file.FileName, ct);
                return Results.Ok(new { ok = true, message = $"插件「{view.Name} {view.Version}」已安装，重启客户端后生效。", plugin = view });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginBusy, ex.Message));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, $"插件包无效：{ex.Message}"));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInstallFailed, $"安装失败：{ex.Message}"));
            }
        }).DisableAntiforgery();

        app.MapPost("/api/plugins/uninstall", (Api.UninstallPluginRequest? request, Transport.PluginInstaller installer) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.PluginId))
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, "缺少插件 ID。"));
            }

            try
            {
                installer.Uninstall(request.PluginId);
                return Results.Ok(new { ok = true, message = $"插件「{request.PluginId}」已卸载，重启客户端后生效。" });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginBusy, ex.Message));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, ex.Message));
            }
        }).DisableAntiforgery();

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

            var jobView = EnrichPrintInfo(JobViews.From(result.Job), result.Job!.Id, transportManager);
            return result.Created
                ? Results.Accepted((string?)null, jobView)
                : Results.Ok(jobView);
        });

        // 作业列表（迭代 18 B10：作业历史页；单机降级用，形状与 Server 兼容）
        app.MapGet("/api/jobs", async (int? limit, ILabelJobStore store, ITransportManager transportManager, CancellationToken ct) =>
        {
            var jobs = await store.ListRecentAsync(Math.Clamp(limit ?? 100, 1, 500), ct);
            return Results.Ok(jobs.Select(j => EnrichPrintInfo(JobViews.From(j), j.Id, transportManager)));
        });

        app.MapGet("/api/jobs/{jobId}", async (string jobId, LabelJobQueue queue, ITransportManager transportManager, CancellationToken ct) =>
        {
            var job = await queue.GetAsync(jobId, ct);
            return job is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。"))
                : Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager));
        });

        app.MapPost("/api/jobs/{jobId}/suspend", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.SuspendAsync, transportManager, ct));

        app.MapPost("/api/jobs/{jobId}/resume", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.ResumeAsync, transportManager, ct));

        app.MapPost("/api/jobs/{jobId}/cancel", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.CancelAsync, transportManager, ct));

        app.MapPost("/api/jobs/{jobId}/items/{itemIndex:int}/retry", async (string jobId, int itemIndex, LabelJobQueue queue, ITransportManager transportManager, CancellationToken ct) =>
        {
            try
            {
                var job = await queue.RetryItemAsync(jobId, itemIndex, ct);
                return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager));
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

        // ---- Excel 模板生成 / 设备日志 / Excel 数据导入（端点实现与 Server 共享，见 LabelFrame.Api.Endpoints）----
        app.MapImportApi(new ImportApiOptions(JobErrorCodes.InvalidRequest));
        app.MapLogApi(new LogApiOptions(logStore, JobErrorCodes.InvalidRequest));

        // ---- 打印机测试页 / 在线状态 ----
        app.MapGet("/api/printer/status", async (ITransportManager transportManager, CancellationToken ct) =>
            Results.Ok(await (transportManager.CurrentTransport as IPrinterStatusProvider ?? new UnsupportedStatusProvider()).GetStatusAsync(ct)));

        app.MapPost("/api/printer/test", async (ITransportManager transportManager, ILabelBitmapRenderer renderer, ZplImageEncoder encoder, CancellationToken ct) =>
        {
            // 测试页与正式打印同源：Skia 渲染整版位图经 ^GF 发送（图片打印语义，无矢量 ZPL）
            var document = new LabelDocument
            {
                Layout = new LabelLayout
                {
                    Name = "test",
                    ContractName = "test",
                    ContractVersion = "1.0",
                    WidthMm = 40,
                    HeightMm = 20,
                    Elements =
                    [
                        new LabelTextElement { Literal = "LabelFrame Test", XMm = 2, YMm = 4, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 36, TextAlign = LabelTextAlign.Center },
                    ],
                },
                Data = new Dictionary<string, string>(),
            };
            var bitmap = renderer.RenderLabelBitmap(document, options.Dpi);
            var command = encoder.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, options.Dpi);
            await transportManager.CurrentTransport.SendAsync(command, ct);
            return Results.Ok(new { sent = true, bytes = System.Text.Encoding.UTF8.GetByteCount(command) });
        });

        // ---- 机器级配置（迭代 18：/api/host/config，前端读写 ServerUrl；仅回环可写）----
        app.MapGet("/api/host/config", (HostOptions options) =>
            Results.Ok(new Api.HostConfigDto(options.ServerUrl ?? string.Empty, options.DeviceId, options.DeviceName, LocalIpAddresses.EnumerateIpv4())));

        app.MapPost("/api/host/config", (HttpContext context, Api.HostConfigRequest? request, HostConfigStore store, HostOptions options) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            {
                return Results.Forbid();
            }

            if (request is null || string.IsNullOrWhiteSpace(request.ServerUrl))
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少 serverUrl。"));
            }

            var serverUrl = request.ServerUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "serverUrl 格式不正确（http://主机:端口）。"));
            }

            store.SaveServerUrl(serverUrl);
            options.ServerUrl = serverUrl;
            HostInfo($"机器级配置已更新：ServerUrl={serverUrl}");
            return Results.Ok(new Api.HostConfigDto(serverUrl, options.DeviceId, options.DeviceName, LocalIpAddresses.EnumerateIpv4()));
        });
        // ---- 批次作业设置（迭代 24）：GET/POST /api/host/print-settings；仅回环可写；保存即生效 ----
        app.MapGet("/api/host/print-settings", (PrintSettings printSettings) =>
            Api.PrintSettingsApi.Get(printSettings));

        app.MapPost("/api/host/print-settings", (HttpContext context, PrintSettingsDto? request, PrintSettingsStore store, PrintSettings printSettings) =>
            Api.PrintSettingsApi.Post(context.Connection.RemoteIpAddress, request, store, printSettings));

        // ---- 本机服务关闭（Web UI 设置页「退出程序」用）----
        app.MapPost("/api/host/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            {
                return Results.Forbid();
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                HostInfo("收到关闭请求，正在停止宿主…");
                lifetime.StopApplication();
                // 托盘线程（WinForms 消息循环）可能阻止 RunAsync 自然返回，延迟后强制退出
                await Task.Delay(500);
                HostInfo("关闭完成。");
                Environment.Exit(0);
            });
            return Results.Ok(new { shuttingDown = true });
        });

        // ---- Web UI 静态托管（前端构建产物 web/dist）----
        var webUiPath = ResolveWebUiPath(options);
        if (webUiPath is not null)
        {
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
        else
        {
            Console.WriteLine("[LabelFrame] 未找到 Web UI 构建产物（web/dist），仅提供 API。");
        }

        // 单机模式：启动后自动打开默认浏览器
        if (options.OpenBrowser)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ToLocalUiUrl(options.ListenUrl),
                        UseShellExecute = true,
                    });
                    HostInfo($"已尝试打开浏览器：{ToLocalUiUrl(options.ListenUrl)}");
                }
                catch (Exception ex)
                {
                    HostInfo($"打开浏览器失败：{ex.Message}");
                }
            });
        }

        app.Lifetime.ApplicationStopping.Register(() => HostInfo("ApplicationStopping"));
        app.Lifetime.ApplicationStopped.Register(() => HostInfo("ApplicationStopped"));

        var tray = new TrayIconService(HostInfo);
        if (options.EnableTray)
        {
            tray.Start(ToLocalUiUrl(options.ListenUrl), () =>
            {
                app.Lifetime.StopApplication();
                return Task.CompletedTask;
            });
            HostInfo("系统托盘已启用（右键托盘图标可退出）。");
        }

        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            // WinExe 无窗口：失败信息写入 host.log（%LOCALAPPDATA%\LabelFrame\host.log）
            HostInfo($"LabelFrame 启动失败：{ex}");
            HostInfo("如端口被占用，可修改 appsettings.json 的 ListenUrl 或结束占用进程后重试。");
            throw;
        }
        finally
        {
            tray.Dispose();
            HostInfo("宿主退出流程完成。");
            Environment.Exit(0);
        }
    }


    /// <summary>解析 Web UI 静态目录：配置优先，否则探测常见位置（含仓库开发路径）。</summary>
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

    /// <summary>Log 模拟打印：把 PNG 目录与张数附到作业视图，便于前端展示「打印图片在哪」。</summary>
    private static JobView EnrichPrintInfo(JobView view, string jobId, ITransportManager transportManager)
    {
        if (transportManager.CurrentConfig.Mode != TransportMode.Log)
        {
            return view;
        }

        var dir = GetLogPrintDir(jobId);
        var count = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
        return view with { PrintImageDir = dir, PrintImageCount = count };
    }

    private static string GetLogPrintDir(string jobId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "print",
        jobId);

    private static async Task<IResult> TransitionAsync(
        string jobId,
        Func<string, CancellationToken, Task<LabelJob>> action,
        ITransportManager transportManager,
        CancellationToken ct)
    {
        try
        {
            var job = await action(jobId, ct);
            return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager));
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

    /// <summary>本地 UI 打开地址：通配监听（0.0.0.0 / * / + / [::]）规范化为 127.0.0.1，避免浏览器/托盘跳到 0.0.0.0。</summary>
    private static string ToLocalUiUrl(string listenUrl)
    {
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var uri))
        {
            return listenUrl;
        }

        if (uri.Host is "0.0.0.0" or "*" or "+" or "::" or "[::]")
        {
            var builder = new UriBuilder(uri) { Host = "127.0.0.1" };
            return builder.Uri.ToString();
        }

        return listenUrl;
    }

    private sealed class UnsupportedStatusProvider : IPrinterStatusProvider
    {
        public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
    }

    /// <summary>Log 传输写入宿主日志文件（WinExe 无控制台，避免 Console 不可用）。</summary>
    private static TextWriter OpenHostLogWriter(HostOptions options)
    {
        try
        {
            var directory = Path.GetDirectoryName(options.HostLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var writer = new StreamWriter(options.HostLogPath, append: true) { AutoFlush = true };
            return TextWriter.Synchronized(writer);
        }
        catch
        {
            return TextWriter.Null;
        }
    }

    /// <summary>TransportConfig → API DTO（params 含全部字段，前端只展示当前模式所需）。</summary>
    /// <summary>TransportConfig → API DTO（迭代 22：pluginId + params 字典 + displayText + availablePlugins；旧字段 mode / availableModes 保留兼容）。</summary>
    private static TransportConfigDto ToTransportConfigDto(TransportConfig config, ITransportPluginRegistry registry)
    {
        var plugin = registry.GetPlugin(config.PluginId);
        return new TransportConfigDto(
            config.PluginId,
            plugin?.DisplayName ?? config.PluginId,
            registry.Describe(config.PluginId, new TransportPluginParameters(config.Params)),
            new Dictionary<string, string>(config.Params, StringComparer.OrdinalIgnoreCase),
            registry.ListPlugins().Select(ToTransportPluginDescriptorDto).ToList(),
            config.Mode.ToString(),
            new[] { "Log", "Tcp", "WindowsDriver", "Zebra" });
    }

    /// <summary>插件描述 → API DTO（参数规格按 TransportParameterSpec 平铺）。</summary>
    private static TransportPluginDescriptorDto ToTransportPluginDescriptorDto(TransportPluginDescriptor plugin) => new(
        plugin.Id,
        plugin.DisplayName,
        plugin.Description,
        plugin.Parameters.Select(p => new TransportPluginParameterDto(
            p.Key,
            p.Label,
            p.Type.ToString(),
            p.Required,
            p.DefaultValue,
            p.Options?.Select(o => new TransportParameterOptionDto(o.Value, o.Label)).ToList(),
            p.Hint)).ToList(),
        plugin.IsExternal,
        plugin.AssemblyPath);
}
