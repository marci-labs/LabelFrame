using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using ZXing;
using ZXing.Common;

namespace LabelFrame.WinHost.Rendering;

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

    private static void DrawElement(
        Graphics graphics,
        LabelElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        int dpi)
    {
        switch (element)
        {
            case LabelTextElement text:
                DrawText(graphics, text, document, dpi);
                break;
            case LabelBarcodeElement barcode:
                DrawBarcode(graphics, barcode, document, dpi);
                break;
            case LabelQrCodeElement qrCode:
                DrawQrCode(graphics, qrCode, document, dpi);
                break;
            case LabelImageElement image:
                DrawImage(graphics, image, document, templateImages, dpi);
                break;
            case LabelLineElement line:
                DrawLine(graphics, line, dpi);
                break;
        }
    }

    private static void DrawText(Graphics graphics, LabelTextElement text, LabelDocument document, int dpi)
    {
        var value = document.Data.TryGetValue(text.SourceKey, out var v) ? v : string.Empty;
        var fontSize = Math.Max(1, ToDots(text.FontHeightMm, dpi));
        using var font = new Font("Microsoft YaHei", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString(value, font, Brushes.Black, ToDots(text.XMm, dpi), ToDots(text.YMm, dpi), StringFormat.GenericTypographic);
    }

    private static void DrawBarcode(Graphics graphics, LabelBarcodeElement barcode, LabelDocument document, int dpi)
    {
        var value = document.Data.TryGetValue(barcode.SourceKey, out var v) ? v : string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var height = Math.Max(1, ToDots(barcode.HeightMm, dpi));
        var width = Math.Max(1, (int)(height * 2.5));
        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Height = height, Width = width, Margin = 4, PureBarcode = false },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var barcodeBitmap = ToBitmap(writer.Write(value));
        graphics.DrawImage(barcodeBitmap, ToDots(barcode.XMm, dpi), ToDots(barcode.YMm, dpi));
    }

    private static void DrawQrCode(Graphics graphics, LabelQrCodeElement qrCode, LabelDocument document, int dpi)
    {
        var value = document.Data.TryGetValue(qrCode.SourceKey, out var v) ? v : string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var size = Math.Max(1, ToDots(qrCode.SizeMm, dpi));
        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 2 },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var qrBitmap = ToBitmap(writer.Write(value));
        graphics.DrawImage(qrBitmap, ToDots(qrCode.XMm, dpi), ToDots(qrCode.YMm, dpi));
    }

    private static void DrawImage(
        Graphics graphics,
        LabelImageElement image,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        int dpi)
    {
        var x = ToDots(image.XMm, dpi);
        var y = ToDots(image.YMm, dpi);
        var width = Math.Max(1, ToDots(image.WidthMm, dpi));
        var height = Math.Max(1, ToDots(image.HeightMm, dpi));

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