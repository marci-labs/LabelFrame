using System.Globalization;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace LabelFrame.Server;

/// <summary>Server 的 SQLite 存储：设备目录 + 作业表。</summary>
public sealed class ServerDb
{
    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS devices (
            id            TEXT PRIMARY KEY,
            name          TEXT NOT NULL,
            registered_at TEXT NOT NULL,
            last_seen_at  TEXT NOT NULL,
            last_ip       TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS server_jobs (
            id              TEXT PRIMARY KEY,
            request_id      TEXT NOT NULL UNIQUE,
            target_device_id TEXT NOT NULL,
            status          TEXT NOT NULL,
            created_at      TEXT NOT NULL,
            claimed_at      TEXT NULL,
            finished_at     TEXT NULL,
            total_items     INTEGER NOT NULL,
            completed_items INTEGER NOT NULL DEFAULT 0,
            failed_items    INTEGER NOT NULL DEFAULT 0,
            error_message   TEXT NULL,
            payload_json    TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_server_jobs_status_device ON server_jobs(status, target_device_id);
        CREATE INDEX IF NOT EXISTS ix_server_jobs_device ON server_jobs(target_device_id, created_at);
        """;

    private static int _initialized;

    private readonly string _connectionString;

    /// <summary>创建 Server 存储。</summary>
    public ServerDb(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
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

        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            raw.SetProvider(new SQLite3Provider_e_sqlite3());
        }
    }

    /// <summary>建表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTablesSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await MigrateDevicesLastIpAsync(connection, cancellationToken);
    }

    /// <summary>旧库兼容迁移：devices 表缺少 last_ip 列时补列（已存在则跳过；失败静默忽略，不影响启动）。</summary>
    private static async Task MigrateDevicesLastIpAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasLastIp = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(devices);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "last_ip", StringComparison.OrdinalIgnoreCase))
                {
                    hasLastIp = true;
                    break;
                }
            }
        }

        if (hasLastIp)
        {
            return;
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE devices ADD COLUMN last_ip TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 已存在列等竞态 / 约束差异：静默忽略，保持旧库可启动
        }
    }

    /// <summary>注册 / 更新设备并刷新心跳。</summary>
    public async Task<Device> UpsertDeviceAsync(Device device, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO devices (id, name, registered_at, last_seen_at, last_ip)
            VALUES ($id, $name, $registeredAt, $lastSeenAt, $lastIp)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                last_seen_at = excluded.last_seen_at,
                last_ip = excluded.last_ip;
            """;
        command.Parameters.AddWithValue("$id", device.Id);
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$registeredAt", Format(device.RegisteredAt));
        command.Parameters.AddWithValue("$lastSeenAt", Format(device.LastSeenAt));
        command.Parameters.AddWithValue("$lastIp", (object?)device.LastIp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return device;
    }

    /// <summary>刷新设备心跳。</summary>
    public async Task TouchDeviceAsync(string deviceId, DateTimeOffset now, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE devices SET last_seen_at = $now, last_ip = $lastIp WHERE id = $id;";
        command.Parameters.AddWithValue("$now", Format(now));
        command.Parameters.AddWithValue("$lastIp", (object?)lastIp ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", deviceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>查询设备。</summary>
    public async Task<Device?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, registered_at, last_seen_at, last_ip FROM devices WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Device
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            RegisteredAt = Parse(reader.GetString(2)),
            LastSeenAt = Parse(reader.GetString(3)),
            LastIp = reader.IsDBNull(4) ? null : reader.GetString(4),
        };
    }

    /// <summary>设备列表。</summary>
    public async Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var devices = new List<Device>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, registered_at, last_seen_at, last_ip FROM devices ORDER BY registered_at;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new Device
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                RegisteredAt = Parse(reader.GetString(2)),
                LastSeenAt = Parse(reader.GetString(3)),
                LastIp = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return devices;
    }

    /// <summary>创建作业；requestId 已存在时返回已有作业。</summary>
    /// <summary>按 last_ip 精确查找设备（忽略大小写；未找到返回 null）。</summary>
    public async Task<Device?> FindDeviceByIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, registered_at, last_seen_at, last_ip FROM devices WHERE last_ip = $ip COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("$ip", ip);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Device
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            RegisteredAt = Parse(reader.GetString(2)),
            LastSeenAt = Parse(reader.GetString(3)),
            LastIp = reader.IsDBNull(4) ? null : reader.GetString(4),
        };
    }


    public async Task<ServerJob?> CreateJobAsync(ServerJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO server_jobs
                (id, request_id, target_device_id, status, created_at, claimed_at, finished_at,
                 total_items, completed_items, failed_items, error_message, payload_json)
            VALUES
                ($id, $requestId, $targetDeviceId, $status, $createdAt, NULL, NULL,
                 $totalItems, 0, 0, NULL, $payloadJson);
            """;
        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$requestId", job.RequestId);
        command.Parameters.AddWithValue("$targetDeviceId", job.TargetDeviceId);
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$createdAt", Format(job.CreatedAt));
        command.Parameters.AddWithValue("$totalItems", job.TotalItems);
        command.Parameters.AddWithValue("$payloadJson", job.PayloadJson);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        return inserted == 0 ? await GetJobByRequestIdAsync(job.RequestId, cancellationToken) : job;
    }

    /// <summary>按作业标识查询。</summary>
    public Task<ServerJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(jobId, byRequestId: false, cancellationToken);

    /// <summary>按幂等键查询。</summary>
    public Task<ServerJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(requestId, byRequestId: true, cancellationToken);

    /// <summary>领取：把目标设备的 Pending 作业置为 Claimed，返回载荷。</summary>
    public async Task<IReadOnlyList<ServerJob>> ClaimPendingJobsAsync(
        string deviceId,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var jobs = new List<ServerJob>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id FROM server_jobs
                WHERE status = $pending AND target_device_id = $deviceId
                ORDER BY created_at, id LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$pending", ServerJobStatus.Pending.ToString());
            command.Parameters.AddWithValue("$deviceId", deviceId);
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                jobs.Add((await GetJobCoreAsync(reader.GetString(0), byRequestId: false, cancellationToken))!);
            }
        }

        foreach (var job in jobs)
        {
            await SetJobClaimedAsync(connection, job.Id, now, cancellationToken);
        }

        return jobs;
    }

    /// <summary>更新作业结果。</summary>
    public async Task<ServerJob?> UpdateJobResultAsync(
        string jobId,
        ServerJobStatus status,
        int completedItems,
        int failedItems,
        string? errorMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE server_jobs
            SET status = $status, completed_items = $completedItems, failed_items = $failedItems,
                error_message = $errorMessage, finished_at = $finishedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$completedItems", completedItems);
        command.Parameters.AddWithValue("$failedItems", failedItems);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$finishedAt", Format(now));
        command.Parameters.AddWithValue("$id", jobId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0 ? null : await GetJobCoreAsync(jobId, byRequestId: false, cancellationToken);
    }

    /// <summary>作业列表（按创建时间倒序）。</summary>
    public async Task<IReadOnlyList<ServerJob>> ListJobsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var jobs = new List<ServerJob>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM server_jobs ORDER BY created_at DESC, id LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var job = await GetJobCoreAsync(reader.GetString(0), byRequestId: false, cancellationToken);
            if (job is not null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    /// <summary>删除终态（Completed / Failed）且结束 / 创建时间早于截止时间的作业（历史清理用）。</summary>
    public async Task<int> DeleteTerminalJobsBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM server_jobs
            WHERE status IN ('Completed', 'Failed')
              AND COALESCE(finished_at, created_at) < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", Format(cutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ServerJob?> GetJobCoreAsync(string key, bool byRequestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = byRequestId
            ? "SELECT id FROM server_jobs WHERE request_id = $key LIMIT 1;"
            : "SELECT id FROM server_jobs WHERE id = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var id = await command.ExecuteScalarAsync(cancellationToken) as string;
        return id is null ? null : await LoadJobCoreAsync(connection, id, cancellationToken);
    }

    private static async Task SetJobClaimedAsync(SqliteConnection connection, string jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET status = $status, claimed_at = $claimedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$status", ServerJobStatus.Claimed.ToString());
        command.Parameters.AddWithValue("$claimedAt", Format(now));
        command.Parameters.AddWithValue("$id", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ServerJob?> LoadJobCoreAsync(SqliteConnection connection, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, request_id, target_device_id, status, created_at, claimed_at, finished_at,
                   total_items, completed_items, failed_items, error_message, payload_json
            FROM server_jobs WHERE id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServerJob
        {
            Id = reader.GetString(0),
            RequestId = reader.GetString(1),
            TargetDeviceId = reader.GetString(2),
            Status = Enum.Parse<ServerJobStatus>(reader.GetString(3)),
            CreatedAt = Parse(reader.GetString(4)),
            ClaimedAt = reader.IsDBNull(5) ? null : Parse(reader.GetString(5)),
            FinishedAt = reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
            TotalItems = reader.GetInt32(7),
            CompletedItems = reader.GetInt32(8),
            FailedItems = reader.GetInt32(9),
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            PayloadJson = reader.GetString(11),
        };
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