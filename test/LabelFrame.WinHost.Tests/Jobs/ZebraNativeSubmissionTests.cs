using LabelFrame.Api;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Rendering;
using LabelFrame.TransportPlugin.Zebra;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Tests.Transport;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>
/// 宿主集成（迭代 78，#120 AC-03；DESIGN §5.4.3）：native 分派走真实 Zebra 编译器——
/// 本地作业指令字段（LabelJobItem.Zpl）为品牌原生指令（^A0 / ^FD，非 ^GF 位图）；
/// image / 缺省路径经真实 Zebra 插件连接零回归（仍整版渲染 ^GF）。
/// 与迭代 77 的 JobSubmissionCompileTests（fake 编译器）互补：此处为真实品牌编译器锚定。
/// </summary>
public class ZebraNativeSubmissionTests
{
    /// <summary>构造提交服务：注册表含内置插件 + 真实 Zebra 官方插件，connection.json 预写指定内容。</summary>
    private static (JobSubmissionService Service, SqliteLabelJobStore Store) CreateService(string connectionJson)
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);

        var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        templates.InitializeAsync().GetAwaiter().GetResult();

        var (manager, registry) = TestTransportRegistry.CreateManagerWithRegistry(
            configure: r => r.Register(new ZebraTransportPlugin()),
            connectionJson: connectionJson);
        var service = new JobSubmissionService(
            queue,
            new ZplImageEncoder(),
            dpi: 203,
            new SkiaLabelRenderer(),
            templates,
            manager,
            registry,
            TestTransportRegistry.CreateContext(),
            TextWriter.Null);
        return (service, store);
    }

    /// <summary>纯文本模板（native 编译本轮只支持文本元素；条码 / 二维码归迭代 79）。</summary>
    private static TemplateDto CreateTemplate() => new(
        new Core.Contracts.LabelContract
        {
            Name = "text-label",
            Version = "1.0",
            Fields = [new Core.Contracts.LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
        },
        new Core.Layout.LabelLayout
        {
            Name = "text-label-40x20",
            ContractName = "text-label",
            ContractVersion = "1.0",
            WidthMm = 40,
            HeightMm = 20,
            Elements =
            [
                new Core.Layout.LabelTextElement { SourceKey = "code", XMm = 2, YMm = 2, FontHeightMm = 4 },
            ],
        });

    private static SubmitJobRequest CreateRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels)
        => new(requestId, CreateTemplate(), labels.Select(d => new LabelDto(d)).ToList());

    private const string NativeConnection =
        """{ "pluginId": "labelframe-transport-zebra", "params": { "kind": "Tcp", "host": "127.0.0.1", "printMode": "native" } }""";

    private const string ImageConnection =
        """{ "pluginId": "labelframe-transport-zebra", "params": { "kind": "Tcp", "host": "127.0.0.1" } }""";

    // ── AC-03：native 分派走真实 Zebra 编译器（作业指令字段为原生指令）──

    [Fact]
    public async Task Native_mode_should_dispatch_to_real_zebra_compiler()
    {
        var (service, store) = CreateService(NativeConnection);

        var result = await service.SubmitAsync(CreateRequest(
            "req-zebra-native",
            new Dictionary<string, string> { ["code"] = "A-1" },
            new Dictionary<string, string> { ["code"] = "B-2" }));

        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Equal(2, stored!.Items.Count);

        // 逐张编译为品牌原生指令：整页自包含结构 + ^A0 文本指令，非 ^GF 位图
        var first = stored.Items[0].Zpl;
        Assert.StartsWith("^XA", first);
        Assert.EndsWith("^XZ", first);
        Assert.Contains("^PW320", first);
        Assert.Contains("^FO16,16^A0N,32^FH^FDA-1^FS", first);
        Assert.Contains("^FH^FDB-2^FS", stored.Items[1].Zpl); // 逐张内容区分
        Assert.DoesNotContain("^GF", first);
    }

    // ── AC-03：image / 缺省路径零回归（经真实 Zebra 插件连接，无 printMode → 既有整版渲染）──

    [Fact]
    public async Task Image_mode_with_real_plugin_should_render_bitmap_as_before()
    {
        var (service, store) = CreateService(ImageConnection);

        var result = await service.SubmitAsync(CreateRequest("req-zebra-image", new Dictionary<string, string> { ["code"] = "A-1" }));

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        // 既有图片路径：Skia 整版渲染 ^GF 位图（打印方式缺省回退 image）
        Assert.Contains("^GF", stored!.Items[0].Zpl);
        Assert.DoesNotContain("^FD", stored.Items[0].Zpl);
    }

    // ── 端到端：中文经宿主提交链路显式失败（LF_ENC_002 + 插件 id + 中文原因 + fieldKey）──

    [Fact]
    public async Task Native_mode_chinese_text_should_fail_with_plugin_reason()
    {
        var (service, store) = CreateService(NativeConnection);

        var result = await service.SubmitAsync(CreateRequest("req-zebra-chinese", new Dictionary<string, string> { ["code"] = "库位A" }));

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("labelframe-transport-zebra", result.ErrorMessage);
        Assert.Contains("内置字体无法表示的字符", result.ErrorMessage);
        Assert.Contains("切回图片", result.ErrorMessage);
        Assert.Equal("code", result.FieldKey);
        Assert.Null(await store.GetJobByRequestIdAsync("req-zebra-chinese"));
    }
}
