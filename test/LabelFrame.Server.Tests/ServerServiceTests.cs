using LabelFrame.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;

namespace LabelFrame.Server.Tests;

public class ServerServiceTests
{
    private static SubmitJobRequest CreateRequest(string requestId, string deviceId = "device-1", int labels = 1) => new(
        requestId,
        new TemplateDto(SampleContract, SampleLayout),
        Enumerable.Range(0, labels).Select(i => new LabelDto(new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = $"A-01-02-0{i}",
        })).ToList(),
        TargetDeviceId: deviceId);

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

        var exception = await Assert.ThrowsAsync<ServerException>(() => db.Service.SubmitJobAsync(new SubmitJobRequest("req-x", null, null, TargetDeviceId: "device-1")));

        Assert.Equal(ServerErrorCodes.InvalidRequest, exception.Code);
    }

    [Fact]
    public async Task Submit_job_by_template_name_should_resolve_template_with_images()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");

        var templatesPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesPath);
        await templates.InitializeAsync();
        try
        {
            var svc = new ServerService(db.Db, templates);
            var png = new byte[] { 1, 2, 3 };
            await templates.SaveAsync(new TemplatePackage
            {
                Name = "tpl-1",
                Group = "默认",
                Contract = SampleContract,
                Layout = SampleLayout,
                TestData = new Dictionary<string, string> { ["zone"] = "A", ["locationCode"] = "C1" },
                Images = new Dictionary<string, byte[]> { ["logo"] = png },
            }, CancellationToken.None);

            var job = await svc.SubmitJobAsync(new SubmitJobRequest(
                "req-tpl",
                null,
                new List<LabelDto> { new(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" }) },
                TargetDeviceId: "device-1",
                TemplateName: "tpl-1"));
            Assert.Equal("Pending", job.Status);

            var claimed = await svc.ClaimPendingJobsAsync("device-1");
            var payload = Assert.Single(claimed).Payload;
            Assert.Equal("tpl-1", payload.Template.Name);
            Assert.NotNull(payload.Template.Images);
            Assert.Equal(System.Convert.ToBase64String(png), payload.Template.Images!["logo"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(templatesPath))
            {
                File.Delete(templatesPath);
            }
        }
    }

    [Fact]
    public async Task Submit_job_with_unknown_template_name_should_fail()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");

        var templatesPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesPath);
        await templates.InitializeAsync();
        try
        {
            var svc = new ServerService(db.Db, templates);
            var exception = await Assert.ThrowsAsync<ServerException>(() => svc.SubmitJobAsync(new SubmitJobRequest(
                "req-x",
                null,
                new List<LabelDto> { new(new Dictionary<string, string>()) },
                TargetDeviceId: "device-1",
                TemplateName: "no-such-template")));
            Assert.Equal(ServerErrorCodes.TemplateNotFound, exception.Code);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(templatesPath))
            {
                File.Delete(templatesPath);
            }
        }
    }

    [Fact]
    public async Task List_jobs_with_device_id_should_filter_by_target_device()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");
        await db.Service.RegisterDeviceAsync("device-2", "二号机");
        await db.Service.SubmitJobAsync(CreateRequest("req-1", "device-1"));
        await db.Service.SubmitJobAsync(CreateRequest("req-2", "device-2"));
        await db.Service.SubmitJobAsync(CreateRequest("req-3", "device-1"));

        var all = await db.Service.ListJobsAsync(100);
        var onlyMine = await db.Service.ListJobsAsync(100, "device-1");

        Assert.Equal(3, all.Count);
        Assert.Equal(2, onlyMine.Count);
        Assert.All(onlyMine, j => Assert.Equal("device-1", j.TargetDeviceId));
        Assert.Contains(onlyMine, j => j.RequestId == "req-1");
        Assert.Contains(onlyMine, j => j.RequestId == "req-3");
        Assert.DoesNotContain(onlyMine, j => j.RequestId == "req-2");
    }

    [Fact]
    public async Task List_jobs_without_device_id_should_return_all()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机");
        await db.Service.SubmitJobAsync(CreateRequest("req-all-1", "device-1"));

        var jobs = await db.Service.ListJobsAsync(100);
        Assert.Single(jobs);
    }
}