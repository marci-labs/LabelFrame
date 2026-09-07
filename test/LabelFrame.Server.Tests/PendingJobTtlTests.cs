using LabelFrame.Api;
using LabelFrame.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.Server.Tests;

/// <summary>
/// Pending 暂存 TTL 行为单元测试（FakeTimeProvider 驱动，时间确定）：
/// 领取过滤（CreatedAt + TTL 拦截超期作业，不依赖扫描）、notify 积压预检、后台扫描标记 Expired、TTL 关闭回归。
/// </summary>
public sealed class PendingJobTtlTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero));
    private readonly TempServer _temp = new();
    private readonly ServerOptions _options = new() { PendingJobTtlHours = 1 };
    private readonly ServerService _service;

    public PendingJobTtlTests()
    {
        _service = new ServerService(_temp.Db, options: _options, timeProvider: _time);
    }

    [Fact]
    public async Task Expired_pending_job_should_not_be_claimed_or_prechecked()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-1", "dev"));

        // 时间推进 2 小时（TTL 1 小时）：作业超期——领取过滤与 notify 预检都应拦截
        _time.Advance(TimeSpan.FromHours(2));

        Assert.Empty(await _service.ClaimPendingJobsAsync("dev"));
        Assert.False(await _service.HasDeliverablePendingJobsAsync("dev"));

        // 作业本身仍是 Pending（标记为 Expired 由扫描负责，领取过滤独立于扫描）
        var job = await _service.GetJobAsync((await _temp.Db.GetJobByRequestIdAsync("req-1"))!.Id);
        Assert.Equal("Pending", job.Status);
    }

    [Fact]
    public async Task Fresh_pending_job_should_still_be_claimed()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-1", "dev"));

        // 未满 TTL：行为与现状一致——可领取、预检命中
        _time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(await _service.HasDeliverablePendingJobsAsync("dev"));
        var claimed = await _service.ClaimPendingJobsAsync("dev");
        Assert.Single(claimed);
    }

    [Fact]
    public async Task Ttl_only_ages_pending_claimed_jobs_are_exempt()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-claimed", "dev"));
        var claimed = Assert.Single(await _service.ClaimPendingJobsAsync("dev"));

        // 领取后推进远超 TTL 再回报：Claimed 不计龄，正常进入终态（不会被标记 Expired）
        _time.Advance(TimeSpan.FromHours(10));
        var reported = await _service.ReportResultAsync("dev", claimed.JobId, new ReportResultRequest("Completed", 1, 0, null));
        Assert.Equal("Completed", reported.Status);
    }

    [Fact]
    public async Task Scan_should_mark_only_expired_pending_jobs()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-old", "dev"));
        await _service.SubmitJobAsync(CreateRequest("req-new", "dev"));

        // 推进 2 小时后把 req-new 的创建时间刷新到当前（模拟后提交的新作业）
        _time.Advance(TimeSpan.FromHours(2));
        var newJob = await _temp.Db.GetJobByRequestIdAsync("req-new");
        await BackdateCreatedAtAsync(_temp.Path, "req-old", _time.GetUtcNow().AddHours(-2).AddMinutes(-1));
        await BackdateCreatedAtAsync(_temp.Path, "req-new", _time.GetUtcNow().AddMinutes(-1));

        var scanner = new PendingJobExpirationService(_temp.Db, _options, _time, NullLogger<PendingJobExpirationService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        var oldJob = await _temp.Db.GetJobByRequestIdAsync("req-old");
        Assert.Equal(ServerJobStatus.Expired, oldJob!.Status);
        Assert.NotNull(oldJob.FinishedAt);
        Assert.Contains("已放弃", oldJob.ErrorMessage);
        Assert.Equal(ServerJobStatus.Pending, newJob!.Status);
    }

    [Fact]
    public async Task Scan_with_ttl_disabled_should_do_nothing()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-1", "dev"));
        _time.Advance(TimeSpan.FromHours(48));

        var disabled = new PendingJobExpirationService(_temp.Db, new ServerOptions { PendingJobTtlHours = 0 }, _time, NullLogger<PendingJobExpirationService>.Instance);
        Assert.Equal(0, await disabled.ScanOnceAsync());

        var job = await _temp.Db.GetJobByRequestIdAsync("req-1");
        Assert.Equal(ServerJobStatus.Pending, job!.Status);

        // TTL 关闭时服务层也不过滤：超龄作业照常领取（与现状一致）
        var claimed = await new ServerService(_temp.Db, options: new ServerOptions { PendingJobTtlHours = 0 }, timeProvider: _time)
            .ClaimPendingJobsAsync("dev");
        Assert.Single(claimed);
    }

    [Fact]
    public void Ttl_options_defaults_and_environment_overrides()
    {
        var options = new ServerOptions();
        Assert.Equal(12, options.PendingJobTtlHours);
        Assert.Equal(TimeSpan.FromHours(12), options.PendingJobTtl);
        Assert.Equal(5, options.ExpirationScanIntervalMinutes);

        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PENDING_TTL_HOURS", "24");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_EXPIRATION_SCAN_MINUTES", "10");
        try
        {
            options.ApplyEnvironmentOverrides();
            Assert.Equal(24, options.PendingJobTtlHours);
            Assert.Equal(10, options.ExpirationScanIntervalMinutes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PENDING_TTL_HOURS", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_EXPIRATION_SCAN_MINUTES", null);
        }

        // 0 / 负值 = 关闭过期
        Assert.Null(new ServerOptions { PendingJobTtlHours = 0 }.PendingJobTtl);
        Assert.Null(new ServerOptions { PendingJobTtlHours = -1 }.PendingJobTtl);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _temp.Dispose();
    }

    private static SubmitJobRequest CreateRequest(string requestId, string deviceId) => new(
        requestId,
        new TemplateDto(SampleContract, SampleLayout),
        [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })],
        TargetDeviceId: deviceId);

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

    private static async Task BackdateCreatedAtAsync(string dbPath, string requestId, DateTimeOffset createdAt)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET created_at = $time WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$time", LabelFrame.Core.Data.SqliteSupport.Format(createdAt));
        command.Parameters.AddWithValue("$requestId", requestId);
        await command.ExecuteNonQueryAsync();
    }
}
