namespace LabelFrame.Server;

/// <summary>Server 配置：监听地址与数据库路径。</summary>
public sealed class ServerOptions
{
    /// <summary>默认监听地址（本机）。</summary>
    public const string DefaultListenUrl = "http://127.0.0.1:53961";

    /// <summary>默认数据库路径（%LOCALAPPDATA%\LabelFrame\server.db）。</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "server.db");

    /// <summary>监听地址。</summary>
    public string ListenUrl { get; set; } = DefaultListenUrl;

    /// <summary>SQLite 数据库路径。</summary>
    public string DatabasePath { get; set; } = DefaultDatabasePath;

    /// <summary>模板库路径（默认 %LOCALAPPDATA%\\LabelFrame\\server\\templates.db；与单机 WinHost 数据隔离）。</summary>
    public string TemplatesDbPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "server",
        "templates.db");

    /// <summary>设备日志库路径（默认 %LOCALAPPDATA%\\LabelFrame\\server\\logs.db）。</summary>
    public string LogsDbPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "server",
        "logs.db");

    /// <summary>Web UI 静态目录（前端构建产物，为空时自动探测 web/dist）。</summary>
    public string? WebUiPath { get; set; }

    /// <summary>渲染 DPI（调试出图 / 预览默认 203）。</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>应用 LABELFRAME_SERVER_* 环境变量覆盖。</summary>
    public void ApplyEnvironmentOverrides()
    {
        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LISTEN") is { } listen)
        {
            ListenUrl = listen;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_DB") is { } db)
        {
            DatabasePath = db;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB") is { } templatesDb)
        {
            TemplatesDbPath = templatesDb;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB") is { } logsDb)
        {
            LogsDbPath = logsDb;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_WEB_UI") is { } webUi)
        {
            WebUiPath = webUi;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_DPI") is { } dpi && int.TryParse(dpi, out var dpiValue))
        {
            Dpi = dpiValue;
        }
    }
}