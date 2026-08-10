using System.Runtime.InteropServices;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace LabelFrame.Rendering;

/// <summary>整版位图渲染器（Image 打印 / 调试出图用）。</summary>
public interface ILabelBitmapRenderer
{
    /// <summary>渲染整张标签为 1bpp 位图（白底黑字）。</summary>
    LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null);

    /// <summary>渲染整张标签为 1bpp 位图并编码为 PNG 字节。</summary>
    byte[] RenderLabelBitmapPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null);
}

/// <summary>
/// SkiaSharp 后端渲染器：与前端 canvas 渲染同源（文字缩小适应 / 左中右对齐 /
/// 内边距 / 边框、线条、区域、ZXing 条码二维码、模板图片），输出 1bpp 位图。
/// 用于图片打印与调试，避免 GDI 对 CJK / 右对齐 / 长文本的兼容问题。
/// </summary>
public sealed class SkiaLabelRenderer : ILabelBitmapRenderer
{
    private const string FontFamily = "Microsoft YaHei";

    /// <inheritdoc />
    public LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var regions = LabelLayoutResolver.IndexRegions(document.Layout);
        foreach (var element in document.Layout.Elements)
        {
            DrawElement(canvas, element, document, templateImages, regions, dpi);
        }

        var rowBytes = bitmap.RowBytes;
        var bytes = new byte[rowBytes * height];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return ToLabelBitmap(width, height, rowBytes, bytes);
    }

    /// <inheritdoc />
    public byte[] RenderLabelBitmapPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var regions = LabelLayoutResolver.IndexRegions(document.Layout);
        foreach (var element in document.Layout.Elements)
        {
            DrawElement(canvas, element, document, templateImages, regions, dpi);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawElement(
        SKCanvas canvas,
        LabelElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        switch (element)
        {
            case LabelTextElement text:
                DrawText(canvas, text, document, regions, dpi);
                break;
            case LabelBarcodeElement barcode:
                DrawBarcode(canvas, barcode, document, regions, dpi);
                break;
            case LabelQrCodeElement qrCode:
                DrawQrCode(canvas, qrCode, document, regions, dpi);
                break;
            case LabelImageElement image:
                DrawImage(canvas, image, document, templateImages, regions, dpi);
                break;
            case LabelLineElement line:
                DrawLine(canvas, line, dpi);
                break;
            case LabelRegionElement region:
                DrawRegion(canvas, region, dpi);
                break;
        }
    }

    private static void DrawText(
        SKCanvas canvas,
        LabelTextElement text,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(text, document.Data, out var value);
        var bounds = LabelLayoutResolver.ResolveBounds(text, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = ToDots(bounds.WidthMm, dpi);
        var padding = ToDots(text.PaddingMm, dpi);
        using var typeface = CreateTypeface(value);

        // 字号：先按 fontHeightMm；若文本超过可用框宽则缩小适应（与前端 shrink 一致），最小 1.5mm
        var baseFontSize = Math.Max(1f, ToDots(text.FontHeightMm, dpi));
        var fontSize = baseFontSize;
        if (boxWidth > 0 && !string.IsNullOrEmpty(value))
        {
            using var measureFont = new SKFont(typeface, baseFontSize);
            var measuredWidth = measureFont.MeasureText(value);
            var innerWidth = Math.Max(1, boxWidth - 2 * padding);
            if (measuredWidth > innerWidth)
            {
                var minFont = Math.Max(1f, ToDots(1.5, dpi));
                fontSize = Math.Max(minFont, baseFontSize * innerWidth / measuredWidth);
            }
        }

        // 有框高（前端保存 heightMm）时按框高；无框高（旧模板）时按字高，避免内边距把裁剪区算成负数
        var hasBox = bounds.HeightMm > 0;
        var boxHeight = hasBox ? ToDots(bounds.HeightMm, dpi) : fontSize;
        if (text.BorderMm > 0 && boxWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(text.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxWidth + 2 * padding, y + boxHeight + 2 * padding), borderPaint);
        }

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        var textWidth = font.MeasureText(value);
        var drawWidth = (float)(boxWidth > 0 ? boxWidth : textWidth);
        var innerX = (float)(x + padding);
        var innerY = (float)(y + padding);
        var fm = font.Metrics;
        var lineHeight = fm.Descent - fm.Ascent; // Ascent 为负，行高 = Descent - Ascent
        var innerTop = innerY;
        // 裁剪高度至少一行，避免内边距过大/无框高时裁剪区塌缩导致文字消失
        var innerH = (int)Math.Max(lineHeight, hasBox ? boxHeight - 2 * padding : 0);
        // 与前端一致：文本在元素框内按 valign 垂直对齐（Top / Middle / Bottom）
        var baseline = text.VerticalAlign switch
        {
            LabelVerticalAlign.Middle => innerTop + (innerH - lineHeight) / 2 - fm.Ascent,
            LabelVerticalAlign.Bottom => innerTop + innerH - fm.Descent,
            _ => innerTop - fm.Ascent,
        };
        var textX = innerX;
        switch (text.TextAlign)
        {
            case LabelTextAlign.Center:
                textX += (drawWidth - textWidth) / 2;
                break;
            case LabelTextAlign.Right:
                textX += drawWidth - textWidth;
                break;
        }

        // 裁剪到文本框，避免溢出到其他区域（与前端 overflow 隐藏一致）
        canvas.Save();
        canvas.ClipRect(new SKRect(innerX, innerY, innerX + Math.Max(1, drawWidth), innerY + Math.Max(1, innerH)));
        canvas.DrawText(value, textX, baseline, SKTextAlign.Left, font, paint);
        canvas.Restore();
    }

    private static void DrawBarcode(
        SKCanvas canvas,
        LabelBarcodeElement barcode,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(barcode, document.Data, out var value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bounds = LabelLayoutResolver.ResolveBounds(barcode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var height = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        var width = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        if (barcode.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(barcode.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + width, y + height), borderPaint);
        }

        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Height = height, Width = width, Margin = 4, PureBarcode = false },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var barcodeBitmap = ToSkBitmap(writer.Write(value));
        canvas.DrawBitmap(barcodeBitmap, x, y);
    }

    private static void DrawQrCode(
        SKCanvas canvas,
        LabelQrCodeElement qrCode,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(qrCode, document.Data, out var value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bounds = LabelLayoutResolver.ResolveBounds(qrCode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var size = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        if (qrCode.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(qrCode.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + size, y + size), borderPaint);
        }

        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 2 },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var qrBitmap = ToSkBitmap(writer.Write(value));
        canvas.DrawBitmap(qrBitmap, x, y);
    }

    private static void DrawImage(
        SKCanvas canvas,
        LabelImageElement image,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        var bounds = LabelLayoutResolver.ResolveBounds(image, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var width = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var height = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        if (image.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(image.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + width, y + height), borderPaint);
        }

        if (templateImages is not null && templateImages.TryGetValue(image.SourceKey, out var bytes))
        {
            using var source = SKBitmap.Decode(bytes);
            if (source is not null)
            {
                canvas.DrawBitmap(source, new SKRect(x, y, x + width, y + height));
                return;
            }
        }

        if (document.Images.TryGetValue(image.SourceKey, out var labelBitmap))
        {
            using var source = ToSkBitmap(labelBitmap);
            canvas.DrawBitmap(source, new SKRect(x, y, x + width, y + height));
            return;
        }

        // 占位框
        using var pen = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = SKColors.Gray };
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), pen);
        using var textFont = new SKFont(SKTypeface.Default, 8);
        using var textPaint = new SKPaint { Color = SKColors.Gray };
        canvas.DrawText(image.SourceKey, x + 2, y + 10, SKTextAlign.Left, textFont, textPaint);
    }

    private static void DrawLine(SKCanvas canvas, LabelLineElement line, int dpi)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, ToDots(line.ThicknessMm, dpi)),
            StrokeCap = SKStrokeCap.Butt,
            IsAntialias = false,
            Color = SKColors.Black,
        };
        canvas.DrawLine(
            ToDots(line.XMm, dpi),
            ToDots(line.YMm, dpi),
            ToDots(line.X2Mm, dpi),
            ToDots(line.Y2Mm, dpi),
            paint);
    }

    private static void DrawRegion(SKCanvas canvas, LabelRegionElement region, int dpi)
    {
        if (region.BorderMm <= 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, ToDots(region.BorderMm, dpi)),
            IsAntialias = false,
            Color = SKColors.Black,
        };
        canvas.DrawRect(
            new SKRect(
                ToDots(region.XMm, dpi),
                ToDots(region.YMm, dpi),
                ToDots(region.XMm + region.WidthMm, dpi),
                ToDots(region.YMm + region.HeightMm, dpi)),
            paint);
    }

    /// <summary>ZXing PixelData（BGRA 字节）→ SKBitmap。</summary>
    private static SKBitmap ToSkBitmap(ZXing.Rendering.PixelData pixelData)
    {
        var bitmap = new SKBitmap(new SKImageInfo(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        return bitmap;
    }

    /// <summary>LabelBitmap（1bpp）→ SKBitmap（白底黑字）。</summary>
    private static SKBitmap ToSkBitmap(LabelBitmap labelBitmap)
    {
        var bitmap = new SKBitmap(new SKImageInfo(labelBitmap.Width, labelBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var bytes = new byte[labelBitmap.Width * labelBitmap.Height * 4];
        for (var y = 0; y < labelBitmap.Height; y++)
        {
            for (var x = 0; x < labelBitmap.Width; x++)
            {
                var bit = (labelBitmap.Pixels[y * labelBitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                var offset = (y * labelBitmap.Width + x) * 4;
                bytes[offset] = bit ? (byte)0 : byte.MaxValue;     // B
                bytes[offset + 1] = bit ? (byte)0 : byte.MaxValue; // G
                bytes[offset + 2] = bit ? (byte)0 : byte.MaxValue; // R
                bytes[offset + 3] = byte.MaxValue;                 // A
            }
        }

        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    /// <summary>BGRA 像素 → 1bpp LabelBitmap（白底黑字，阈值 128）。</summary>
    private static LabelBitmap ToLabelBitmap(int width, int height, int rowBytes, byte[] bytes)
    {
        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * rowBytes + x * 4;
                var b = bytes[offset];
                var g = bytes[offset + 1];
                var r = bytes[offset + 2];
                var luma = (r * 299 + g * 587 + b * 114) / 1000;
                if (luma < 128)
                {
                    result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 创建文本字型：优先指定字体族；文本含非 ASCII（中文等）时用系统字体回退
    /// 匹配第一个非 ASCII 字符，避免指定字体缺字型导致整段文本不绘制。
    /// </summary>
    private static SKTypeface CreateTypeface(string value)
    {
        var preferred = SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
        if (string.IsNullOrEmpty(value))
        {
            return preferred;
        }

        if (value.Any(c => c > 0x7F))
        {
            // 含中文等非 ASCII：用常见 CJK 字符匹配系统回退字体。
            // 不能用文本首字符匹配——首字符若是生僻字，会匹配到只含该字的小字体，
            // 其余字符无字型导致整段文本只剩一两个字。
            return SKFontManager.Default.MatchCharacter('中') ?? preferred;
        }

        return preferred;
    }

    /// <summary>毫米 → 点（DPI），四舍五入远离零。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}
