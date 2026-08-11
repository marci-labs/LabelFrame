using LabelFrame.Core.Logs;
using LabelFrame.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFrame.Server.Tests;

public class DataCleanupServiceTests
{
    private const string OldTime = "2020-01-01T00:00:00.0000000+00:00";

    [Fact]
    public async Task Delete_terminal_jobs_before_cutoff_should_remove_only_old_terminal_jobs()
    {
        using var temp = new TempServer();
        await temp.Service.RegisterDeviceAsync("device-1", "一号机");
        await temp.Service.SubmitJobAsync(CreateRequest("req-old", "device-1"));
        await temp.Service.SubmitJobAsync(CreateRequest("req-new", "device-1"));

        // 两个终态作业（Claimed -> Completed）；req-pending 之后提交保持 Pending
        var claimed = await temp.Service.ClaimPendingJobsAsync("device-1");
        Assert.Equal(2, claimed.Count);
        await temp.Service.ReportResultAsync("device-1", claimed[0].JobId, new ReportResultRequest("Completed", 1, 0, null));
        await temp.Service.ReportResultAsync("device-1", claimed[1].JobId, new ReportResultRequest("Completed", 1, 0, null));
        await temp.Service.SubmitJobAsync(CreateRequest("req-pending", "device-1"));

        // 把 req-old 的结束时间改到很早（模拟超期）
        await BackdateFinishedAtAsync(temp.Path, "req-old");

        // 截止时间取 1 小时前：req-new（刚完成）保留，req-old（2020）被清理
        var deleted = await temp.Db.DeleteTerminalJobsBeforeAsync(DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Equal(1, deleted);
        var jobs = await temp.Service.ListJobsAsync(100);
        var remaining = jobs.Select(j => j.RequestId).ToList();
        Assert.Contains("req-new", remaining);
        Assert.Contains("req-pending", remaining);
        Assert.DoesNotContain("req-old", remaining);
    }

    [Fact]
    public async Task Cleanup_service_should_delete_old_logs_and_keep_recent()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-clean-{Guid.NewGuid():N}.db");
        try
        {
            var logStore = new SqliteLogStore(dbPath);
            await logStore.InitializeAsync();
            await logStore.AppendAsync("pda-1", ["旧日志"], CancellationToken.None);
            await logStore.AppendAsync("pda-1", ["新日志"], CancellationToken.None);
            await BackdateLogAsync(dbPath, "旧日志");

            using var temp = new TempServer();
            var options = new ServerOptions { LogRetentionDays = 30, JobRetentionDays = 30, CleanupIntervalHours = 24 };
            var service = new DataCleanupService(temp.Db, logStore, options, NullLogger<DataCleanupService>.Instance);
            await service.CleanupAsync();

            var entries = await logStore.QueryAsync("pda-1", null, CancellationToken.None);
            var entry = Assert.Single(entries);
            Assert.Contains("新日志", entry.Line);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public void Server_options_defaults_and_environment_overrides()
    {
        var options = new ServerOptions();
        Assert.Equal(30, options.JobRetentionDays);
        Assert.Equal(90, options.LogRetentionDays);
        Assert.Equal(24, options.CleanupIntervalHours);
        Assert.Contains("ProgramData", ServerOptions.DefaultDataDirectory);

        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_JOB_RETENTION_DAYS", "7");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_RETENTION_DAYS", "15");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_CLEANUP_INTERVAL_HOURS", "6");
        try
        {
            options.ApplyEnvironmentOverrides();
            Assert.Equal(7, options.JobRetentionDays);
            Assert.Equal(15, options.LogRetentionDays);
            Assert.Equal(6, options.CleanupIntervalHours);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_JOB_RETENTION_DAYS", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_RETENTION_DAYS", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_CLEANUP_INTERVAL_HOURS", null);
        }
    }

    private static SubmitJobRequest CreateRequest(string requestId, string deviceId) => new(
        requestId,
        deviceId,
        new TemplateDto(SampleContract, SampleLayout),
        [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })]);

    private static LabelFrame.Core.Contracts.LabelContract SampleContract { get; } = new()
    {
        Name = "location-label",
        Version = "1.0",
        Fields =
        [
            new LabelFrame.Core.Contracts.LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            new LabelFrame.Core.Contracts.LabelField { Key = "zone", DisplayName = "区域", IsRequired = true },
        ],
    };

    private static LabelFrame.Core.Layout.LabelLayout SampleLayout { get; } = new()
    {
        Name = "location-label-100x60",
        ContractName = "location-label",
        ContractVersion = "1.0",
        WidthMm = 100,
        HeightMm = 60,
        Elements =
        [
            new LabelFrame.Core.Layout.LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
            new LabelFrame.Core.Layout.LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
        ],
    };

    private static async Task BackdateFinishedAtAsync(string dbPath, string requestId)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET finished_at = $time WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$time", OldTime);
        command.Parameters.AddWithValue("$requestId", requestId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task BackdateLogAsync(string dbPath, string line)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE logs SET time = $time WHERE line LIKE '%' || $line || '%';";
        command.Parameters.AddWithValue("$time", OldTime);
        command.Parameters.AddWithValue("$line", line);
        await command.ExecuteNonQueryAsync();
    }
}
