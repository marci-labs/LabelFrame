using System.Diagnostics;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Tests.Jobs;

/// <summary>验证独立作业库迁移的字段保真、回滚、备份与互斥。</summary>
public sealed class JobStorageMigrationTests
{
    [Theory]
    [InlineData(true, "copied")]
    [InlineData(true, "before-commit")]
    [InlineData(true, "committed")]
    [InlineData(false, "copied")]
    [InlineData(false, "before-commit")]
    [InlineData(false, "committed")]
    public async Task Terminated_migration_process_recovers_atomic_format(bool toSplit, string stage)
    {
        using var db = new MigrationDb(true);
        if (!toSplit) await JobStorageMigration.ConvertAsync(db.Path, true);
        var before = db.Snapshot(!toSplit, true);
        using (var probe = new ChildProcess("test", "LabelFrame.JobStorageProbe",
            toSplit ? "to-split" : "to-legacy", db.Path, stage))
        {
            Assert.Equal(stage, await probe.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            probe.Kill();
        }
        var split = stage == "committed" ? toSplit : !toSplit;
        Assert.Equal(before, db.Snapshot(split, true));
        Assert.Equal(split ? 1L : 0L, db.Scalar("PRAGMA user_version;"));
        Assert.Equal("ok", db.Scalar("PRAGMA integrity_check;"));
        await JobStorageMigration.ConvertAsync(db.Path, toSplit);
        Assert.Equal(before, db.Snapshot(toSplit, true));
    }

    [Fact]
    public async Task Tool_rejects_other_process_lock_then_converts_current_data()
    {
        using var db = new MigrationDb(true);
        var before = db.Snapshot(false, true);
        using (var reader = new ChildProcess("test", "LabelFrame.JobStorageProbe", "hold-read", db.Path))
        {
            Assert.Equal("ready", await reader.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            using var blocked = new ChildProcess("tools", "LabelFrame.JobStorageTool", "to-split", db.Path, "--hosts-stopped");
            await blocked.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(1, blocked.Process.ExitCode);
            Assert.Contains("转换失败", await blocked.Process.StandardError.ReadToEndAsync());
            Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
        }
        foreach (var direction in new[] { "to-split", "to-legacy" })
        {
            using var tool = new ChildProcess("tools", "LabelFrame.JobStorageTool", direction, db.Path, "--hosts-stopped");
            await tool.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, tool.Process.ExitCode);
            Assert.Contains("已验证", await tool.Process.StandardOutput.ReadToEndAsync());
            Assert.Equal(before, db.Snapshot(direction == "to-split", true));
        }
    }

    private sealed class ChildProcess : IDisposable
    {
        public ChildProcess(string folder, string name, params string[] args)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = directory.Parent!.Name;
            while (!File.Exists(System.IO.Path.Combine(directory.FullName, "LabelFrame.slnx")))
                directory = directory.Parent ?? throw new InvalidOperationException("找不到测试仓库。");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = directory.FullName,
            };
            start.ArgumentList.Add(System.IO.Path.Combine(directory.FullName, folder, name, "bin", configuration, "net10.0", name + ".dll"));
            foreach (var argument in args) start.ArgumentList.Add(argument);
            Process = Process.Start(start) ?? throw new InvalidOperationException("测试子进程未启动。");
        }

        public Process Process { get; }

        public void Kill()
        {
            if (!Process.HasExited) Process.Kill();
            if (!Process.WaitForExit(10000)) throw new TimeoutException("测试子进程未退出。");
        }

        public void Dispose()
        {
            Kill();
            Process.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Product_initialization_migrates_and_preserves_all_fields(bool withBytes)
    {
        using var db = new MigrationDb(withBytes);
        var before = db.Snapshot(false, withBytes);
        var store = new SqliteLabelJobStore(db.Path);
        await store.InitializeAsync();
        Assert.Equal(before, db.Snapshot(true, true));
        await store.InitializeAsync();
        Assert.Single(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
    }

    [Theory]
    [InlineData("PRAGMA user_version=99;")]
    [InlineData("ALTER TABLE job_items ADD COLUMN extra TEXT;")]
    public async Task Product_initialization_rejects_unknown_database(string sql)
    {
        using var db = new MigrationDb(true);
        db.Execute(sql);
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteLabelJobStore(db.Path).InitializeAsync());
        Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Round_trip_preserves_fields_and_current_terminal_states(bool withBytes)
    {
        using var db = new MigrationDb(withBytes);
        var before = db.Snapshot(false, withBytes);
        var backup = await JobStorageMigration.ConvertAsync(db.Path, true);
        Assert.NotNull(backup);
        Assert.Equal(before, db.Snapshot(true, true));
        Assert.Equal(1L, db.Scalar("PRAGMA user_version;"));
        Assert.Null(await JobStorageMigration.ConvertAsync(db.Path, true));
        using (var backupDb = db.Open(backup))
        {
            using var command = backupDb.CreateCommand();
            command.CommandText = "SELECT count(*) FROM job_items;";
            Assert.Equal(5L, command.ExecuteScalar());
        }

        db.Execute("""
            UPDATE jobs SET status='Completed';
            UPDATE job_items SET status='Completed', updated_at='2026-10-26T12:34:56.1234567+08:00';
            INSERT INTO jobs VALUES ('new-job','new-request','Pending','new-created','new-updated');
            INSERT INTO job_items VALUES ('new-item','new-job',0,'Pending',NULL,NULL,'new-time');
            INSERT INTO job_payloads VALUES ('new-item','新增文本',x'0080FF');
            """);
        var current = db.Snapshot(true, true);
        Assert.NotNull(await JobStorageMigration.ConvertAsync(db.Path, false));
        Assert.Equal(current, db.Snapshot(false, true));
        Assert.Equal(0L, db.Scalar("PRAGMA user_version;"));
        Assert.Null(await JobStorageMigration.ConvertAsync(db.Path, false));
        Assert.NotNull(await JobStorageMigration.ConvertAsync(db.Path, true));
        Assert.Equal(current, db.Snapshot(true, true));
    }

    [Theory]
    [InlineData(true, "copied")]
    [InlineData(true, "before-commit")]
    [InlineData(false, "copied")]
    [InlineData(false, "before-commit")]
    public async Task Exceptions_roll_back_schema_and_all_fields(bool toSplit, string stage)
    {
        using var db = new MigrationDb(true);
        if (!toSplit) await JobStorageMigration.ConvertAsync(db.Path, true);
        var before = db.Snapshot(!toSplit, true);
        await Assert.ThrowsAsync<IOException>(() => JobStorageMigration.ConvertAsync(db.Path, toSplit,
            checkpoint: (point, _) => { if (point == stage) throw new IOException("测试故障"); }));
        Assert.Equal(before, db.Snapshot(!toSplit, true));
        Assert.Equal(toSplit ? 0L : 1L, db.Scalar("PRAGMA user_version;"));
        Assert.Equal(0L, db.Scalar("SELECT count(*) FROM sqlite_schema WHERE name='job_items_next';"));
        Assert.NotNull(await JobStorageMigration.ConvertAsync(db.Path, toSplit));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_before_commit_rolls_back(bool toSplit)
    {
        using var db = new MigrationDb(true);
        if (!toSplit) await JobStorageMigration.ConvertAsync(db.Path, true);
        var before = db.Snapshot(!toSplit, true);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JobStorageMigration.ConvertAsync(db.Path, toSplit,
            checkpoint: (point, _) => { if (point == "before-commit") cancellation.Cancel(); }, cancellationToken: cancellation.Token));
        Assert.Equal(before, db.Snapshot(!toSplit, true));
    }

    [Fact]
    public async Task Insufficient_space_does_not_create_backup_or_change_source()
    {
        using var db = new MigrationDb(true);
        var before = db.Snapshot(false, true);
        await Assert.ThrowsAsync<IOException>(() => JobStorageMigration.ConvertAsync(db.Path, true, availableSpace: _ => 0));
        Assert.Equal(before, db.Snapshot(false, true));
        Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
    }

    [Theory]
    [InlineData("PRAGMA user_version=2;")]
    [InlineData("ALTER TABLE job_items ADD COLUMN unknown TEXT;")]
    [InlineData("CREATE TRIGGER custom_trigger AFTER UPDATE ON job_items BEGIN SELECT 1; END;")]
    [InlineData("CREATE INDEX custom_index ON job_items(status);")]
    [InlineData("CREATE VIEW custom_view AS SELECT * FROM job_items;")]
    [InlineData("CREATE TABLE sqliteXextra (id TEXT);")]
    [InlineData("DROP INDEX ux_job_items_job_index;")]
    [InlineData("DROP INDEX ix_job_items_job_id; CREATE INDEX ix_job_items_job_id ON job_items(status);")]
    [InlineData("DROP INDEX ux_job_items_job_index; CREATE INDEX ux_job_items_job_index ON job_items(job_id,item_index);")]
    [InlineData("UPDATE job_items SET job_id='missing' WHERE id='i0';")]
    public async Task Unrecognized_or_inconsistent_legacy_database_is_rejected(string sql)
    {
        using var db = new MigrationDb(true);
        db.Execute(sql);
        await Assert.ThrowsAsync<InvalidDataException>(() => JobStorageMigration.ConvertAsync(db.Path, true));
        Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
    }

    [Theory]
    [InlineData("DELETE FROM job_payloads WHERE item_id='i0';")]
    [InlineData("INSERT INTO job_payloads VALUES ('orphan','text',NULL);")]
    public async Task Missing_or_orphan_payload_is_rejected(string sql)
    {
        using var db = new MigrationDb(true);
        await JobStorageMigration.ConvertAsync(db.Path, true);
        db.Execute(sql);
        await Assert.ThrowsAsync<InvalidDataException>(() => JobStorageMigration.ConvertAsync(db.Path, false));
        Assert.Equal(1L, db.Scalar("PRAGMA user_version;"));
    }

    [Theory]
    [InlineData("request_id TEXT NOT NULL UNIQUE", "request_id TEXT NOT NULL")]
    [InlineData("status TEXT NOT NULL", "status TEXT NOT NULL CHECK(length(status)>0)")]
    [InlineData("REFERENCES jobs(id)", "REFERENCES jobs(id) ON DELETE CASCADE")]
    [InlineData("error_code TEXT NULL", "error_code TEXT NULL DEFAULT 'unknown'")]
    [InlineData("zpl TEXT NOT NULL", "zpl TEXT NOT NULL COLLATE NOCASE")]
    public async Task Unknown_constraints_are_rejected_before_backup(string original, string replacement)
    {
        using var db = new MigrationDb(true, sql => sql.Replace(original, replacement, StringComparison.Ordinal));
        var before = db.Snapshot(false, true);
        await Assert.ThrowsAsync<InvalidDataException>(() => JobStorageMigration.ConvertAsync(db.Path, true));
        Assert.Equal(before, db.Snapshot(false, true));
        Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
    }

    [Theory]
    [InlineData("backed-up")]
    [InlineData("copied")]
    [InlineData("before-commit")]
    public async Task Failure_identifies_verified_backup_without_wrapping_exception(string stage)
    {
        using var db = new MigrationDb(true);
        var failure = new IOException("注入失败");
        var caught = await Assert.ThrowsAsync<IOException>(() => JobStorageMigration.ConvertAsync(db.Path, true,
            checkpoint: (point, _) => { if (point == stage) throw failure; }));
        Assert.Same(failure, caught);
        Assert.Equal(true, caught.Data["JobStorageBackupVerified"]);
        var path = Assert.IsType<string>(caught.Data["JobStorageBackupPath"]);
        Assert.True(File.Exists(path));
        using var backup = db.Open(path);
        using var command = backup.CreateCommand();
        command.CommandText = "SELECT count(*) FROM job_items;";
        Assert.Equal(5L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Backup_failure_identifies_unverified_file_and_preserves_source()
    {
        using var db = new MigrationDb(true);
        var before = db.Snapshot(false, true);
        var error = await Assert.ThrowsAsync<IOException>(() => JobStorageMigration.ConvertAsync(db.Path, true,
            checkpoint: (point, _) => { if (point == "backup-created") throw new IOException("备份写入前故障"); }));
        Assert.Equal(false, error.Data["JobStorageBackupVerified"]);
        var path = Assert.IsType<string>(error.Data["JobStorageBackupPath"]);
        Assert.Equal(0, new FileInfo(path).Length);
        Assert.Equal(before, db.Snapshot(false, true));
        Assert.NotNull(await JobStorageMigration.ConvertAsync(db.Path, true));
        Assert.Equal(2, Directory.GetFiles(db.DirectoryPath, "*.before-*.db").Length);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("backup-created")]
    [InlineData("backed-up")]
    [InlineData("copied")]
    public async Task Cancellation_at_maintenance_boundaries_keeps_source(string stage)
    {
        using var db = new MigrationDb(true);
        var before = db.Snapshot(false, true);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JobStorageMigration.ConvertAsync(db.Path, true,
            checkpoint: (point, _) => { if (point == stage) cancellation.Cancel(); }, cancellationToken: cancellation.Token));
        Assert.Equal(before, db.Snapshot(false, true));
        Assert.Equal(0L, db.Scalar("PRAGMA user_version;"));
        if (stage == "locked") Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.before-*.db"));
    }

    [Fact]
    public async Task Existing_reader_blocks_migration_without_changing_data()
    {
        using var db = new MigrationDb(true);
        using (var readerConnection = db.Open())
        using (var transaction = readerConnection.BeginTransaction(deferred: true))
        {
            using var command = readerConnection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT * FROM job_items;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            await Assert.ThrowsAsync<SqliteException>(() => JobStorageMigration.ConvertAsync(db.Path, true));
        }
        Assert.Equal(0L, db.Scalar("PRAGMA user_version;"));
        Assert.NotNull(await JobStorageMigration.ConvertAsync(db.Path, true));
    }

    [Fact]
    public async Task Maintenance_lock_blocks_other_connections_during_backup_and_conversion()
    {
        using var db = new MigrationDb(true);
        await JobStorageMigration.ConvertAsync(db.Path, true, checkpoint: (point, _) =>
        {
            if (point is not ("backed-up" or "before-commit")) return;
            Assert.Throws<SqliteException>(() => db.Scalar("SELECT count(*) FROM job_items;"));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sqlite_full_rolls_back_and_allows_retry(bool toSplit)
    {
        using var db = new MigrationDb(true);
        if (!toSplit) await JobStorageMigration.ConvertAsync(db.Path, true);
        db.Execute("VACUUM;");
        var before = db.Snapshot(!toSplit, true);
        var error = await Assert.ThrowsAsync<SqliteException>(() => JobStorageMigration.ConvertAsync(db.Path, toSplit,
            checkpoint: (point, connection) =>
            {
                if (point != "backed-up") return;
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA page_count;";
                var pages = (long)command.ExecuteScalar()!;
                command.CommandText = FormattableString.Invariant($"PRAGMA max_page_count={pages};");
                command.ExecuteNonQuery();
            }));
        Assert.Equal(13, error.SqliteErrorCode);
        Assert.Equal(before, db.Snapshot(!toSplit, true));
        Assert.NotNull(await JobStorageMigration.ConvertAsync(db.Path, toSplit));
    }

    [Fact]
    public async Task Backup_includes_committed_wal_without_prior_checkpoint()
    {
        using var db = new MigrationDb(true);
        using (var connection = db.Open())
        {
            Assert.Equal(0, SQLitePCL.raw.sqlite3_db_config(connection.Handle,
                SQLitePCL.raw.SQLITE_DBCONFIG_NO_CKPT_ON_CLOSE, 1, out _));
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_autocheckpoint=0; UPDATE job_items SET zpl='仅在WAL中的载荷' WHERE id='i0';";
            command.ExecuteNonQuery();
        }
        Assert.True(new FileInfo(db.Path + "-wal").Length > 32);
        var backup = await JobStorageMigration.ConvertAsync(db.Path, true);
        using var backupConnection = db.Open(backup);
        using var check = backupConnection.CreateCommand();
        check.CommandText = "SELECT zpl FROM job_items WHERE id='i0';";
        Assert.Equal("仅在WAL中的载荷", check.ExecuteScalar());
    }

    private sealed class MigrationDb : IDisposable
    {
        public MigrationDb(bool withBytes, Func<string, string>? transform = null)
        {
            SqliteSupport.EnsureInitialized();
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "labelframe-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(DirectoryPath, "jobs.db");
            var setup = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE jobs (id TEXT PRIMARY KEY, request_id TEXT NOT NULL UNIQUE, status TEXT NOT NULL,
                    created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE job_items (id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id),
                    item_index INTEGER NOT NULL, status TEXT NOT NULL, zpl TEXT NOT NULL,
                    error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL);
                CREATE UNIQUE INDEX ux_job_items_job_index ON job_items(job_id,item_index);
                CREATE INDEX ix_job_items_job_id ON job_items(job_id);
                INSERT INTO jobs VALUES ('job','request','Suspended','2026-10-25T01:02:03.456+08:00','2026-10-26T00:00:00Z');
                INSERT INTO job_items VALUES
                    ('i0','job',0,'Pending','中文',NULL,NULL,'original-0'),
                    ('i1','job',1,'Printing','',NULL,'','original-1'),
                    ('i2','job',2,'Completed','done','code','错误','original-2'),
                    ('i3','job',3,'Failed','failed',NULL,NULL,'original-3'),
                    ('i4','job',4,'Cancelled','cancelled',NULL,NULL,'original-4');
                """;
            Execute(transform?.Invoke(setup) ?? setup);
            if (withBytes) Execute("ALTER TABLE job_items ADD COLUMN command_bytes BLOB NULL; UPDATE job_items SET command_bytes=x'0080FF' WHERE id='i1'; UPDATE job_items SET command_bytes=x'' WHERE id='i4';");
        }

        public string DirectoryPath { get; }
        public string Path { get; }

        public SqliteConnection Open(string? path = null)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path ?? Path, Pooling = false, DefaultTimeout = 1, ForeignKeys = false,
            }.ToString());
            connection.Open();
            return connection;
        }

        public void Execute(string sql)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public object? Scalar(string sql)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        public List<string> Snapshot(bool split, bool withBytes)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = split
                ? "SELECT j.*,i.id,i.job_id,i.item_index,i.status,i.error_code,i.error_message,i.updated_at,p.zpl,p.command_bytes FROM jobs j LEFT JOIN job_items i ON i.job_id=j.id LEFT JOIN job_payloads p ON p.item_id=i.id ORDER BY j.id,i.item_index;"
                : $"SELECT j.*,i.id,i.job_id,i.item_index,i.status,i.error_code,i.error_message,i.updated_at,i.zpl,{(withBytes ? "i.command_bytes" : "NULL")} FROM jobs j LEFT JOIN job_items i ON i.job_id=j.id ORDER BY j.id,i.item_index;";
            using var reader = command.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(System.Text.Json.JsonSerializer.Serialize(values));
            }
            return rows;
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
