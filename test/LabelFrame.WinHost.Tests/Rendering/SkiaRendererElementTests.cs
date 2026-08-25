using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

/// <summary>
/// 渲染器元素覆盖补全：图片 / 线 / 区域三种元素此前零测试（文本 / 条码 / 二维码已有）。
/// 断言策略与 SkiaLabelRendererTests 一致：按毫米区域数墨点（不依赖具体字体渲染）。
/// </summary>
public class SkiaRendererElementTests
{
    private const int Dpi = 203;

    private static int CountBlack(LabelBitmap bitmap, double xMm, double yMm, double wMm, double hMm)
    {
        var black = 0;
        var x0 = (int)Math.Round(xMm / 25.4 * Dpi);
        var x1 = (int)Math.Round((xMm + wMm) / 25.4 * Dpi);
        var y0 = (int)Math.Round(yMm / 25.4 * Dpi);
        var y1 = (int)Math.Round((yMm + hMm) / 25.4 * Dpi);
        for (var y = y0; y < y1 && y < bitmap.Height; y++)
        {
            for (var x = x0; x < x1 && x < bitmap.Width; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                {
                    black++;
                }
            }
        }

        return black;
    }

    private static LabelBitmap Render(LabelLayout layout, IReadOnlyDictionary<string, byte[]>? images = null)
        => new SkiaLabelRenderer().RenderLabelBitmap(new LabelDocument { Layout = layout, Data = new Dictionary<string, string>() }, Dpi, images);

    /// <summary>生成纯黑 8x8 PNG（图片元素的模板资源）。</summary>
    private static byte[] BlackSquarePng()
    {
        using var bmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(8, 8, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque));
        bmp.Erase(SkiaSharp.SKColors.Black);
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    [Fact]
    public void Image_element_should_render_template_image_pixels()
    {
        var bitmap = Render(new LabelLayout
        {
            Name = "img", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "logo", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10 }],
        }, new Dictionary<string, byte[]> { ["logo"] = BlackSquarePng() });

        // 图片区域应有大量墨点（8x8 黑块放大到 10mm×10mm ≈ 80×80 点）
        Assert.True(CountBlack(bitmap, 2.5, 2.5, 9, 9) > 3000, "图片区域应有大面积墨迹");
        // 图片外区域应空白
        Assert.Equal(0, CountBlack(bitmap, 20, 2, 15, 15));
    }

    [Fact]
    public void Image_element_without_resource_should_render_nothing_but_optional_border()
    {
        var noBorder = Render(new LabelLayout
        {
            Name = "img-missing", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "missing", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10 }],
        });
        Assert.Equal(0, CountBlack(noBorder, 2, 2, 10, 10));

        // 带边框时仍画出边框矩形（缺图不缺框）
        var bordered = Render(new LabelLayout
        {
            Name = "img-bordered", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "missing", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10, BorderMm = 0.5 }],
        });
        Assert.True(CountBlack(bordered, 2, 2, 10, 10) > 50, "边框应画出矩形墨迹");
    }

    [Fact]
    public void Line_element_should_render_horizontal_and_vertical_strokes()
    {
        var bitmap = Render(new LabelLayout
        {
            Name = "line", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements =
            [
                new LabelLineElement { XMm = 2, YMm = 10, X2Mm = 38, Y2Mm = 10 },          // 横线
                new LabelLineElement { XMm = 20, YMm = 2, X2Mm = 20, Y2Mm = 18 },          // 竖线
            ],
        });

        // 横线带（y=10mm ±1mm）与竖线带（x=20mm ±1mm）均有墨迹，交叉区外空白
        Assert.True(CountBlack(bitmap, 5, 9.5, 10, 1) > 20, "横线应有墨迹");
        Assert.True(CountBlack(bitmap, 19.5, 4, 1, 10) > 20, "竖线应有墨迹");
        Assert.Equal(0, CountBlack(bitmap, 25, 3, 10, 5));
    }

    [Fact]
    public void Region_element_should_render_border_rectangle_only()
    {
        var bitmap = Render(new LabelLayout
        {
            Name = "region", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements =
            [
                new LabelRegionElement { Id = "r1", XMm = 2, YMm = 2, WidthMm = 30, HeightMm = 15, BorderMm = 0.4 },
                // 元素锚定区域（Start/Center）应正常渲染在区域内
                new LabelTextElement { Literal = "区域文本", XMm = 0, YMm = 0, FontHeightMm = 4, RegionId = "r1", RegionHAlign = LabelRegionAlign.Start, RegionVAlign = LabelRegionAlign.Start },
            ],
        });

        // 区域边框：四条边有墨迹
        Assert.True(CountBlack(bitmap, 2, 2, 30, 0.6) > 10, "区域上边框");
        Assert.True(CountBlack(bitmap, 2, 16.4, 30, 0.6) > 10, "区域下边框");
        Assert.True(CountBlack(bitmap, 2, 2, 0.6, 15) > 10, "区域左边框");
        Assert.True(CountBlack(bitmap, 31.4, 2, 0.6, 15) > 10, "区域右边框");
        // 区域中心空白（仅有边框与锚定文本）
        Assert.Equal(0, CountBlack(bitmap, 12, 10, 8, 5));
        // 锚定文本渲染在区域左上角
        Assert.True(CountBlack(bitmap, 2.5, 2.5, 15, 6) > 30, "区域锚定文本应有墨迹");
    }

    [Fact]
    public void Region_anchored_element_should_align_when_configured()
    {
        // 对比式断言：同一文本分别以 Start（左上）与 Center（居中）锚定，居中版墨迹重心应更靠区域中心。
        // 已知限制（测试发现，记 DESIGN 风险）：文本无显式 WidthMm 时块宽被扩为区域全宽，
        // RegionHAlign 对自动宽度文本不生效（水平锚定需显式宽度参与计算）；垂直锚定按字高正常工作。
        LabelBitmap RenderWith(LabelRegionAlign h, LabelRegionAlign v, double widthMm) => Render(new LabelLayout
        {
            Name = "anchor-" + h + "-" + v, ContractName = "c", ContractVersion = "1.0", WidthMm = 60, HeightMm = 30,
            Elements =
            [
                new LabelRegionElement { Id = "r1", XMm = 2, YMm = 2, WidthMm = 56, HeightMm = 26 },
                new LabelTextElement { Literal = "锚定对比文本", XMm = 0, YMm = 0, FontHeightMm = 4, WidthMm = widthMm, RegionId = "r1", RegionHAlign = h, RegionVAlign = v },
            ],
        });

        // 垂直锚定（无显式宽度，高度取字高）：Center 的墨迹应明显靠下
        var (_, startY) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Start, 0));
        var (_, centerY) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Center, 0));
        Assert.True(centerY > startY + 2, $"垂直锚定未生效：start y={startY:F1}, center y={centerY:F1}");

        // 水平锚定（显式宽度，块位置由对齐系数计算）：Center 的墨迹应明显靠右
        var (startX, _) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Start, 12));
        var (centerX, _) = InkCenter(RenderWith(LabelRegionAlign.Center, LabelRegionAlign.Center, 12));
        Assert.True(centerX > startX + 5, $"水平锚定未生效：start x={startX:F1}, center x={centerX:F1}");
    }

    /// <summary>墨迹重心（毫米）。</summary>
    private static (double X, double Y) InkCenter(LabelBitmap bitmap)
    {
        double sumX = 0, sumY = 0; long count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                {
                    sumX += x; sumY += y; count++;
                }
            }
        }

        return (sumX / count / Dpi * 25.4, sumY / count / Dpi * 25.4);
    }

    [Fact]
    public void Image_element_should_encode_to_zpl_gf_via_full_pipeline()
    {
        // 全链路：图片元素 → Skia 整版位图 → ^GF 编码（验证元素进入打印编码路径而非仅预览）
        var bitmap = Render(new LabelLayout
        {
            Name = "img-zpl", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "logo", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10 }],
        }, new Dictionary<string, byte[]> { ["logo"] = BlackSquarePng() });

        var zpl = new ZplImageEncoder().EncodeImage(bitmap, 40, 20, Dpi);
        Assert.StartsWith("^XA", zpl);
        Assert.Contains("^GFA,", zpl);
        Assert.EndsWith("^XZ", zpl);
        // 40mm@203dpi = 320 点宽
        Assert.Contains("^PW320", zpl);
        Assert.Contains("^LL160", zpl);
    }
}
