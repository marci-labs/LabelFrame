using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Zebra;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// Zebra 原生指令编译器单测（迭代 78，#120 AC-01 / AC-02；DESIGN §5.4）：
/// 能力与参数声明（能力位真 / printMode Select）+ 文本编译正确性（整页自包含结构 / ^A 字体与字高 DPI 换算 /
/// 超长文本不换行不缩放直接输出 / 锚定与对齐起始坐标换算路径 / 中文 LF_ENC_002 显式拒绝）。
/// 编译输出为确定性文本，不依赖真机（§5.4.5 单测口径）；真机效果验收归 AC-06（转待验收）。
/// </summary>
public class ZebraLabelCompilerTests
{
    private static readonly ZebraLabelCompiler Compiler = new();

    private static LabelCommandCompileOptions Options(int dpi = 203)
        => new(dpi, new TransportPluginContext(TextWriter.Null, Path.GetTempPath()));

    /// <summary>默认文本元素：绑定字段 code，(2,2)mm，字高 5mm，字体 0。</summary>
    private static LabelTextElement Text(
        double xMm = 2,
        double yMm = 2,
        double fontHeightMm = 5,
        string fontName = "0",
        double fontWidthMm = 0,
        bool bold = false,
        double widthMm = 0,
        double heightMm = 0,
        LabelTextAlign textAlign = LabelTextAlign.Left,
        LabelVerticalAlign verticalAlign = LabelVerticalAlign.Middle,
        string? regionId = null,
        LabelRegionAlign? regionHAlign = null)
        => new()
        {
            SourceKey = "code",
            XMm = xMm,
            YMm = yMm,
            FontHeightMm = fontHeightMm,
            FontName = fontName,
            FontWidthMm = fontWidthMm,
            Bold = bold,
            WidthMm = widthMm,
            HeightMm = heightMm,
            TextAlign = textAlign,
            VerticalAlign = verticalAlign,
            RegionId = regionId,
            RegionHAlign = regionHAlign,
        };

    private static LabelDocument Document(string text = "A-01", LabelTextElement? textElement = null, params LabelElement[] extraElements)
        => new()
        {
            Layout = new LabelLayout
            {
                Name = "t",
                ContractName = "c",
                ContractVersion = "1.0",
                WidthMm = 40,
                HeightMm = 20,
                Elements = textElement is null
                    ? [Text(), .. extraElements]
                    : [textElement, .. extraElements],
            },
            Data = new Dictionary<string, string> { ["code"] = text },
        };

    private static LabelCommandCompileResult Compile(LabelDocument document, int dpi = 203)
        => Compiler.CompileAsync(document, Options(dpi)).GetAwaiter().GetResult();

    private static LabelCommandCompileResult CompilePlugin(LabelDocument document, int dpi = 203)
        => new ZebraTransportPlugin().CompileAsync(document, Options(dpi)).GetAwaiter().GetResult();

    // ── AC-01：能力与参数声明 ──

    [Fact]
    public void Plugin_should_implement_compiler_and_registry_capability_should_be_true()
    {
        // 能力实现于插件对象（§5.4.1）：注册表装配期判定 plugin is ILabelCommandCompiler → 能力位真；
        // GET /api/transport 的 DTO 为 descriptor 字段透传（#119 WinHostEndpointsTests 已锚定映射），此处锚定源头
        var plugin = new ZebraTransportPlugin();
        Assert.IsAssignableFrom<ILabelCommandCompiler>(plugin);

        var registry = new TransportPluginRegistry();
        registry.Register(plugin);
        var descriptor = registry.GetPlugin(TransportPluginIdPolicy.ZebraPluginId);
        Assert.NotNull(descriptor);
        Assert.True(descriptor!.SupportsDocumentCompile);
        Assert.Equal(plugin, registry.GetCommandCompiler(TransportPluginIdPolicy.ZebraPluginId));
    }

    [Fact]
    public void Plugin_should_declare_print_mode_select_parameter()
    {
        // §5.4.2 决议 2：Select 参数 Key printMode、必填、默认 image、Options image / native
        var spec = new ZebraTransportPlugin().Parameters.Single(p => p.Key == TransportPrintMode.ParameterKey);
        Assert.Equal(TransportParameterType.Select, spec.Type);
        Assert.True(spec.Required);
        Assert.Equal(TransportPrintMode.Image, spec.DefaultValue);
        Assert.Equal(
            new[] { TransportPrintMode.Image, TransportPrintMode.Native },
            spec.Options!.Select(o => o.Value).ToArray());
    }

    // ── AC-02：整页自包含结构（^XA / ^XZ / ^PW / ^LL）与文本指令 ──

    [Fact]
    public void Compile_should_produce_self_contained_page_structure()
    {
        var result = Compile(Document());

        Assert.NotNull(result.Command);
        Assert.Null(result.ErrorCode);
        var lines = result.Command!.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("^XA", lines[0]);
        Assert.Equal("^XZ", lines[^1]);
        // 40mm / 20mm @203dpi → 320 / 160 点（毫米 → 点按 DPI 换算）
        Assert.Equal("^PW320", lines[1]);
        Assert.Equal("^LL160", lines[2]);
        // ^CI28 = UTF-8 国际编码（Latin-1 字符通道）
        Assert.Contains("^CI28", result.Command);
    }

    [Fact]
    public void Compile_should_scale_coordinates_and_font_height_by_dpi()
    {
        // 同一文档 300 dpi：2mm → 24 点、字高 4mm → 47 点（DPI 换算路径）
        var result = Compile(Document(text: "A-1", textElement: Text(fontHeightMm: 4)), dpi: 300);

        Assert.Contains("^FO24,24^A0N,47^FH^FDA-1^FS", result.Command);
        Assert.Contains("^PW472", result.Command);
        Assert.Contains("^LL236", result.Command);
    }

    [Fact]
    public void Compile_should_use_template_font_and_bold_variant_mapping()
    {
        // 模板保留的 ZPL 内置字体名原样输出（^ABN）；默认字体 0（缩放字体）
        Assert.Contains("^FO16,16^ABN,40,40^FH^FDA-01^FS", Compile(Document(textElement: Text(fontName: "B", fontWidthMm: 5))).Command);
        Assert.Contains("^FO16,16^A0N,40^FH^FDA-01^FS", Compile(Document()).Command);

        // 加粗近似（决策 #49 方案 A）：粗体字体变体映射 0 → 1
        Assert.Contains("^A1N,40", Compile(Document(textElement: Text(bold: true))).Command);
        // 无粗体变体映射的字体：加粗忽略（尽力项，效果以真机验收为准）
        Assert.Contains("^AAN,40", Compile(Document(textElement: Text(fontName: "A", bold: true))).Command);
    }

    [Fact]
    public void Compile_should_escape_caret_tilde_and_underscore_via_field_hex()
    {
        // ^FH 十六进制转义（官方口径）：^ / ~ / _ 不进入指令层
        var result = Compile(Document(text: "a^b~c_d"));

        Assert.Contains("^FH^FDa_5Eb_7Ec_5Fd^FS", result.Command);
    }

    [Fact]
    public void Compile_should_accept_latin1_characters()
    {
        var result = Compile(Document(text: "café"));

        Assert.Null(result.ErrorCode);
        Assert.Contains("café", result.Command);
    }

    [Fact]
    public void Compile_should_skip_empty_text_without_field_command()
    {
        var result = Compile(Document(text: string.Empty));

        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain("^FD", result.Command);
    }

    // ── AC-02：超长文本不换行不缩放直接输出（§5.4.1 排版降级——溢出为预期常态）──

    [Fact]
    public void Compile_should_output_overlong_text_as_single_line_without_scaling()
    {
        var longText = new string('A', 60) + "-" + new string('B', 60); // 远超 40mm 版心
        var result = Compile(Document(text: longText));

        // 不换行：单个 ^FD 输出全部内容；不缩放：字高仍按模板 5mm（40 点）
        Assert.Null(result.ErrorCode);
        Assert.Contains($"^A0N,40^FH^FD{longText}^FS", result.Command);
        Assert.Equal(1, CountOccurrences(result.Command!, "^FD"));
    }

    // ── AC-02：锚定 / 对齐的起始坐标换算路径（尽力近似：近似路径存在即可，不要求与图片模式等价）──

    [Fact]
    public void Compile_should_shift_origin_by_region_anchor_alignment_with_builtin_metrics()
    {
        // 区域 (10,20) 80×30mm；自动宽度文本 "ABC"（缩放字体 5mm，实测 (667+667+722)/1000×5 = 10.28mm）：
        // Start → x=10mm（80 点）；End → x=79.72mm（638 点）；y 垂直居中 → 32.5mm（260 点）
        LabelDocument Create(LabelRegionAlign? align)
        {
            var region = new LabelRegionElement { Id = "r1", XMm = 10, YMm = 20, WidthMm = 80, HeightMm = 30 };
            return Document(text: "ABC", textElement: Text(regionId: "r1", regionHAlign: align), region);
        }

        var start = Compile(Create(LabelRegionAlign.Start)).Command!;
        var end = Compile(Create(LabelRegionAlign.End)).Command!;

        Assert.Contains("^FO80,260", start); // Start：区域左缘（10mm = 80 点）
        Assert.Contains("^FO637,260", end); // End：按内置字体度量右移 79.72mm = 637 点（锚定换算路径可断言）
    }

    [Fact]
    public void Compile_should_approximate_block_text_align_and_vertical_align()
    {
        // 显式块宽 30mm、块高 10mm、字高 5mm，文本 "A-1"（实测 (667+333+556)/1000×5 = 7.78mm）：
        // TextAlign Center → x = 2 + (30-7.78)×0.5 = 13.11mm（105 点）；VerticalAlign Bottom → y = 2 + (10-5)×1 = 7mm（56 点）
        var result = Compile(Document(
            text: "A-1",
            textElement: Text(widthMm: 30, heightMm: 10, textAlign: LabelTextAlign.Center, verticalAlign: LabelVerticalAlign.Bottom)));

        Assert.Contains("^FO105,56^A0N,40", result.Command);
    }

    [Fact]
    public void Region_element_should_compile_as_container_without_graphic_command()
    {
        // 区域 = 版式容器：仅参与锚定解析，自身不产出指令；边框为图片模式能力（§5.4.1 裁剪）
        var region = new LabelRegionElement { Id = "r1", XMm = 1, YMm = 1, WidthMm = 10, HeightMm = 10, BorderMm = 0.2 };
        var result = Compile(Document(extraElements: region));

        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain("^GB", result.Command);
        Assert.Equal(1, CountOccurrences(result.Command!, "^FD"));
    }

    // ── AC-02：中文（无字库）→ LF_ENC_002 显式拒绝（§5.4.4）──

    [Fact]
    public void Chinese_text_should_be_rejected_with_lf_enc_002_and_field_key()
    {
        var result = Compile(Document(text: "库位A-01"));

        Assert.Null(result.Command);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("内置字体无法表示的字符", result.ErrorMessage);
        Assert.Contains("5E93", result.ErrorMessage); // 首个问题字符码点「库」U+5E93（可定位）
        Assert.Equal("code", result.FieldKey);
    }

    [Theory]
    [InlineData("中文")]
    [InlineData("カナ")]
    [InlineData("한글")]
    [InlineData("🙂")]
    public void Unsupported_scripts_should_be_rejected(string text)
    {
        var result = Compile(Document(text: text));

        Assert.Null(result.Command);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
    }

    [Fact]
    public void Control_characters_should_be_rejected()
    {
        var result = Compile(Document(text: "A\r\nB"));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
    }

    // ── 范围外元素：显式失败（§5.4.4；条码 / 二维码归迭代 79，图片 / 线元素按裁剪拒绝）──

    [Fact]
    public void Barcode_element_should_fail_explicitly()
    {
        var result = Compile(Document(extraElements: new LabelBarcodeElement { SourceKey = "code", YMm = 10, HeightMm = 8 }));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("条码", result.ErrorMessage);
    }

    [Fact]
    public void Qrcode_element_should_fail_explicitly()
    {
        var result = Compile(Document(extraElements: new LabelQrCodeElement { SourceKey = "code", YMm = 10, SizeMm = 8 }));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("二维码", result.ErrorMessage);
    }

    [Fact]
    public void Image_element_should_fail_explicitly()
    {
        var result = Compile(Document(extraElements: new LabelImageElement { SourceKey = "logo", YMm = 10, WidthMm = 8, HeightMm = 8 }));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("图片", result.ErrorMessage);
    }

    [Fact]
    public void Line_element_should_fail_explicitly()
    {
        var result = Compile(Document(extraElements: new LabelLineElement { XMm = 1, YMm = 18, X2Mm = 39, Y2Mm = 18, ThicknessMm = 0.3 }));

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("线", result.ErrorMessage);
    }

    // ── 输入口径：DPI 非法显式失败；插件对象与编译器同结果（能力委托）──

    [Fact]
    public void Invalid_dpi_should_fail_explicitly()
    {
        var result = Compile(Document(), dpi: 0);

        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("DPI", result.ErrorMessage);
    }

    [Fact]
    public void Plugin_compile_should_delegate_to_compiler()
    {
        Assert.Equal(Compile(Document()).Command, CompilePlugin(Document()).Command);
        Assert.Equal(Compile(Document(text: "库位")).ErrorCode, CompilePlugin(Document(text: "库位")).ErrorCode);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

/// <summary>
/// Zebra 内置字体度量（ITextWidthMeasurer 编译侧实现，决策 #110）单测：锚定近似换算的确定性口径。
/// </summary>
public class ZebraTextWidthMeasurerTests
{
    [Fact]
    public void Scalable_font_width_should_be_proportional_to_height()
    {
        var measurer = new ZebraTextWidthMeasurer(203);
        var element = new LabelTextElement { SourceKey = "c", FontHeightMm = 5 };

        // "ii"：222/1000 × 2 × 5mm = 2.22mm（Helvetica 同度量近似）；字高翻倍宽度翻倍（比例度量与 DPI 无关）
        Assert.Equal(2.22, measurer.MeasureWidthMm(element, "ii"), precision: 3);
        Assert.Equal(4.44, measurer.MeasureWidthMm(new LabelTextElement { SourceKey = "c", FontHeightMm = 10 }, "ii"), precision: 3);
    }

    [Fact]
    public void Bitmap_font_width_should_scale_by_design_cell()
    {
        var measurer = new ZebraTextWidthMeasurer(203);
        var element = new LabelTextElement { SourceKey = "c", FontHeightMm = 5, FontName = "A" };

        // 字体 A 设计 9×5 点 @203dpi：5mm = 40 点 → 倍率 40/9；两字符宽（点）= 2×5×(40/9)，换算毫米
        var expected = 2 * 5 * (40.0 / 9) * 25.4 / 203;
        Assert.Equal(expected, measurer.MeasureWidthMm(element, "AB"), precision: 6);
    }

    [Fact]
    public void Empty_value_should_measure_zero()
    {
        var measurer = new ZebraTextWidthMeasurer(203);
        Assert.Equal(0, measurer.MeasureWidthMm(new LabelTextElement { SourceKey = "c", FontHeightMm = 5 }, string.Empty));
    }
}
