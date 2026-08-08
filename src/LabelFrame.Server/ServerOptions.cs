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
    }
}