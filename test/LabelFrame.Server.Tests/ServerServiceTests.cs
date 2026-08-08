using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Server.Tests;

public class ServerServiceTests
{
    private static SubmitJobRequest CreateRequest(string requestId, string deviceId = "device-1", int labels = 1) => new(
        requestId,
        deviceId,
        new TemplateDto(SampleContract, SampleLayout),
        Enumerable.Range(0, labels).Select(i => new LabelDto(new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = $"A-01-02-0{i}",
        })).ToList());

    private static LabelContract SampleContract { get; } = new()
    {
        Name = "location-label",
        Version = "1.0",
        Fields =
        [
            new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            new LabelField { Key = "zone", DisplayName = "区域", IsRequired = true },
        ],
    };

    private static LabelLayout SampleLayout { get; } = new()
    {
        Name = "location-label-100x60",
        ContractName = "location-label",
        ContractVersion = "1.0",
        WidthMm = 100,
        HeightMm = 60,
        Elements =
        [
            new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
            new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
        ],
    };

    [Fact]
    public async Task Register_and_list_devices_should_reflect_online_status()
    {
        using var db = new TempServer();

        var device = await db.Service.RegisterDeviceAsync("device-1", "一号机");
        var devices = await db.Service.ListDevicesAsync();

        Assert.Equal("device-1", device.DeviceId);
        Assert.Equal("Online", device.Status);
        var listed = Assert.Single(devices);
        Assert.Equal("一号机", listed.Name);
        Assert.Equal("Online", listed.Status);
    }

    [Fact]
    public async Task Submit_job_to_unregistered_device_should_fail()
    {
        using var db = new TempServer();

        var exception = await Assert.ThrowsAsync<ServerException>(() => db.Service.SubmitJobAsync(CreateRequest("req-1", "missing-device")));

        Assert.Equal(ServerErrorCodes.DeviceNotFound, exception.Code);
    }

    [Fact]
    public async Task Submit_job_should_be_idempotent_by_request_id()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");

        var first = await db.Service.SubmitJobAsync(CreateRequest("req-idem"));
        var second = await db.Service.SubmitJobAsync(CreateRequest("req-idem", labels: 5));

        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal("Pending", first.Status);
        Assert.Equal(1, first.TotalItems);
    }

    [Fact]
    public async Task Claim_should_return_targeted_pending_job_once()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");
        await db.Service.RegisterDeviceAsync("device-2", "二号机");
        await db.Service.SubmitJobAsync(CreateRequest("req-claim", "device-1", labels: 3));

        var claimed = await db.Service.ClaimPendingJobsAsync("device-1");
        var secondClaim = await db.Service.ClaimPendingJobsAsync("device-1");
        var otherClaim = await db.Service.ClaimPendingJobsAsync("device-2");

        var job = Assert.Single(claimed);
        Assert.Equal("req-claim", job.RequestId);
        Assert.Equal(3, job.TotalItems);
        Assert.Equal("location-label", job.Payload.Template.Contract!.Name);
        Assert.Equal(3, job.Payload.Labels.Count);
        Assert.Empty(secondClaim);
        Assert.Empty(otherClaim);
    }

    [Fact]
    public async Task Report_result_should_update_job_status()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");
        await db.Service.SubmitJobAsync(CreateRequest("req-report", labels: 2));
        var claimed = await db.Service.ClaimPendingJobsAsync("device-1");

        var result = await db.Service.ReportResultAsync(
            "device-1",
            claimed[0].JobId,
            new ReportResultRequest("Completed", 2, 0, null));

        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, result.CompletedItems);
        Assert.Equal("Online", result.DeviceStatus);
    }

    [Fact]
    public async Task Report_result_by_non_owner_should_fail()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");
        await db.Service.RegisterDeviceAsync("device-2", "二号机");
        await db.Service.SubmitJobAsync(CreateRequest("req-owner"));
        var claimed = await db.Service.ClaimPendingJobsAsync("device-1");

        var exception = await Assert.ThrowsAsync<ServerException>(() => db.Service.ReportResultAsync(
            "device-2",
            claimed[0].JobId,
            new ReportResultRequest("Completed", 1, 0, null)));

        Assert.Equal(ServerErrorCodes.NotJobOwner, exception.Code);
    }

    [Fact]
    public async Task Offline_device_should_keep_job_pending_and_show_offline()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");
        // 手动把心跳改到窗口外（模拟长时间未轮询）
        await db.Db.TouchDeviceAsync("device-1", DateTimeOffset.UtcNow.AddMinutes(-10));
        await db.Service.SubmitJobAsync(CreateRequest("req-offline"));

        var job = await db.Service.GetJobAsync((await db.Service.ListJobsAsync())[0].JobId);
        var devices = await db.Service.ListDevicesAsync();

        Assert.Equal("Pending", job.Status);
        Assert.Equal("Offline", job.DeviceStatus);
        Assert.Equal("Offline", devices[0].Status);
    }

    [Fact]
    public async Task Missing_fields_should_fail_with_invalid_request()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");

        var exception = await Assert.ThrowsAsync<ServerException>(() => db.Service.SubmitJobAsync(new SubmitJobRequest("req-x", "device-1", null, null)));

        Assert.Equal(ServerErrorCodes.InvalidRequest, exception.Code);
    }
}