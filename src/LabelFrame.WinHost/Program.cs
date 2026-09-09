using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Ui;
using Serilog;

namespace LabelFrame.WinHost;

/// <summary>WinHost：本地打印服务（作业队列 + HTTP API + 打印 Worker）。</summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        // 配置装配：appsettings.json 的 WinHost 节 + 环境变量覆盖 + --autostart（开机自启：托盘常驻不显示界面窗口）
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        var options = new HostOptions();
        builder.Configuration.GetSection("WinHost").Bind(options);
        options.ApplyEnvironmentOverrides();
        if (args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
        {
            options.OpenBrowser = false;
        }

#if !WINDOWS
        // Linux 首版是容器化无头测试客户端，传输能力固定为 Log。
        options.Transport = TransportMode.Log;
        options.OpenBrowser = false;
        options.EnableTray = false;
        options.WebUiPath = null;
#endif

#if WINDOWS
        // Serilog 文件日志（ILogger 逐张日志落盘；与 host.log 分开文件）
        // 文件名 app-20260818.log：Serilog.Sinks.File 的 {Date} 是字面量（不会替换），
        // 正确做法是 app-.log + RollingInterval.Day（Serilog 自动追加日期后缀）。
        var appLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "logs");
        builder.Host.UseSerilog((_, loggerConfig) => loggerConfig
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appLogDirectory, "app-.log"),
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));
#endif

        var hostLogWriter = OpenHostLogWriter(options);
        void HostInfo(string message)
        {
            try
            {
                hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                hostLogWriter.Flush();
#if !WINDOWS
                Console.WriteLine($"[LabelFrame] {message}");
#endif
            }
            catch
            {
                // 日志写入失败不影响启动
            }
        }

#if WINDOWS
        // 单实例（迭代 44）：已有实例在运行 → 通知其显示界面窗口后退出，不再走到端口监听（此前表现为端口占用启动失败）
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            HostInfo("检测到 LabelFrame 已在运行，已请求显示界面窗口，本次启动退出。");
            SingleInstanceGuard.SignalActivate();
            return;
        }
#endif

        HostInfo($"LabelFrame 启动：监听 {options.ListenUrl}，DPI {options.Dpi}，OpenBrowser={options.OpenBrowser}，ServerUrl={options.ServerUrl ?? "(未配置路由)"}");

        // 应用装配（DI + 全部端点 + Web UI 托管）；宿主层职责（界面壳 / 托盘 / 退出）留在 Main
        var app = await WinHostApp.BuildAsync(options, hostLogWriter, HostInfo);

#if WINDOWS
        // 界面壳（迭代 44，决策 #99）：窗口形态（WebView2）优先，运行时不可用回退浏览器；
        // OpenBrowser 配置语义演进为「启动时显示界面」（默认开），--autostart 仅托盘不弹界面（D4）
        var localUiUrl = new Uri(UiUrl.ToLocalUiUrl(options.ListenUrl));
        IUiShell? uiShell = null;
        if (options.OpenBrowser || options.EnableTray)
        {
            var windowShell = WindowUiShell.TryStart(
                localUiUrl,
                showInitially: options.OpenBrowser,
                hideOnClose: options.EnableTray,
                singleInstance.ActivateEvent,
                HostInfo);
            if (windowShell is null)
            {
                HostInfo("WebView2 运行时不可用，界面回退默认浏览器形态（可从微软官网安装 WebView2 运行时恢复窗口形态）。");
                uiShell = new BrowserUiShell(localUiUrl, openInitially: options.OpenBrowser, HostInfo);
            }
            else
            {
                uiShell = windowShell;
            }
        }
#endif

        app.Lifetime.ApplicationStopping.Register(() => HostInfo("ApplicationStopping"));
        app.Lifetime.ApplicationStopped.Register(() => HostInfo("ApplicationStopped"));

#if WINDOWS
        using var tray = new TrayIconService(HostInfo);
        if (options.EnableTray)
        {
            tray.Start(
                () => uiShell?.OpenUi(),
                () =>
                {
                    app.Lifetime.StopApplication();
                    return Task.CompletedTask;
                });
            HostInfo("系统托盘已启用（双击或右键托盘图标可打开界面，退出请用托盘菜单）。");
        }
#endif

#if WINDOWS
        var exitCode = 0;
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            // WinExe 无控制台：失败原因写入 host.log 并以消息框向用户展示中文原因（迭代 44 前只写日志，用户无感知）
            HostInfo($"LabelFrame 启动失败：{ex}");
            exitCode = 1;
            var friendly = DescribeStartupFailure(ex, options.ListenUrl);
            HostInfo(friendly);
            uiShell?.ShowFatalError(friendly);
        }
#else
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            HostInfo($"LabelFrame 启动失败：{ex}");
            Console.WriteLine($"[LabelFrame] {DescribeStartupFailure(ex, options.ListenUrl)}");
        }
#endif
        finally
        {
#if WINDOWS
            uiShell?.Dispose();
#endif
            HostInfo("宿主退出流程完成。");
#if WINDOWS
            Environment.Exit(exitCode);
#endif
        }
    }

    /// <summary>启动失败的用户可读原因（端口占用等常见场景转中文可行动提示，其余透出原始信息）。</summary>
    public static string DescribeStartupFailure(Exception exception, string listenUrl)
    {
        if (IsPortInUse(exception))
        {
            return $"LabelFrame 启动失败：{listenUrl} 端口被占用。请关闭占用该端口的程序，或修改安装目录下 appsettings.json 的 ListenUrl 后重试。";
        }

        return $"LabelFrame 启动失败：{exception.Message}（详情见本机日志 host.log）";
    }

    private static bool IsPortInUse(Exception exception)
    {
        return exception is Microsoft.AspNetCore.Connections.AddressInUseException
            || exception.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException
            || exception is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse,
            }
            || exception.InnerException is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse,
            };
    }

    internal sealed class UnsupportedStatusProvider : Core.Transport.IPrinterStatusProvider
    {
        public Task<Core.Transport.PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new Core.Transport.PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
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
}
