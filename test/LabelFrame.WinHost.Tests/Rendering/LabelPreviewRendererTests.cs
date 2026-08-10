using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

public class LabelPreviewRendererTests
{
    [Fact]
    public void RenderPng_should_produce_valid_png_for_location_label()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "location-label-100x60",
                ContractName = "location-label",
                ContractVersion = "1.0",
                WidthMm = 100,
                HeightMm = 60,
                Elements =
                [
                    new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
                    new LabelTextElement { SourceKey = "locationCode", XMm = 5, YMm = 14, FontHeightMm = 8, FontWidthMm = 8 },
                    new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
                ],
            },
            Data = new Dictionary<string, string>
            {
                ["zone"] = "中文区域",
                ["locationCode"] = "A-01-02-03",
            },
        };

        var renderer = new LabelPreviewRenderer();
        var png = renderer.RenderPng(document, dpi: 203);

        // PNG 魔数 + 非空
        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
        // 100mm x 60mm @203dpi = 800x480
        Assert.True(png.Length > 1000, "PNG 内容过小，疑似未绘制元素。");
    }

    [Fact]
    public void RenderLabelBitmap_should_match_label_size_and_have_content()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "loc",
                ContractName = "loc",
                ContractVersion = "1.0",
                WidthMm = 100,
                HeightMm = 60,
                Elements =
                [
                    new LabelTextElement { SourceKey = "locationCode", XMm = 5, YMm = 14, FontHeightMm = 8, FontWidthMm = 8 },
                ],
            },
            Data = new Dictionary<string, string> { ["locationCode"] = "A-01-02-03" },
        };

        var bitmap = new LabelPreviewRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.Equal(799, bitmap.Width);
        Assert.Equal(480, bitmap.Height);
        Assert.True(bitmap.Pixels.Any(b => b != 0), "位图不应为全白（应有文字内容）。");
    }

    [Fact]
    public void Right_aligned_small_cjk_text_should_render()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "right",
                ContractName = "right",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelQrCodeElement { SourceKey = "qr", XMm = 30, YMm = 7.5, SizeMm = 35 },
                    new LabelTextElement { SourceKey = "t", XMm = 40, YMm = 0, FontHeightMm = 1.8, FontWidthMm = 1.8, WidthMm = 25, TextAlign = LabelTextAlign.Right },
                ],
            },
            Data = new Dictionary<string, string> { ["qr"] = "20260808092529000001", ["t"] = "劢微机器人科技（深圳）有限公司" },
        };

        var bitmap = new LabelPreviewRenderer().RenderLabelBitmap(document, dpi: 203);

        // 文本区域 x 40-65mm，y 0-10mm
        var black = 0;
        var x0 = (int)Math.Round(40 / 25.4 * 203);
        var x1 = (int)Math.Round(65 / 25.4 * 203);
        var y0 = 0;
        var y1 = (int)Math.Round(10 / 25.4 * 203);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                {
                    black++;
                }
            }
        }

        Assert.True(black > 300, $"右对齐小字不应消失，黑像素={black}");
    }

    [Fact]
    public void Long_cjk_text_in_box_should_render_shrunk_not_disappear()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "long",
                ContractName = "long",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 10, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25 },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "5m门架O20起升拉线固定支架" },
        };

        var bitmap = new LabelPreviewRenderer().RenderLabelBitmap(document, dpi: 203);

        // 文本区域（x 5-30mm，y 10-25mm）必须有墨迹（长文本缩小适应，而不是消失）
        var black = 0;
        var x0 = (int)Math.Round(5 / 25.4 * 203);
        var x1 = (int)Math.Round(30 / 25.4 * 203);
        var y0 = (int)Math.Round(10 / 25.4 * 203);
        var y1 = (int)Math.Round(25 / 25.4 * 203);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                {
                    black++;
                }
            }
        }

        Assert.True(black > 200, $"长中文文本不应消失，黑像素={black}");
    }

    [Fact]
    public void RenderLabelBitmapPng_should_return_png_of_print_bitmap()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "png",
                ContractName = "png",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "text", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8, WidthMm = 60 },
                ],
            },
            Data = new Dictionary<string, string> { ["text"] = "ABC-123" },
        };

        var png = new LabelPreviewRenderer().RenderLabelBitmapPng(document, dpi: 203);

        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
    }

    [Fact]
    public void RenderPng_should_include_qr_code()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "qr",
                ContractName = "qr",
                ContractVersion = "1.0",
                WidthMm = 50,
                HeightMm = 50,
                Elements =
                [
                    new LabelQrCodeElement { SourceKey = "qr", XMm = 5, YMm = 5, SizeMm = 20 },
                ],
            },
            Data = new Dictionary<string, string> { ["qr"] = "LABELFRAME-DEMO-001" },
        };

        var png = new LabelPreviewRenderer().RenderPng(document, dpi: 203);

        Assert.True(png.Length > 1000);
    }
}