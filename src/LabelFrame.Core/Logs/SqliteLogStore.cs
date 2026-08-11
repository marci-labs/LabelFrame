using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Logs;

/// <summary>日志条目（PDA / 设备回传）。</summary>
public sealed record LogEntry(string DeviceId, DateTimeOffset Time, string Line);

/// <summary>SQLite 日志存储：设备日志回传与查询（迭代 11：PDA 调试用）。</summary>
public sealed class SqliteLogStore
{
    private readonly string _connectionString;

    /// <summary>创建日志存储（默认 %LOCALAPPDATA%\LabelFrame\logs.db）。</summary>
    public SqliteLogStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "logs.db");
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            DefaultTimeout = 5,
            Pooling = true,
        }.ToString();
        // SQLite provider 由宿主（WinHost 启动时）初始化
    }

    /// <summary>建表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS logs (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                time      TEXT NOT NULL,
                line      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_logs_device_time ON logs(device_id, time);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>追加设备日志。</summary>
    public async Task AppendAsync(string deviceId, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || lines.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO logs (device_id, time, line)
            VALUES ($device, $time, $line);
            """;
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$time", Format(now));
        command.Parameters.AddWithValue("$line", string.Join(Environment.NewLine, lines));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>查询日志（可按设备 / 时间过滤，最多返回 500 条）。</summary>
    public async Task<IReadOnlyList<LogEntry>> QueryAsync(
        string? deviceId = null,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = "SELECT device_id, time, line FROM logs";
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            conditions.Add("device_id = $device");
            command.Parameters.AddWithValue("$device", deviceId);
        }

        if (since is not null)
        {
            conditions.Add("time > $since");
            command.Parameters.AddWithValue("$since", Format(since.Value));
        }

        if (conditions.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", conditions);
        }

        sql += " ORDER BY id DESC LIMIT 500;";
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<LogEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new LogEntry(
                reader.GetString(0),
                Parse(reader.GetString(1)),
                reader.GetString(2)));
        }

        return entries;
    }

    /// <summary>删除早于截止时间的日志（历史清理用）。</summary>
    public async Task<int> DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM logs WHERE time < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", Format(cutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
