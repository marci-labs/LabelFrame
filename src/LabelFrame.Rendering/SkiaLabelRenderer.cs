using System.Runtime.InteropServices;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

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
/// SkiaSharp 后端渲染器：与前端 canvas 渲染同源（自动换行 / 行距 / 溢出处理 / 字体族 /
/// 左中右对齐 / 双边内边距 / 边框、线条、区域、ZXing 条码二维码参数、模板图片），输出 1bpp 位图。
/// 用于图片打印与调试，避免 GDI 对 CJK / 右对齐 / 长文本的兼容问题。
/// 新排版字段（wrap/lineHeight/fitMode/fontFamily/qrEcc/qrMargin/displayValue/paddingH/V）
/// 只影响本渲染器，不参与 ZPL 矢量编码（与契约 §4 一致）。
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
        var padH = ToDots(text.EffectivePaddingHMm, dpi);
        var padV = ToDots(text.EffectivePaddingVMm, dpi);
        // 决策 A：无 heightMm 时框高兜底 = max(字高 + 2×最大双边内边距, 10mm)（与前端读回兜底一致）
        var boxHeightMm = text.HeightMm > 0
            ? text.HeightMm
            : Math.Max(text.FontHeightMm + 2 * Math.Max(text.EffectivePaddingHMm, text.EffectivePaddingVMm), 10);
        var boxHeight = ToDots(boxHeightMm, dpi);
        using var typeface = CreateTypeface(text.FontFamily, value);

        // 边框 = 元素框；内边距在框内（与前端 ElementNode 一致）
        if (text.BorderMm > 0 && boxWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(text.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxWidth, y + boxHeight), borderPaint);
        }

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var innerX = (float)(x + padH);
        var innerY = (float)(y + padV);
        // 内容区 = 元素框减去 paddingH / paddingV（0 = 宽度不限）
        var innerW = boxWidth > 0 ? Math.Max(1, boxWidth - 2 * padH) : 0;
        var innerH = Math.Max(1, boxHeight - 2 * padV);
        var baseFontSize = Math.Max(1f, ToDots(text.FontHeightMm, dpi));
        var lineHeightFactor = (float)(text.LineHeight > 0 ? text.LineHeight : 1.2);
        var minFontSize = Math.Max(1f, ToDots(1.5, dpi));

        var fontSize = baseFontSize;
        List<string> lines;
        if (text.Wrap)
        {
            // wrap=true：按框宽自动换行；若整体超高则整体缩小至能放下（最小 1.5mm），避免打印丢字
            lines = WrapLines(value, typeface, fontSize, innerW, text.Bold);
            var totalHeight = lines.Count * fontSize * lineHeightFactor;
            var guard = 0;
            while (totalHeight > innerH && fontSize > minFontSize && guard++ < 10)
            {
                fontSize = Math.Max(minFontSize, fontSize * innerH / totalHeight);
                lines = WrapLines(value, typeface, fontSize, innerW, text.Bold);
                totalHeight = lines.Count * fontSize * lineHeightFactor;
            }
        }
        else if (text.FitMode == LabelFitMode.Overflow)
        {
            // wrap=false + overflow：单行、不缩小、按框裁剪（隐藏溢出）
            lines = [value];
        }
        else
        {
            // wrap=false + shrink（默认）：单行，超出框宽 / 框高按比例缩小至最小 1.5mm
            lines = [value];
            using var measureFont = new SKFont(typeface, fontSize) { Embolden = text.Bold };
            var measuredWidth = measureFont.MeasureText(value);
            var widthFactor = innerW > 0 && measuredWidth > innerW ? innerW / measuredWidth : 1f;
            var fitLineHeightPx = fontSize * lineHeightFactor;
            var heightFactor = fitLineHeightPx > innerH ? innerH / fitLineHeightPx : 1f;
            var factor = Math.Min(widthFactor, heightFactor);
            if (factor < 1f)
            {
                fontSize = Math.Max(minFontSize, fontSize * factor);
            }
        }

        using var font = new SKFont(typeface, fontSize) { Embolden = text.Bold };
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        var fm = font.Metrics;
        var lineHeightPx = fontSize * lineHeightFactor;
        var totalHeightPx = lines.Count * lineHeightPx;
        // 与前端一致：文本在元素框内按 verticalAlign 垂直对齐（Top / Middle / Bottom）
        var startY = innerY;
        switch (text.VerticalAlign)
        {
            case LabelVerticalAlign.Middle:
                startY += (innerH - totalHeightPx) / 2;
                break;
            case LabelVerticalAlign.Bottom:
                startY += innerH - totalHeightPx;
                break;
        }

        startY = Math.Max(innerY, startY);
        canvas.Save();
        var clipRight = innerW > 0 ? innerX + innerW : canvas.DeviceClipBounds.Right;
        canvas.ClipRect(new SKRect(innerX, innerY, clipRight, innerY + innerH));
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var lineWidth = font.MeasureText(line);
            var lineX = innerX;
            if (innerW > 0)
            {
                lineX = text.TextAlign switch
                {
                    LabelTextAlign.Center => innerX + (innerW - lineWidth) / 2,
                    LabelTextAlign.Right => innerX + innerW - lineWidth,
                    _ => innerX,
                };
            }

            var lineTop = startY + i * lineHeightPx;
            var baseline = lineTop - fm.Ascent;
            canvas.DrawText(line, lineX, baseline, SKTextAlign.Left, font, paint);
        }

        canvas.Restore();
    }

    /// <summary>按框宽换行：英文按空格断词，超宽单词 / 中文按字拆行。</summary>
    private static List<string> WrapLines(string value, SKTypeface typeface, float fontSize, float maxWidth, bool embolden)
    {
        using var font = new SKFont(typeface, fontSize) { Embolden = embolden };
        var lines = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return lines;
        }

        if (maxWidth <= 0)
        {
            lines.Add(value);
            return lines;
        }

        foreach (var rawLine in value.Split('\n'))
        {
            var current = new StringBuilder();
            var words = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rawLine.Length == 0 || words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            foreach (var word in words)
            {
                if (font.MeasureText(word) > maxWidth)
                {
                    // 超宽单词 / 无空格中文：按字拆行
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }

                    foreach (var ch in word)
                    {
                        var trial = current.Length == 0 ? ch.ToString() : current.ToString() + ch;
                        if (current.Length > 0 && font.MeasureText(trial) > maxWidth)
                        {
                            lines.Add(current.ToString());
                            current.Clear();
                        }

                        current.Append(ch);
                    }

                    continue;
                }

                var candidate = current.Length == 0 ? word : current.ToString() + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate) > maxWidth)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(word);
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
            }
        }

        return lines;
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
        var boxWidth = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var boxHeight = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        var padH = ToDots(barcode.EffectivePaddingHMm, dpi);
        var padV = ToDots(barcode.EffectivePaddingVMm, dpi);
        // 内容区 = 元素框减去 paddingH / paddingV
        var contentX = x + padH;
        var contentY = y + padV;
        var contentW = Math.Max(1, boxWidth - 2 * padH);
        var contentH = Math.Max(1, boxHeight - 2 * padV);

        if (barcode.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(barcode.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxWidth, y + boxHeight), borderPaint);
        }

        // displayValue=true：底部绘制数值文字（字号取框高比例，最小 1.5mm），条码占剩余高度；
        // displayValue=false：仅条码（PureBarcode，不绘制文字）
        var textSize = 0f;
        var textBand = 0f;
        if (barcode.DisplayValue)
        {
            textSize = Math.Max(ToDots(1.5, dpi), contentH * 0.15f);
            using var measureFont = new SKFont(CreateTypeface(LabelTextElement.DefaultFontFamily, value), textSize);
            var measured = measureFont.MeasureText(value);
            if (measured > contentW)
            {
                textSize = Math.Max(ToDots(1.5, dpi), textSize * contentW / measured);
            }

            textBand = textSize * 1.2f;
            if (textBand >= contentH)
            {
                textBand = Math.Max(0f, contentH * 0.15f);
            }
        }

        var barcodeHeight = Math.Max(1, (int)(contentH - textBand));
        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Height = barcodeHeight, Width = contentW, Margin = 2, PureBarcode = true },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var barcodeBitmap = ToSkBitmap(writer.Write(value));
        canvas.DrawBitmap(barcodeBitmap, contentX, contentY);

        if (barcode.DisplayValue)
        {
            using var typeface = CreateTypeface(LabelTextElement.DefaultFontFamily, value);
            using var textFont = new SKFont(typeface, textSize);
            using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
            var textWidth = textFont.MeasureText(value);
            var textX = contentX + Math.Max(0, (contentW - textWidth) / 2);
            var textY = contentY + contentH - textFont.Metrics.Descent;
            canvas.Save();
            canvas.ClipRect(new SKRect(contentX, contentY, contentX + contentW, contentY + contentH));
            canvas.DrawText(value, textX, textY, SKTextAlign.Left, textFont, textPaint);
            canvas.Restore();
        }
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
        var boxSize = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var padH = ToDots(qrCode.EffectivePaddingHMm, dpi);
        var padV = ToDots(qrCode.EffectivePaddingVMm, dpi);
        // 内容区 = 元素框减去 paddingH / paddingV；二维码保持正方形并居中
        var contentW = Math.Max(1, boxSize - 2 * padH);
        var contentH = Math.Max(1, boxSize - 2 * padV);
        var contentSize = Math.Min(contentW, contentH);
        var contentX = x + padH + (contentW - contentSize) / 2;
        var contentY = y + padV + (contentH - contentSize) / 2;

        if (qrCode.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(qrCode.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxSize, y + boxSize), borderPaint);
        }

        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = contentSize,
                Height = contentSize,
                Margin = qrCode.QrMargin,
                ErrorCorrection = qrCode.QrEcc switch
                {
                    LabelQrEcc.L => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                    LabelQrEcc.Q => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                    LabelQrEcc.H => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                    _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                },
            },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var qrBitmap = ToSkBitmap(writer.Write(value));
        canvas.DrawBitmap(qrBitmap, contentX, contentY);
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
    /// 创建文本字型：优先指定字体族（fontFamily，缺省回退微软雅黑）；文本含非 ASCII（中文等）时用系统字体回退
    /// 匹配常见中文字符，避免指定字体缺字型导致整段文本不绘制。
    /// </summary>
    private static SKTypeface CreateTypeface(string fontFamily, string value)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? LabelTextElement.DefaultFontFamily : fontFamily;
        var preferred = SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
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
