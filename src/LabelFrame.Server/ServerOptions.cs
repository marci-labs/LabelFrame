namespace LabelFrame.Server;

/// <summary>Server 配置：监听地址、数据库路径与历史清理保留期。</summary>
public sealed class ServerOptions
{
    /// <summary>默认监听地址（本机）。</summary>
    public const string DefaultListenUrl = "http://127.0.0.1:53961";

    /// <summary>默认数据目录（%ProgramData%\LabelFrame\server；Windows 服务以 LocalSystem 运行时 LOCALAPPDATA 指向系统账户目录，不可靠）。</summary>
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabelFrame",
        "server");

    /// <summary>默认数据库路径（%ProgramData%\LabelFrame\server\server.db）。</summary>
    public static string DefaultDatabasePath => Path.Combine(DefaultDataDirectory, "server.db");

    /// <summary>监听地址。</summary>
    public string ListenUrl { get; set; } = DefaultListenUrl;

    /// <summary>SQLite 数据库路径。</summary>
    public string DatabasePath { get; set; } = DefaultDatabasePath;

    /// <summary>模板库路径（默认 %ProgramData%\LabelFrame\server\templates.db；与单机 WinHost 数据隔离）。</summary>
    public string TemplatesDbPath { get; set; } = Path.Combine(DefaultDataDirectory, "templates.db");

    /// <summary>设备日志库路径（默认 %ProgramData%\LabelFrame\server\logs.db）。</summary>
    public string LogsDbPath { get; set; } = Path.Combine(DefaultDataDirectory, "logs.db");

    /// <summary>渲染 DPI（调试出图 / 预览默认 203）。</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>终态作业（Completed / Failed）保留天数，超过则定期清理；非终态作业不清理。</summary>
    public int JobRetentionDays { get; set; } = 30;

    /// <summary>设备日志保留天数，超过则定期清理。</summary>
    public int LogRetentionDays { get; set; } = 90;

    /// <summary>历史清理周期（小时）。</summary>
    public int CleanupIntervalHours { get; set; } = 24;

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

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_DPI") is { } dpi && int.TryParse(dpi, out var dpiValue))
        {
            Dpi = dpiValue;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_JOB_RETENTION_DAYS") is { } jobDays && int.TryParse(jobDays, out var jobRetention))
        {
            JobRetentionDays = jobRetention;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOG_RETENTION_DAYS") is { } logDays && int.TryParse(logDays, out var logRetention))
        {
            LogRetentionDays = logRetention;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_CLEANUP_INTERVAL_HOURS") is { } hours && int.TryParse(hours, out var intervalHours))
        {
            CleanupIntervalHours = intervalHours;
        }
    }
}
