using System.Globalization;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>
/// Zebra 原生指令编译器（迭代 78，DESIGN §5.4；实现 2/4——文本元素先行）：
/// 把 <see cref="LabelDocument"/> 编译为整页自包含 ZPL 文本（^XA / ^XZ / ^PW / ^LL + 逐元素指令），
/// 宿主不包装追加（§5.4.1 输出口径）。纯函数性质：同输入同输出、无副作用、不访问打印机。
/// </summary>
/// <remarks>
/// 功能裁剪原则（§5.4.1，图片模式 = 全功能基准）：排版增强（wrap / fitMode / lineHeight）一律不实现——
/// 超长文本不换行不缩放直接输出，溢出为预期常态；对齐类能力（区域锚定 / 块内 TextAlign / 垂直对齐）无指令
/// 原生等价，按内置字体度量（<see cref="ZebraTextWidthMeasurer"/>）在编译期换算起始坐标作尽力近似，
/// 换算路径复用宿主同一 <see cref="LabelLayoutResolver"/>（决策 #110）。条码 / 二维码（迭代 79）与图片 / 线元素
/// 不在本轮：含此类元素的文档按编译失败显式拒绝（§5.4.4，LF_ENC_002），不自动回退图片。
/// 中英文口径：仅接受打印机内置字体可覆盖的字符（可打印 ASCII + Latin-1）——中文等未覆盖字符（无中文字库）
/// 显式拒绝（LF_ENC_002，§5.4.4「首版不做元素级混合模式」）。
/// </remarks>
public sealed class ZebraLabelCompiler : ILabelCommandCompiler
{
    /// <summary>加粗近似：粗体字体变体映射（决策 #49 方案 A；不同机型可调整映射表，效果以真机验收为准）。</summary>
    internal static readonly IReadOnlyDictionary<char, char> BoldFontVariantMap = new Dictionary<char, char>
    {
        ['0'] = '1', // 缩放字体 0 → 粗体变体 1
    };

    /// <inheritdoc />
    public Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.Dpi <= 0)
        {
            return Task.FromResult(Fail($"DPI 必须为正整数（当前 {options.Dpi}）。", null));
        }

        var layout = document.Layout;
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var measurer = new ZebraTextWidthMeasurer(options.Dpi);

        // 整页自包含结构：格式开始 / 页宽 / 页长 / 国际编码（^CI28 = UTF-8，覆盖 Latin-1 字符）由编译器负责
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^PW{ToDots(layout.WidthMm, options.Dpi)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^LL{ToDots(layout.HeightMm, options.Dpi)}");
        sb.AppendLine("^CI28");

        foreach (var element in layout.Elements)
        {
            switch (element)
            {
                case LabelRegionElement:
                    // 区域 = 版式容器：仅参与元素锚定解析（#110），自身不产出指令；边框为图片模式能力，原生模式裁剪（§5.4.1 预期差异）
                    continue;
                case LabelTextElement text:
                    var compiled = CompileText(text, document, regions, measurer, options.Dpi);
                    if (compiled.ErrorCode is not null)
                    {
                        return Task.FromResult(compiled);
                    }

                    sb.Append(compiled.Command);
                    break;
                case LabelBarcodeElement:
                case LabelQrCodeElement:
                    return Task.FromResult(Fail("含条码 / 二维码元素：原生指令编译暂不支持（后续迭代提供），可将打印方式切回图片。", null));
                case LabelImageElement:
                    return Task.FromResult(Fail("含图片元素：打印机内置指令无法原生表示位图元素，可将打印方式切回图片。", null));
                case LabelLineElement:
                    return Task.FromResult(Fail("含线元素：原生指令编译暂不支持，可将打印方式切回图片。", null));
                default:
                    return Task.FromResult(Fail($"含不支持的元素类型：{element.GetType().Name}。", null));
            }
        }

        sb.Append("^XZ");
        return Task.FromResult(new LabelCommandCompileResult(sb.ToString(), null, null, null));
    }

    /// <summary>编译单个文本元素：定位（锚定 / 对齐尽力近似）+ ^A 字体 + ^FD 内容。</summary>
    private static LabelCommandCompileResult CompileText(
        LabelTextElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        ZebraTextWidthMeasurer measurer,
        int dpi)
    {
        string value;
        try
        {
            value = LabelElementContent.Get(element, document.Data);
        }
        catch (ArgumentException ex)
        {
            // 缺字段数据（宿主提交链路已先行契约校验，此为直连编译器调用的防御口径）
            return Fail(ex.Message, element.SourceKey);
        }

        if (value.Length == 0)
        {
            return new LabelCommandCompileResult(string.Empty, null, null, null);
        }

        // 字符覆盖检查（§5.4.4）：内置字体无法表示的字符（中文等）显式拒绝，不输出乱码
        foreach (var c in value)
        {
            if (!IsSupportedChar(c))
            {
                return Fail(
                    $"文本值含内置字体无法表示的字符「{c}」（U+{(int)c:X4}）：原生指令模式不支持打印机内置字体未覆盖的字符（如未安装中文字库时的中文）。",
                    element.SourceKey);
            }
        }

        // 字体：模板保留的 ZPL 内置字体名（默认 0 = 缩放字体）；加粗近似 = 粗体字体变体映射（决策 #49 方案 A）
        var fontChar = ResolveFontChar(element.FontName);
        if (element.Bold && BoldFontVariantMap.TryGetValue(fontChar, out var boldChar))
        {
            fontChar = boldChar;
        }

        var heightDots = Math.Max(1, ToDots(element.FontHeightMm, dpi)); // ≤0 兜底 1 点（与渲染口径一致）
        var widthFragment = element.FontWidthMm > 0
            ? $",{Math.Max(1, ToDots(element.FontWidthMm, dpi))}"
            : string.Empty; // 字宽缺省 = 按字体标准纵横比（ZPL ^A 口径）

        // 起始坐标换算路径（§5.4.1 解析几何，尽力近似）：与图片渲染共用 LabelLayoutResolver，
        // 区域锚定偏移按本品牌内置字体度量计算（决策 #110）
        var bounds = LabelLayoutResolver.ResolveBounds(element, regions, measurer, value);
        var xMm = bounds.XMm + element.EffectivePaddingHMm;
        var yMm = bounds.YMm + element.EffectivePaddingVMm;

        // 块内对齐尽力近似：显式块宽时按实测文本宽度偏移（TextAlign）；块高时按（块高 - 字高）偏移（VerticalAlign）
        if (element.WidthMm > 0)
        {
            var measuredMm = measurer.MeasureWidthMm(element, value);
            xMm += Math.Max(0, element.WidthMm - measuredMm) * AlignFactor(element.TextAlign);
        }

        var blockHeightMm = element.HeightMm > 0 ? element.HeightMm : element.FontHeightMm;
        yMm += Math.Max(0, blockHeightMm - element.FontHeightMm) * VerticalFactor(element.VerticalAlign);

        // 超长文本不换行不缩放直接输出（§5.4.1 排版降级）：不做溢出校验、不截断
        var command =
            $"^FO{ToDots(xMm, dpi)},{ToDots(yMm, dpi)}" +
            $"^A{fontChar}N,{heightDots}{widthFragment}" +
            $"^FH^FD{EscapeFieldData(value)}^FS" + Environment.NewLine;
        return new LabelCommandCompileResult(command, null, null, null);
    }

    /// <summary>内置字体可表示的字符：可打印 ASCII（U+0020..U+007E）+ Latin-1 补充（U+00A0..U+00FF）。</summary>
    internal static bool IsSupportedChar(char c)
        => c is (>= (char)0x20 and <= (char)0x7E) or (>= (char)0xA0 and <= (char)0xFF);

    /// <summary>字体字符解析：合法内置字体字符（A-Z / 0-9）原样，其余回退默认缩放字体 0。</summary>
    internal static char ResolveFontChar(string? fontName)
        => fontName is { Length: 1 } && ZebraBuiltInFontMetrics.IsBuiltInFontChar(fontName[0])
            ? fontName[0]
            : '0';

    /// <summary>
    /// 字段数据转义（^FH 十六进制转义，官方口径）：^ / ~ / _ 三类指令控制字符以 _HH 表示，
    /// 其余字符原样（UTF-8 多字节序列不含 ASCII 字节，与转义不冲突）。
    /// 注意先转义 _：后转义会破坏已引入的 _5E / _7E 前缀（二次替换）。
    /// </summary>
    internal static string EscapeFieldData(string value)
        => value
            .Replace("_", "_5F", StringComparison.Ordinal)
            .Replace("^", "_5E", StringComparison.Ordinal)
            .Replace("~", "_7E", StringComparison.Ordinal);

    private static LabelCommandCompileResult Fail(string reason, string? fieldKey)
        => new(null, JobErrorCodes.CommandCompileFailed, reason, fieldKey);

    private static double AlignFactor(LabelTextAlign align) => align switch
    {
        LabelTextAlign.Left => 0,
        LabelTextAlign.Center => 0.5,
        LabelTextAlign.Right => 1,
        _ => 0,
    };

    private static double VerticalFactor(LabelVerticalAlign align) => align switch
    {
        LabelVerticalAlign.Top => 0,
        LabelVerticalAlign.Middle => 0.5,
        LabelVerticalAlign.Bottom => 1,
        _ => 0.5,
    };

    /// <summary>毫米 → 点，四舍五入（远离零）；与图片模式 ZplImageEncoder 同口径。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}
