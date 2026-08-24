using System.Globalization;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace LabelFrame.Core.Data;

/// <summary>
/// SQLite 存储公共基建（jobs / templates / logs / server 四存储共享）：
/// provider 初始化、连接串构造、WAL 连接打开、UTC 时间往返格式化。
/// </summary>
public static class SqliteSupport
{
    private static int _initialized;

    /// <summary>确保 SQLitePCLRaw 的 e_sqlite3 provider 已设置（进程内仅一次，幂等）。</summary>
    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            raw.SetProvider(new SQLite3Provider_e_sqlite3());
        }
    }

    /// <summary>构造存储连接串：绝对路径（父目录不存在自动创建）+ busy 超时 5s + 连接池。</summary>
    public static string BuildConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        EnsureInitialized();
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            DefaultTimeout = 5,
            Pooling = true,
        }.ToString();
    }

    /// <summary>
    /// 打开连接并启用 WAL（写前日志）：读写并发不再互相阻塞、写性能更好；
    /// 对已启用 WAL 的库为幂等 no-op；WAL 不可用（如只读卷）时静默回退默认日志模式，不阻断。
    /// </summary>
    public static async Task<SqliteConnection> OpenAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // WAL 不可用不阻断业务
        }

        return connection;
    }

    /// <summary>UTC 时间 → 可往返的 ISO-8601 文本（存储统一格式）。</summary>
    public static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>ISO-8601 文本 → DateTimeOffset（往返语义解析）。</summary>
    public static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
