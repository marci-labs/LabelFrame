using LabelFrame.Api;
using LabelFrame.Server;

namespace LabelFrame.Server.Tests;

public class PendingJobNotifierTests
{
    [Fact]
    public async Task Notify_should_wake_waiter_immediately()
    {
        var notifier = new PendingJobNotifier();
        var waiting = notifier.WaitAsync("dev-1", TimeSpan.FromSeconds(10));

        notifier.Notify("dev-1");

        Assert.True(await waiting);
    }

    [Fact]
    public async Task Wait_should_return_false_after_timeout()
    {
        var notifier = new PendingJobNotifier();
        var signaled = await notifier.WaitAsync("dev-1", TimeSpan.FromMilliseconds(50));
        Assert.False(signaled);
    }

    [Fact]
    public async Task Notify_should_wake_all_waiters_of_same_device()
    {
        var notifier = new PendingJobNotifier();
        var w1 = notifier.WaitAsync("dev-1", TimeSpan.FromSeconds(10));
        var w2 = notifier.WaitAsync("dev-1", TimeSpan.FromSeconds(10));

        notifier.Notify("dev-1");

        Assert.True(await w1);
        Assert.True(await w2);
    }

    [Fact]
    public async Task Submit_job_should_notify_target_device_waiter()
    {
        using var temp = new TempServer();
        var notifier = new PendingJobNotifier();
        var service = new ServerService(temp.Db, templates: null, notifier);
        await service.RegisterDeviceAsync("device-1", "一号机");

        var waiting = notifier.WaitAsync("device-1", TimeSpan.FromSeconds(10));
        await service.SubmitJobAsync(CreateRequest("req-notify", "device-1"));

        Assert.True(await waiting);
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
