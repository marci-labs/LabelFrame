using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
// Zebra SDK 引入 Microsoft.Maui.Graphics，以下别名固定使用 System.Drawing 类型
using Bitmap = System.Drawing.Bitmap;
using Brushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using Graphics = System.Drawing.Graphics;
using GraphicsUnit = System.Drawing.GraphicsUnit;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using PointF = System.Drawing.PointF;
using SmoothingMode = System.Drawing.Drawing2D.SmoothingMode;
using StringFormat = System.Drawing.StringFormat;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;

namespace LabelFrame.WinHost.Rendering;

/// <summary>
/// GDI 文本栅格化（Windows 专属）：把非 ASCII 文本元素按 DPI 渲染为 1bpp 位图，
/// 由 ZPL 编码器输出 ^GF；字体优先加载指定字体文件（内嵌 / 本地），否则使用系统字体。
/// </summary>
public sealed class GdiTextRasterizer : ITextRasterizer
{
    private readonly string _fontFamily;
    private readonly string? _fontFilePath;

    /// <summary>创建栅格化器。</summary>
    /// <param name="fontFamily">系统字体族名。</param>
    /// <param name="fontFilePath">可选字体文件（TTF / TTC），存在时优先使用。</param>
    public GdiTextRasterizer(string? fontFamily = null, string? fontFilePath = null)
    {
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei" : fontFamily;
        _fontFilePath = string.IsNullOrWhiteSpace(fontFilePath) ? null : fontFilePath;
    }

    /// <inheritdoc />
    public LabelDocument Rasterize(LabelDocument document, int dpi)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layout = document.Layout;
        var needsRasterize = layout.Elements.Any(e => e is LabelTextElement t && ContainsNonAscii(GetValue(document, t)));
        if (!needsRasterize)
        {
            return document;
        }

        var images = new Dictionary<string, LabelBitmap>(document.Images);
        var elements = new List<LabelElement>(layout.Elements.Count);
        var imageIndex = 0;

        foreach (var element in layout.Elements)
        {
            if (element is not LabelTextElement text || !ContainsNonAscii(GetValue(document, text)))
            {
                elements.Add(element);
                continue;
            }

            var value = GetValue(document, text);
            var bitmap = Render(value, text.FontHeightMm, dpi);
            var key = $"img-{text.SourceKey}-{imageIndex++}";
            images[key] = bitmap;
            elements.Add(new LabelImageElement
            {
                SourceKey = key,
                XMm = text.XMm,
                YMm = text.YMm,
                WidthMm = bitmap.Width * 25.4 / dpi,
                HeightMm = bitmap.Height * 25.4 / dpi,
            });
        }

        return new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = layout.Name,
                ContractName = layout.ContractName,
                ContractVersion = layout.ContractVersion,
                WidthMm = layout.WidthMm,
                HeightMm = layout.HeightMm,
                Elements = elements,
            },
            Data = document.Data,
            Images = images,
        };
    }

    /// <summary>把文本渲染为 1bpp 位图（白底黑字）。</summary>
    public LabelBitmap Render(string text, double fontHeightMm, int dpi)
    {
        ArgumentNullException.ThrowIfNull(text);

        var fontPixels = Math.Max(1, (int)Math.Round(fontHeightMm / 25.4 * dpi, MidpointRounding.AwayFromZero));
        using var font = CreateFont(fontPixels);

        using var measureBitmap = new Bitmap(1, 1);
        using var measureGraphics = Graphics.FromImage(measureBitmap);
        measureGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var size = measureGraphics.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic);

        var width = Math.Max(1, (int)Math.Ceiling(size.Width));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(text, font, Brushes.Black, 0, 0, StringFormat.GenericTypographic);
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

    private System.Drawing.Font CreateFont(int pixels)
    {
        if (_fontFilePath is not null && File.Exists(_fontFilePath))
        {
            using var collection = new PrivateFontCollection();
            collection.AddFontFile(_fontFilePath);
            return new System.Drawing.Font(collection.Families[0], pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        return new System.Drawing.Font(_fontFamily, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static string GetValue(LabelDocument document, LabelTextElement text)
        => document.Data.TryGetValue(text.SourceKey, out var value) ? value : string.Empty;

    private static bool ContainsNonAscii(string value)
        => value.Any(c => c > 0x7F);
}