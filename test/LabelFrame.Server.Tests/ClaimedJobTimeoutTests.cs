using LabelFrame.Api;
using LabelFrame.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.Server.Tests;

/// <summary>
/// Claimed 超时回收行为单元测试（FakeTimeProvider 驱动，时间确定）：
/// 失联回收为 Failed（LF_SRV_009）、正常回报零回归、不重投、迟到回报幂等重放、超时关闭回归、配置项。
/// 超时判定只以 claimed_at + 服务端时钟计龄——全程不触发设备心跳，即「与设备在线状态无关」的体现。
/// </summary>
public sealed class ClaimedJobTimeoutTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero));
    private readonly TempServer _temp = new();
    private readonly ServerOptions _options = new();
    private readonly ServerService _service;

    public ClaimedJobTimeoutTests()
    {
        _service = new ServerService(_temp.Db, options: _options, timeProvider: _time);
    }

    [Fact]
    public async Task Host_lost_claimed_job_should_be_reclaimed_as_failed_and_never_redelivered()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-1", "dev"));
        var claimed = Assert.Single(await _service.ClaimPendingJobsAsync("dev"));

        // 领取后宿主消失（无任何心跳 / 回报），推进超过默认 30 分钟
        _time.Advance(TimeSpan.FromMinutes(31));

        var scanner = new ClaimedJobTimeoutService(_temp.Db, _options, _time, NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        var job = await _temp.Db.GetJobByRequestIdAsync("req-1");
        Assert.Equal(ServerJobStatus.Failed, job!.Status);
        Assert.NotNull(job.FinishedAt);
        Assert.Contains(ServerErrorCodes.HostLostTimeout, job.ErrorMessage);
        Assert.Contains("可能已实际打印", job.ErrorMessage);

        // 回收为终态后不被再次投递（同一 requestId 重放只返回既有作业，领取查询无产出）
        var replay = await _service.SubmitJobAsync(CreateRequest("req-1", "dev"));
        Assert.Equal(job.Id, replay.JobId);
        Assert.Equal("Failed", replay.Status);
        Assert.Empty(await _service.ClaimPendingJobsAsync("dev"));
    }

    [Fact]
    public async Task Normal_report_within_timeout_should_complete_without_regression()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-ok", "dev"));
        var claimed = Assert.Single(await _service.ClaimPendingJobsAsync("dev"));

        // 未满超时（29 分钟）正常回报：照常进入终态，扫描不介入
        _time.Advance(TimeSpan.FromMinutes(29));
        var reported = await _service.ReportResultAsync("dev", claimed.JobId, new ReportResultRequest("Completed", 1, 0, null));
        Assert.Equal("Completed", reported.Status);

        var scanner = new ClaimedJobTimeoutService(_temp.Db, _options, _time, NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(0, await scanner.ScanOnceAsync());
        Assert.Equal(ServerJobStatus.Completed, (await _temp.Db.GetJobByRequestIdAsync("req-ok"))!.Status);
    }

    [Fact]
    public async Task Scan_should_only_touch_stale_claimed_jobs()
    {
        // 两台设备隔离：pending-dev 从不领取（req-pending 保持 Pending），live-dev 在 T0+20 领取 req-live
        await _service.RegisterDeviceAsync("pending-dev", "暂存机");
        await _service.RegisterDeviceAsync("live-dev", "在打机");
        await _service.SubmitJobAsync(CreateRequest("req-pending", "pending-dev"));
        _time.Advance(TimeSpan.FromMinutes(20));
        await _service.SubmitJobAsync(CreateRequest("req-live", "live-dev"));
        Assert.Single(await _service.ClaimPendingJobsAsync("live-dev"));

        // now = T0+31：req-live 领取于 T0+20（计龄 11 分钟 < 30）不回收；req-pending 是 Pending 不归本扫描管
        _time.Advance(TimeSpan.FromMinutes(11));
        var scanner = new ClaimedJobTimeoutService(_temp.Db, _options, _time, NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(0, await scanner.ScanOnceAsync());

        Assert.Equal(ServerJobStatus.Claimed, (await _temp.Db.GetJobByRequestIdAsync("req-live"))!.Status);
        Assert.Equal(ServerJobStatus.Pending, (await _temp.Db.GetJobByRequestIdAsync("req-pending"))!.Status);
    }

    [Fact]
    public async Task Late_report_after_timeout_should_replay_existing_failed_without_override()
    {
        await _service.RegisterDeviceAsync("dev", "一号机");
        await _service.SubmitJobAsync(CreateRequest("req-late", "dev"));
        var claimed = Assert.Single(await _service.ClaimPendingJobsAsync("dev"));

        _time.Advance(TimeSpan.FromMinutes(31));
        var scanner = new ClaimedJobTimeoutService(_temp.Db, _options, _time, NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        // 宿主迟到的真实回报（如重启后本地队列打完）：幂等重放返回既有终态，不覆盖超时判定
        var reported = await _service.ReportResultAsync("dev", claimed.JobId, new ReportResultRequest("Completed", 1, 0, null));
        Assert.Equal("Failed", reported.Status);
        var job = await _temp.Db.GetJobByRequestIdAsync("req-late");
        Assert.Equal(ServerJobStatus.Failed, job!.Status);
        Assert.Contains(ServerErrorCodes.HostLostTimeout, job.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_disabled_should_keep_current_behaviour()
    {
        var disabledOptions = new ServerOptions { ClaimedJobTimeoutMinutes = 0 };
        var disabledService = new ServerService(_temp.Db, options: disabledOptions, timeProvider: _time);
        await disabledService.RegisterDeviceAsync("dev", "一号机");
        await disabledService.SubmitJobAsync(CreateRequest("req-off", "dev"));
        var claimed = Assert.Single(await disabledService.ClaimPendingJobsAsync("dev"));

        // 关闭回收：即使失联数天也停留 Claimed（沿用现状），迟到的回报仍可正常进入终态
        _time.Advance(TimeSpan.FromDays(3));
        var scanner = new ClaimedJobTimeoutService(_temp.Db, disabledOptions, _time, NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(0, await scanner.ScanOnceAsync());
        Assert.Equal(ServerJobStatus.Claimed, (await _temp.Db.GetJobByRequestIdAsync("req-off"))!.Status);

        var reported = await disabledService.ReportResultAsync("dev", claimed.JobId, new ReportResultRequest("Completed", 1, 0, null));
        Assert.Equal("Completed", reported.Status);
    }

    [Fact]
    public void Timeout_options_defaults_and_environment_overrides()
    {
        var options = new ServerOptions();
        Assert.Equal(30, options.ClaimedJobTimeoutMinutes);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ClaimedJobTimeout);

        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES", "120");
        try
        {
            options.ApplyEnvironmentOverrides();
            Assert.Equal(120, options.ClaimedJobTimeoutMinutes);
            Assert.Equal(TimeSpan.FromHours(2), options.ClaimedJobTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES", null);
        }

        // 0 / 负值 = 关闭回收
        Assert.Null(new ServerOptions { ClaimedJobTimeoutMinutes = 0 }.ClaimedJobTimeout);
        Assert.Null(new ServerOptions { ClaimedJobTimeoutMinutes = -1 }.ClaimedJobTimeout);
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
}
