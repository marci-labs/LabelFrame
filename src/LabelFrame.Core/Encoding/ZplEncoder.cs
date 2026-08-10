using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Encoding;

/// <summary>
/// ZPL 编码器：文本（^A / ^FB 块对齐）、Code128（^BC）、二维码（^BQ）、图片（^GF）、
/// 线（^GB L）、区域边框（^GB B）；毫米 → 点按 DPI 换算；区域锚定按 LabelLayoutResolver 计算位置。
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

        var regions = LabelLayoutResolver.IndexRegions(document.Layout);
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        // 显式声明打印宽度与标签长度（毫米 → 点），避免打印机沿用旧的长度设置导致一张作业走多张纸
        sb.AppendLine($"^PW{Math.Max(1, ToDots(document.Layout.WidthMm, dpi))}");
        sb.AppendLine($"^LL{Math.Max(1, ToDots(document.Layout.HeightMm, dpi))}");
        foreach (var element in document.Layout.Elements)
        {
            AppendElement(sb, element, document.Data, document.Images, regions, dpi);
        }

        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>整版位图编码：把整张标签的 1bpp 位图经 ^GF 输出（图片打印模式，所见即所得）。</summary>
    public string EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi = DefaultDpi)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 必须为正整数。");
        }

        var totalBytes = bitmap.RowBytes * bitmap.Height;
        var hex = Convert.ToHexString(bitmap.Pixels);
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine($"^PW{Math.Max(1, ToDots(widthMm, dpi))}");
        sb.AppendLine($"^LL{Math.Max(1, ToDots(heightMm, dpi))}");
        sb.Append($"^FO0,0^GFA,{totalBytes},{totalBytes},{bitmap.RowBytes},{hex}^FS").AppendLine();
        sb.Append("^XZ");
        return sb.ToString();
    }

    private static void AppendElement(
        StringBuilder sb,
        LabelElement element,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, LabelBitmap> images,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        switch (element)
        {
            case LabelTextElement text:
                AppendText(sb, text, data, regions, dpi);
                break;
            case LabelBarcodeElement barcode:
                AppendBarcode(sb, barcode, data, regions, dpi);
                break;
            case LabelQrCodeElement qrCode:
                AppendQrCode(sb, qrCode, data, regions, dpi);
                break;
            case LabelImageElement image:
                if (images.TryGetValue(image.SourceKey, out var bitmap))
                {
                    AppendImage(sb, image, bitmap, regions, dpi);
                }
                else
                {
                    AppendImagePlaceholder(sb, image);
                }

                break;
            case LabelLineElement line:
                AppendLine(sb, line, dpi);
                break;
            case LabelRegionElement region:
                AppendRegion(sb, region, dpi);
                break;
            default:
                throw new NotSupportedException(
                    $"ZPL 编码器暂不支持 {element.Type} 元素（{element.GetType().Name}）。");
        }
    }

    /// <summary>毫米 → 点，四舍五入（远离零）。</summary>
    private static int ToDots(double mm, int dpi)
        => (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero);


    private static void AppendText(
        StringBuilder sb,
        LabelTextElement text,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        var value = LabelElementContent.Get(text, data);
        var bounds = LabelLayoutResolver.ResolveBounds(text, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = ToDots(bounds.WidthMm, dpi);
        var fontHeight = ToDots(text.FontHeightMm, dpi);
        var fontWidth = ToDots(text.FontWidthMm, dpi);
        var padding = ToDots(text.PaddingMm, dpi);
        var (escaped, needsFieldHex) = EscapeFieldData(value);

        if (text.BorderMm > 0 && boxWidth > 0)
        {
            AppendBox(sb, x, y, boxWidth + 2 * padding, fontHeight + 2 * padding, ToDots(text.BorderMm, dpi));
        }

        var textX = x + padding;
        var textY = y + padding;
        sb.Append($"^FO{textX},{textY}^A{text.FontName}N,{fontHeight},{fontWidth}");
        if (boxWidth > 0)
        {
            var justify = text.TextAlign switch
            {
                LabelTextAlign.Left => 0,
                LabelTextAlign.Center => 1,
                LabelTextAlign.Right => 2,
                _ => 0,
            };
            sb.Append($"^FB{boxWidth},1,0,{justify}");
        }

        if (needsFieldHex)
        {
            sb.Append("^FH");
        }

        sb.Append($"^FD{escaped}^FS").AppendLine();
    }

    private static void AppendBarcode(
        StringBuilder sb,
        LabelBarcodeElement barcode,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        var value = LabelElementContent.Get(barcode, data);
        var bounds = LabelLayoutResolver.ResolveBounds(barcode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var height = ToDots(bounds.HeightMm, dpi);
        var width = ToDots(bounds.WidthMm, dpi);
        var module = Math.Clamp(barcode.ModuleWidth, 1, 10);
        var (escaped, needsFieldHex) = EscapeFieldData(value);

        if (barcode.BorderMm > 0)
        {
            AppendBox(sb, x, y, width, height, ToDots(barcode.BorderMm, dpi));
        }

        sb.Append($"^FO{x},{y}^BY{module},3^BCN,{height},Y,N,N");
        if (needsFieldHex)
        {
            sb.Append("^FH");
        }

        sb.Append($"^FD{escaped}^FS").AppendLine();
    }

    private static void AppendQrCode(
        StringBuilder sb,
        LabelQrCodeElement qrCode,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        var value = LabelElementContent.Get(qrCode, data);
        var bounds = LabelLayoutResolver.ResolveBounds(qrCode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var size = ToDots(bounds.WidthMm, dpi);
        var magnification = Math.Clamp((int)Math.Round(size / 24.0, MidpointRounding.AwayFromZero), 1, 10);

        if (qrCode.BorderMm > 0)
        {
            AppendBox(sb, x, y, size, size, ToDots(qrCode.BorderMm, dpi));
        }

        sb.Append($"^FO{x},{y}^BQN,2,{magnification}^FDQA,{value}^FS").AppendLine();
    }

    private static void AppendImage(
        StringBuilder sb,
        LabelImageElement image,
        LabelBitmap bitmap,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        var bounds = LabelLayoutResolver.ResolveBounds(image, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var totalBytes = bitmap.RowBytes * bitmap.Height;
        var hex = Convert.ToHexString(bitmap.Pixels);

        if (image.BorderMm > 0)
        {
            var width = ToDots(bounds.WidthMm, dpi);
            var height = ToDots(bounds.HeightMm, dpi);
            AppendBox(sb, x, y, width, height, ToDots(image.BorderMm, dpi));
        }

        sb.Append($"^FO{x},{y}^GFA,{totalBytes},{totalBytes},{bitmap.RowBytes},{hex}^FS").AppendLine();
    }

    private static void AppendLine(StringBuilder sb, LabelLineElement line, int dpi)
    {
        var x1 = ToDots(line.XMm, dpi);
        var y1 = ToDots(line.YMm, dpi);
        var width = Math.Abs(ToDots(line.X2Mm, dpi) - x1);
        var height = Math.Abs(ToDots(line.Y2Mm, dpi) - y1);
        var thickness = Math.Max(1, ToDots(line.ThicknessMm, dpi));
        sb.Append($"^FO{x1},{y1}^GB{width},{height},{thickness},L,0^FS").AppendLine();
    }

    private static void AppendRegion(StringBuilder sb, LabelRegionElement region, int dpi)
    {
        if (region.BorderMm <= 0)
        {
            return;
        }

        var x = ToDots(region.XMm, dpi);
        var y = ToDots(region.YMm, dpi);
        var width = ToDots(region.WidthMm, dpi);
        var height = ToDots(region.HeightMm, dpi);
        AppendBox(sb, x, y, width, height, ToDots(region.BorderMm, dpi));
    }

    private static void AppendBox(StringBuilder sb, int x, int y, int width, int height, int thickness)
    {
        sb.Append($"^FO{x},{y}^GB{Math.Max(1, width)},{Math.Max(1, height)},{Math.Max(1, thickness)},B,0^FS")
            .AppendLine();
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