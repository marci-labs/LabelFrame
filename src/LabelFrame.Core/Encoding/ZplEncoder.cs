using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Encoding;

/// <summary>
/// ZPL 编码器：文本（^A）、Code128（^BC）、图片占位（^FX 注释）。
/// 毫米 → 点按 DPI 换算；二维码 / 线元素在模型中定义，但迭代 1 明确报错（迭代 2 补全）。
/// </summary>
public sealed class ZplEncoder : IZplEncoder
{
    /// <summary>默认打印机分辨率（203 dpi，Zebra 常见）。</summary>
    public const int DefaultDpi = 203;

    /// <inheritdoc />
    public string Encode(LabelDocument document, int dpi = DefaultDpi)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 必须为正整数。");
        }

        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        foreach (var element in document.Layout.Elements)
        {
            AppendElement(sb, element, document.Data, document.Images, dpi);
        }

        sb.Append("^XZ");
        return sb.ToString();
    }

    private static void AppendElement(
        StringBuilder sb,
        LabelElement element,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, LabelBitmap> images,
        int dpi)
    {
        switch (element)
        {
            case LabelTextElement text:
                AppendText(sb, text, data, dpi);
                break;
            case LabelBarcodeElement barcode:
                AppendBarcode(sb, barcode, data, dpi);
                break;
            case LabelImageElement image:
                if (images.TryGetValue(image.SourceKey, out var bitmap))
                {
                    AppendImage(sb, image, bitmap, dpi);
                }
                else
                {
                    AppendImagePlaceholder(sb, image);
                }

                break;
            default:
                throw new NotSupportedException(
                    $"ZPL 编码器暂不支持 {element.Type} 元素（{element.GetType().Name}），计划在迭代 2 补全。");
        }
    }

    /// <summary>毫米 → 点，四舍五入（远离零）。</summary>
    private static int ToDots(double mm, int dpi)
        => (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero);

    private static string GetData(IReadOnlyDictionary<string, string> data, string key)
    {
        if (!data.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"标签文档缺少字段数据：{key}。", nameof(data));
        }

        return value;
    }

    private static void AppendText(StringBuilder sb, LabelTextElement text, IReadOnlyDictionary<string, string> data, int dpi)
    {
        var value = GetData(data, text.SourceKey);
        var x = ToDots(text.XMm, dpi);
        var y = ToDots(text.YMm, dpi);
        var height = ToDots(text.FontHeightMm, dpi);
        var width = ToDots(text.FontWidthMm, dpi);
        var (escaped, needsFieldHex) = EscapeFieldData(value);

        sb.Append($"^FO{x},{y}^A{text.FontName}N,{height},{width}");
        if (needsFieldHex)
        {
            sb.Append("^FH");
        }

        sb.Append($"^FD{escaped}^FS").AppendLine();
    }

    private static void AppendBarcode(StringBuilder sb, LabelBarcodeElement barcode, IReadOnlyDictionary<string, string> data, int dpi)
    {
        var value = GetData(data, barcode.SourceKey);
        var x = ToDots(barcode.XMm, dpi);
        var y = ToDots(barcode.YMm, dpi);
        var height = ToDots(barcode.HeightMm, dpi);
        var module = Math.Clamp(barcode.ModuleWidth, 1, 10);
        var (escaped, needsFieldHex) = EscapeFieldData(value);

        sb.Append($"^FO{x},{y}^BY{module},3^BCN,{height},Y,N,N");
        if (needsFieldHex)
        {
            sb.Append("^FH");
        }

        sb.Append($"^FD{escaped}^FS").AppendLine();
    }

    /// <summary>
    /// 图片位图编码：^GF 按 1bpp、行字节对齐、十六进制（ASCII）输出，
    /// 每个像素对应 1 个打印点，位置取元素左上角。
    /// </summary>
    private static void AppendImage(StringBuilder sb, LabelImageElement image, LabelBitmap bitmap, int dpi)
    {
        var x = ToDots(image.XMm, dpi);
        var y = ToDots(image.YMm, dpi);
        var totalBytes = bitmap.RowBytes * bitmap.Height;
        var hex = Convert.ToHexString(bitmap.Pixels);
        sb.Append($"^FO{x},{y}^GFA,{totalBytes},{totalBytes},{bitmap.RowBytes},{hex}^FS").AppendLine();
    }

    /// <summary>
    /// 图片占位：未提供位图数据时输出 ^FX 注释（不打印），记录图片键、位置与尺寸。
    /// </summary>
    private static void AppendImagePlaceholder(StringBuilder sb, LabelImageElement image)
    {
        sb.Append(
            $"^FX image:{image.SourceKey} placeholder ({image.WidthMm}mm x {image.HeightMm}mm) at ({image.XMm}mm,{image.YMm}mm) iteration 2^FS")
            .AppendLine();
    }

    /// <summary>
    /// 转义 ^FD 字段数据：^ / ~ / _ 使用 ^FH 十六进制转义（_XX），
    /// 避免与 ZPL 命令或转义符冲突；无需转义时不输出 ^FH。
    /// </summary>
    private static (string Escaped, bool NeedsFieldHex) EscapeFieldData(string value)
    {
        var sb = new StringBuilder(value.Length);
        var needsFieldHex = false;
        foreach (var c in value)
        {
            if (c is '^' or '~' or '_')
            {
                needsFieldHex = true;
                sb.Append('_').Append(((int)c).ToString("X2"));
            }
            else
            {
                sb.Append(c);
            }
        }

        return (sb.ToString(), needsFieldHex);
    }
}