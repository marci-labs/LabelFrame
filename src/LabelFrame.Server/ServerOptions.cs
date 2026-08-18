namespace LabelFrame.Server;

/// <summary>Server 配置：监听地址、数据库路径与历史清理保留期。</summary>
public sealed class ServerOptions
{
    /// <summary>默认监听地址（本机）。</summary>
    public const string DefaultListenUrl = "http://127.0.0.1:53961";

    /// <summary>
    /// 默认数据目录：Windows %ProgramData%\LabelFrame\server（服务账户下 LOCALAPPDATA 不可靠）；
    /// Linux /var/lib/labelframe/server（迭代 19，systemd 部署约定）；LABELFRAME_SERVER_* 环境变量优先。
    /// </summary>
    public static string DefaultDataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabelFrame", "server")
        : "/var/lib/labelframe/server";

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

    /// <summary>文本日志文件路径（为空不写文件；Linux 部署挂载到宿主机查看，迭代 19）。</summary>
    public string? LogFilePath { get; set; }

    /// <summary>产品版本（随迭代版本号更新；打包脚本 -Version 需保持一致）。</summary>
    public const string ProductVersion = "0.20.2";

    /// <summary>客户端安装包目录（迭代 22 §2.3：服务端统一分发客户端安装包；Windows %ProgramData%\\LabelFrame\\server\\client-packages；Linux /var/lib/labelframe/server/client-packages）。</summary>
    public static string DefaultClientPackagesPath => Path.Combine(DefaultDataDirectory, "client-packages");

    /// <summary>传输插件包目录（迭代 23 决策 2A：插件包上传服务端独立目录；Windows %ProgramData%\LabelFrame\server\plugin-packages；Linux /var/lib/labelframe/server/plugin-packages）。</summary>
    public static string DefaultPluginPackagesPath => Path.Combine(DefaultDataDirectory, "plugin-packages");

    /// <summary>传输插件包目录（存在即列出；目录直放文件或经 API 上传都支持，上传时解析 manifest 展示元数据）。</summary>
    public string PluginPackagesPath { get; set; } = DefaultPluginPackagesPath;
    /// <summary>客户端安装包目录（存在即列出；目录直放文件或经 API 上传都支持，决策 #71）。</summary>
    public string ClientPackagesPath { get; set; } = DefaultClientPackagesPath;

    /// <summary>默认服务端管理界面插件目录（Windows %ProgramData%\LabelFrame\server\plugins\web-ui；Linux /var/lib/labelframe/server/plugins/web-ui）。</summary>
    public static string DefaultWebUiPath => Path.Combine(DefaultDataDirectory, "plugins", "web-ui");

    /// <summary>服务端管理界面插件目录（目录存在即托管、放进去即时生效；为空 / 目录不存在 = 无头，不推翻决策 #53）。</summary>
    public string? WebUiPath { get; set; } = DefaultWebUiPath;

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

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE") is { } logFile)
        {
            LogFilePath = logFile;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_PLUGIN_PACKAGES") is { } pluginPackages)
        {
            PluginPackagesPath = string.IsNullOrWhiteSpace(pluginPackages) ? DefaultPluginPackagesPath : pluginPackages;
        }
        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_CLIENT_PACKAGES") is { } clientPackages)
        {
            ClientPackagesPath = string.IsNullOrWhiteSpace(clientPackages) ? DefaultClientPackagesPath : clientPackages;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_WEB_UI") is { } webUi)
        {
            // 环境变量为空 = 显式不启用插件；非空覆盖默认插件目录
            WebUiPath = string.IsNullOrWhiteSpace(webUi) ? null : webUi;
        }
    }
}
