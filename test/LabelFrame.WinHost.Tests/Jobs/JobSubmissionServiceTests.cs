using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.WinHost.Tests.Samples;
using LabelFrame.Core.Validation;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Tests.Transport;

namespace LabelFrame.WinHost.Tests.Jobs;

public class JobSubmissionServiceTests
{
    private static (JobSubmissionService Service, SqliteLabelJobStore Store, TemplateStore Templates) CreateService()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);

        var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        templates.InitializeAsync().GetAwaiter().GetResult();

        var transportManager = TestTransportRegistry.CreateManager(new HostOptions { Transport = TransportMode.Log });
        var service = new JobSubmissionService(queue, new ZplImageEncoder(), dpi: 203, new SkiaLabelRenderer(), templates, transportManager, TextWriter.Null);
        return (service, store, templates);
    }

    private static SubmitJobRequest CreateRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels) => new(
        requestId,
        new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout),
        labels.Select(d => new LabelDto(d)).ToList());

    [Fact]
    public async Task Valid_request_should_create_job_with_encoded_gf_image()
    {
        var (service, store, _) = CreateService();
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
        // 迭代 15：恒为整版位图（^GF），不再有元素级 ^BC
        Assert.Contains("^GF", stored!.Items[0].Zpl);
        Assert.DoesNotContain("^BC", stored.Items[0].Zpl);
    }

    [Fact]
    public async Task Duplicate_request_id_should_return_existing_job()
    {
        var (service, store, _) = CreateService();
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
        var (service, store, _) = CreateService();
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
        var (service, store, _) = CreateService();
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
    public async Task Every_job_should_encode_whole_label_as_gf()
    {
        var (service, store, _) = CreateService();
        var request = CreateRequest("req-image", new Dictionary<string, string>
        {
            ["zone"] = "A-01",
            ["locationCode"] = "A-01-02-03",
        });

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        var zpl = stored!.Items[0].Zpl;
        // 100mm x 60mm @203dpi => PW799 / LL480；整版 ^GF，无元素级 ^BC
        Assert.StartsWith("^XA", zpl);
        Assert.Contains("^PW799", zpl);
        Assert.Contains("^LL480", zpl);
        Assert.Contains("^FO0,0^GFA,", zpl);
        Assert.DoesNotContain("^BC", zpl);
    }

    [Fact]
    public async Task Empty_labels_should_fail_with_invalid_request()
    {
        var (service, _, _) = CreateService();
        var request = new SubmitJobRequest("req-empty", new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout), new List<LabelDto>());

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task Missing_data_key_should_render_empty_and_create_job()
    {
        var (service, store, _) = CreateService();
        var layout = new LabelFrame.Core.Layout.LabelLayout
        {
            Name = "missing-key",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelFrame.Core.Layout.LabelTextElement { SourceKey = "notInData", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 },
            ],
        };
        var request = new SubmitJobRequest("req-missing-key", new TemplateDto(LocationLabelSamples.Contract, layout), new List<LabelDto>
        {
            new(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" }),
        });

        var result = await service.SubmitAsync(request);

        // 迭代 15：图片渲染容错（TryGet），缺失的非必填字段渲染为空文本，作业正常创建（与预览一致）
        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl);
    }
}
