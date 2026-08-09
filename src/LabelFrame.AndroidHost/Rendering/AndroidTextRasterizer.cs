using Android.Graphics;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;

namespace LabelFrame.AndroidHost.Rendering;

/// <summary>
/// Android 文本栅格化：把非 ASCII 文本元素用 Android.Graphics 渲染为 1bpp 位图，
/// 由 ZPL 编码器输出 ^GF；ASCII 保持原生文本。与 WinHost GDI 实现同契约。
/// </summary>
public sealed class AndroidTextRasterizer
{
    /// <summary>把文档中的非 ASCII 文本元素替换为图片元素。</summary>
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

            var bitmap = Render(GetValue(document, text), text.FontHeightMm, dpi);
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
        using var paint = new Paint
        {
            TextSize = fontPixels,
            AntiAlias = true,
            Color = Color.Black,
        };

        var width = Math.Max(1, (int)Math.Ceiling(paint.MeasureText(text)));
        var height = Math.Max(1, (int)Math.Ceiling(-paint.Ascent() + paint.Descent()));
        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        using (var canvas = new Canvas(bitmap))
        {
            canvas.DrawColor(Color.White);
            canvas.DrawText(text, 0, -paint.Ascent(), paint);
        }

        var pixels = new int[width * height];
        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var argb = pixels[y * width + x];
                var red = (argb >> 16) & 0xFF;
                var green = (argb >> 8) & 0xFF;
                var blue = argb & 0xFF;
                var luma = (red * 299 + green * 587 + blue * 114) / 1000;
                if (luma < 128)
                {
                    result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return result;
    }

    private static string GetValue(LabelDocument document, LabelTextElement text)
        => document.Data.TryGetValue(text.SourceKey, out var value) ? value : string.Empty;

    private static bool ContainsNonAscii(string value) => value.Any(c => c > 0x7F);
}