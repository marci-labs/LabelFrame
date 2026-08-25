using System.Text.Json;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.WinHost.Transport;
using Serilog;

namespace LabelFrame.WinHost;

/// <summary>WinHost：本地打印服务（作业队列 + HTTP API + 打印 Worker）。</summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static async Task Main(string[] args)
    {
        // 配置装配：appsettings.json 的 WinHost 节 + 环境变量覆盖 + --autostart（开机自启：托盘常驻不拉浏览器）
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

        var hostLogWriter = OpenHostLogWriter(options);
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

        HostInfo($"LabelFrame 启动：监听 {options.ListenUrl}，DPI {options.Dpi}，OpenBrowser={options.OpenBrowser}，ServerUrl={options.ServerUrl ?? "(未配置路由)"}");

        // 应用装配（DI + 全部端点 + Web UI 托管）；宿主层职责（托盘 / 浏览器 / 退出）留在 Main
        var app = await WinHostApp.BuildAsync(options, hostLogWriter, HostInfo);

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
