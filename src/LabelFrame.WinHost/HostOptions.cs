using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost;

/// <summary>传输模式。</summary>
public enum TransportMode
{
    /// <summary>日志模拟（默认，联调用）。</summary>
    Log,

    /// <summary>TCP 9100 网络打印机。</summary>
    Tcp,

    /// <summary>Windows 驱动（USB / 驱动安装的打印机），raw 指令。</summary>
    WindowsDriver,

    /// <summary>Zebra 官方 Link-OS SDK（TCP / USB / 驱动统一连接）。</summary>
    Zebra,
}

/// <summary>WinHost 配置：端口、数据库、传输、中文渲染。</summary>
public sealed class HostOptions
{
    /// <summary>默认监听地址（仅本机）。</summary>
    public const string DefaultListenUrl = "http://127.0.0.1:53960";

    /// <summary>默认数据库目录（%LOCALAPPDATA%\LabelFrame）。</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "jobs.db");

    /// <summary>Kestrel 监听地址。</summary>
    public string ListenUrl { get; set; } = DefaultListenUrl;

    /// <summary>SQLite 数据库文件路径。</summary>
    public string DatabasePath { get; set; } = DefaultDatabasePath;

    /// <summary>模板库数据库路径（默认 %LOCALAPPDATA%\\LabelFrame\\templates.db）。</summary>
    public string TemplatesDbPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "templates.db");

    /// <summary>传输模式。</summary>
    public TransportMode Transport { get; set; } = TransportMode.Log;

    /// <summary>Zebra SDK 连接类型（Transport=Zebra 时生效）。</summary>
    public ZebraTransportKind ZebraKind { get; set; } = ZebraTransportKind.Tcp;

    /// <summary>Zebra USB 打印机名（为空时自动发现第一台）。</summary>
    public string ZebraUsbName { get; set; } = string.Empty;

    /// <summary>Server 地址（如 http://127.0.0.1:53921）；为空则不启用路由。</summary>
    public string? ServerUrl { get; set; }

    /// <summary>注册到 Server 的设备标识。</summary>
    public string DeviceId { get; set; } = Environment.MachineName;

    /// <summary>注册到 Server 的设备展示名。</summary>
    public string DeviceName { get; set; } = Environment.MachineName;

    /// <summary>Server 轮询间隔（秒）。</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>TCP 打印机主机 / IP。</summary>
    public string TcpHost { get; set; } = "127.0.0.1";

    /// <summary>TCP 打印机端口。</summary>
    public int TcpPort { get; set; } = 9100;

    /// <summary>Windows 驱动打印机名（如 "ZDesigner ZD421-203dpi ZPL"）。</summary>
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>打印机 DPI。</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>中文渲染字体族（默认微软雅黑）。</summary>
    public string FontFamily { get; set; } = "Microsoft YaHei";

    /// <summary>可选中文字体文件路径（内嵌 / 本地字体，为空时用系统字体）。</summary>
    public string? FontFilePath { get; set; }

    /// <summary>应用 LABELFRAME_* 环境变量覆盖（优先级最高）。</summary>
    public void ApplyEnvironmentOverrides()
    {
        if (GetEnv("LABELFRAME_LISTEN") is { } listen)
        {
            ListenUrl = listen;
        }

        if (GetEnv("LABELFRAME_DB") is { } db)
        {
            DatabasePath = db;
        }

        if (GetEnv("LABELFRAME_TEMPLATES_DB") is { } templatesDb)
        {
            TemplatesDbPath = templatesDb;
        }

        if (GetEnv("LABELFRAME_TRANSPORT") is { } transport)
        {
            Transport = Enum.Parse<TransportMode>(transport, ignoreCase: true);
        }

        if (GetEnv("LABELFRAME_TCP_HOST") is { } tcpHost)
        {
            TcpHost = tcpHost;
        }

        if (GetEnv("LABELFRAME_TCP_PORT") is { } tcpPort && int.TryParse(tcpPort, out var port))
        {
            TcpPort = port;
        }

        if (GetEnv("LABELFRAME_PRINTER") is { } printer)
        {
            PrinterName = printer;
        }

        if (GetEnv("LABELFRAME_ZEBRA_KIND") is { } zebraKind)
        {
            ZebraKind = Enum.Parse<ZebraTransportKind>(zebraKind, ignoreCase: true);
        }

        if (GetEnv("LABELFRAME_ZEBRA_USB") is { } zebraUsb)
        {
            ZebraUsbName = zebraUsb;
        }

        if (GetEnv("LABELFRAME_SERVER_URL") is { } serverUrl)
        {
            ServerUrl = serverUrl;
        }

        if (GetEnv("LABELFRAME_DEVICE_ID") is { } deviceId)
        {
            DeviceId = deviceId;
        }

        if (GetEnv("LABELFRAME_DEVICE_NAME") is { } deviceName)
        {
            DeviceName = deviceName;
        }

        if (GetEnv("LABELFRAME_POLL_INTERVAL") is { } poll && int.TryParse(poll, out var pollSeconds))
        {
            PollIntervalSeconds = pollSeconds;
        }

        if (GetEnv("LABELFRAME_DPI") is { } dpi && int.TryParse(dpi, out var dpiValue))
        {
            Dpi = dpiValue;
        }

        if (GetEnv("LABELFRAME_FONT") is { } font)
        {
            FontFamily = font;
        }

        if (GetEnv("LABELFRAME_FONT_FILE") is { } fontFile)
        {
            FontFilePath = fontFile;
        }
    }

    private static string? GetEnv(string name) => Environment.GetEnvironmentVariable(name);
}