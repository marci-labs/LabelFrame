using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using ZXing;
using ZXing.Common;

namespace LabelFrame.Rendering;

/// <summary>
/// 设计期预览渲染：LabelDocument → PNG（PC）。
/// 文本 / 线用 GDI；条码 / 二维码用 ZXing；图片用模板资源或位图数据。
/// </summary>
public sealed class LabelPreviewRenderer
{
    /// <summary>渲染为 PNG。</summary>
    /// <param name="document">标签文档。</param>
    /// <param name="dpi">分辨率（默认 203）。</param>
    /// <param name="templateImages">模板图片资源（键 → PNG/JPEG 字节）。</param>
    public byte[] RenderPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var width = ToDots(document.Layout.WidthMm, dpi);
        var height = ToDots(document.Layout.HeightMm, dpi);
        using var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var element in document.Layout.Elements)
            {
                DrawElement(graphics, element, document, templateImages, dpi);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>渲染整张标签为 1bpp 位图（白底黑字，与预览同源；图片打印模式用）。</summary>
    public LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var element in document.Layout.Elements)
            {
                DrawElement(graphics, element, document, templateImages, dpi);
            }
        }

        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var luma = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                if (luma < 128)
                {
                    result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return result;
    }

    private static void DrawElement(
        Graphics graphics,
        LabelElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        int dpi)
    {
        var regions = LabelFrame.Core.Layout.LabelLayoutResolver.IndexRegions(document.Layout);
        switch (element)
        {
            case LabelTextElement text:
                DrawText(graphics, text, document, regions, dpi);
                break;
            case LabelBarcodeElement barcode:
                DrawBarcode(graphics, barcode, document, regions, dpi);
                break;
            case LabelQrCodeElement qrCode:
                DrawQrCode(graphics, qrCode, document, regions, dpi);
                break;
            case LabelImageElement image:
                DrawImage(graphics, image, document, templateImages, regions, dpi);
                break;
            case LabelLineElement line:
                DrawLine(graphics, line, dpi);
                break;
            case LabelFrame.Core.Layout.LabelRegionElement region:
                DrawRegion(graphics, region, dpi);
                break;
        }
    }

    private static void DrawText(
        Graphics graphics,
        LabelTextElement text,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelFrame.Core.Layout.LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(text, document.Data, out var value);
        var bounds = LabelFrame.Core.Layout.LabelLayoutResolver.ResolveBounds(text, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = ToDots(bounds.WidthMm, dpi);
        var fontSize = Math.Max(1, ToDots(text.FontHeightMm, dpi));
        var padding = ToDots(text.PaddingMm, dpi);
        using var font = new Font("Microsoft YaHei", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);

        if (text.BorderMm > 0 && boxWidth > 0)
        {
            using var borderPen = new Pen(Color.Black, Math.Max(1, ToDots(text.BorderMm, dpi)));
            graphics.DrawRectangle(borderPen, x, y, boxWidth + 2 * padding, fontSize + 2 * padding);
        }

        var format = new StringFormat(StringFormat.GenericTypographic);
        format.Alignment = text.TextAlign switch
        {
            LabelFrame.Core.Layout.LabelTextAlign.Center => StringAlignment.Center,
            LabelFrame.Core.Layout.LabelTextAlign.Right => StringAlignment.Far,
            _ => StringAlignment.Near,
        };
        format.LineAlignment = StringAlignment.Near;
        // 未显式指定块宽时按文本实际宽度绘制，避免 1px 矩形把文字裁掉（与 ZPL 无 ^FB 行为一致）
        var drawWidth = boxWidth > 0 ? boxWidth : MeasureTextWidth(graphics, value, font);
        var rect = new RectangleF(x + padding, y + padding, Math.Max(1, drawWidth), Math.Max(1, fontSize));
        graphics.DrawString(value, font, Brushes.Black, rect, format);
        format.Dispose();
    }

    private static void DrawBarcode(
        Graphics graphics,
        LabelBarcodeElement barcode,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelFrame.Core.Layout.LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(barcode, document.Data, out var value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bounds = LabelFrame.Core.Layout.LabelLayoutResolver.ResolveBounds(barcode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var height = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        var width = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        if (barcode.BorderMm > 0)
        {
            using var borderPen = new Pen(Color.Black, Math.Max(1, ToDots(barcode.BorderMm, dpi)));
            graphics.DrawRectangle(borderPen, x, y, width, height);
        }
        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Height = height, Width = width, Margin = 4, PureBarcode = false },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var barcodeBitmap = ToBitmap(writer.Write(value));
        graphics.DrawImage(barcodeBitmap, x, y);
    }

    private static void DrawQrCode(
        Graphics graphics,
        LabelQrCodeElement qrCode,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelFrame.Core.Layout.LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(qrCode, document.Data, out var value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bounds = LabelFrame.Core.Layout.LabelLayoutResolver.ResolveBounds(qrCode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var size = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        if (qrCode.BorderMm > 0)
        {
            using var borderPen = new Pen(Color.Black, Math.Max(1, ToDots(qrCode.BorderMm, dpi)));
            graphics.DrawRectangle(borderPen, x, y, size, size);
        }
        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 2 },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var qrBitmap = ToBitmap(writer.Write(value));
        graphics.DrawImage(qrBitmap, x, y);
    }

    private static void DrawImage(
        Graphics graphics,
        LabelImageElement image,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        IReadOnlyDictionary<string, LabelFrame.Core.Layout.LabelRegionElement> regions,
        int dpi)
    {
        var bounds = LabelFrame.Core.Layout.LabelLayoutResolver.ResolveBounds(image, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var width = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var height = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        if (image.BorderMm > 0)
        {
            using var borderPen = new Pen(Color.Black, Math.Max(1, ToDots(image.BorderMm, dpi)));
            graphics.DrawRectangle(borderPen, x, y, width, height);
        }

        if (templateImages is not null && templateImages.TryGetValue(image.SourceKey, out var bytes))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = System.Drawing.Image.FromStream(stream);
            graphics.DrawImage(source, x, y, width, height);
            return;
        }

        if (document.Images.TryGetValue(image.SourceKey, out var bitmap))
        {
            using var source = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format1bppIndexed);
            var data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(bitmap.Pixels, 0, data.Scan0, bitmap.Pixels.Length);
            }
            finally
            {
                source.UnlockBits(data);
            }

            graphics.DrawImage(source, x, y, width, height);
            return;
        }

        // 占位框
        using var pen = new Pen(Color.Gray, 1);
        graphics.DrawRectangle(pen, x, y, width, height);
        graphics.DrawString(image.SourceKey, new Font("Microsoft YaHei", 8), Brushes.Gray, x + 2, y + 2);
    }

    private static void DrawRegion(Graphics graphics, LabelFrame.Core.Layout.LabelRegionElement region, int dpi)
    {
        if (region.BorderMm <= 0)
        {
            return;
        }

        var x = ToDots(region.XMm, dpi);
        var y = ToDots(region.YMm, dpi);
        var width = Math.Max(1, ToDots(region.WidthMm, dpi));
        var height = Math.Max(1, ToDots(region.HeightMm, dpi));
        using var pen = new Pen(Color.Black, Math.Max(1, ToDots(region.BorderMm, dpi)));
        graphics.DrawRectangle(pen, x, y, width, height);
    }

    private static void DrawLine(Graphics graphics, LabelLineElement line, int dpi)
    {
        using var pen = new Pen(Color.Black, Math.Max(1, ToDots(line.ThicknessMm, dpi)));
        graphics.DrawLine(
            pen,
            ToDots(line.XMm, dpi),
            ToDots(line.YMm, dpi),
            ToDots(line.X2Mm, dpi),
            ToDots(line.Y2Mm, dpi));
    }


    private static float MeasureTextWidth(Graphics graphics, string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        return graphics.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic).Width;
    }
    private static Bitmap ToBitmap(ZXing.Rendering.PixelData pixelData)
    {
        var bitmap = new Bitmap(pixelData.Width, pixelData.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, pixelData.Width, pixelData.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, data.Scan0, pixelData.Pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}