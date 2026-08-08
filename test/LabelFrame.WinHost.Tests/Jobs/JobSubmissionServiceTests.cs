using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Tests.Samples;
using LabelFrame.Core.Validation;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Rendering;

namespace LabelFrame.WinHost.Tests.Jobs;

public class JobSubmissionServiceTests
{
    private static (JobSubmissionService Service, SqliteLabelJobStore Store) CreateService()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);
        var service = new JobSubmissionService(queue, new ZplEncoder(), new GdiTextRasterizer(), dpi: 203);
        return (service, store);
    }

    private static SubmitJobRequest CreateRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels) => new(
        requestId,
        new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout),
        labels.Select(d => new LabelDto(d)).ToList());

    [Fact]
    public async Task Valid_request_should_create_job_with_encoded_zpl()
    {
        var (service, store) = CreateService();
        var request = CreateRequest("req-valid", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        Assert.Equal(LabelJobStatus.Pending, result.Job!.Status);
        var stored = await store.GetJobAsync(result.Job.Id);
        Assert.Contains("^BC", stored!.Items[0].Zpl);
    }

    [Fact]
    public async Task Duplicate_request_id_should_return_existing_job()
    {
        var (service, store) = CreateService();
        var request = CreateRequest("req-duplicate", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var first = await service.SubmitAsync(request);
        var second = await service.SubmitAsync(request);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Job!.Id, second.Job!.Id);
        Assert.Single(second.Job.Items);
    }

    [Fact]
    public async Task Missing_required_field_should_fail_with_problem_code_and_not_create_job()
    {
        var (service, store) = CreateService();
        var request = CreateRequest("req-invalid", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
        });

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(LabelProblemCodes.RequiredFieldMissing, result.ErrorCode);
        Assert.Equal("locationCode", result.FieldKey);
        Assert.Null(await store.GetJobByRequestIdAsync("req-invalid"));
    }

    [Fact]
    public async Task Chinese_label_should_encode_gf()
    {
        var (service, store) = CreateService();
        var request = CreateRequest("req-cn", new Dictionary<string, string>
        {
            ["zone"] = "中文区域",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl);
    }

    [Fact]
    public async Task Empty_labels_should_fail_with_invalid_request()
    {
        var (service, _) = CreateService();
        var request = new SubmitJobRequest("req-empty", new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout), new List<LabelDto>());

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task Unsupported_element_should_fail_with_encode_error()
    {
        var (service, _) = CreateService();
        var layout = new LabelFrame.Core.Layout.LabelLayout
        {
            Name = "qr",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelFrame.Core.Layout.LabelQrCodeElement { SourceKey = "locationCode", XMm = 5, YMm = 5, SizeMm = 20 },
            ],
        };
        var request = new SubmitJobRequest("req-qr", new TemplateDto(LocationLabelSamples.Contract, layout), new List<LabelDto>
        {
            new(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" }),
        });

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.EncodeFailed, result.ErrorCode);
    }
}