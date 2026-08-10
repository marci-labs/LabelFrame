using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

public class SkiaLabelRendererTests
{
    private static int CountBlack(LabelBitmap bitmap, double xMm, double yMm, double wMm, double hMm, int dpi = 203)
    {
        var black = 0;
        var x0 = (int)Math.Round(xMm / 25.4 * dpi);
        var x1 = (int)Math.Round((xMm + wMm) / 25.4 * dpi);
        var y0 = (int)Math.Round(yMm / 25.4 * dpi);
        var y1 = (int)Math.Round((yMm + hMm) / 25.4 * dpi);
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

    [Fact]
    public void Material_label_70x50_should_render_all_four_fields()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "70x50",
                ContractName = "70x50",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelQrCodeElement { SourceKey = "UniCode", XMm = 30, YMm = 7.5, SizeMm = 35 },
                    new LabelTextElement { SourceKey = "MaterialName", XMm = 5, YMm = 10, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25 },
                    new LabelTextElement { SourceKey = "CompanyName", XMm = 40, YMm = 0, FontHeightMm = 1.8, FontWidthMm = 1.8, WidthMm = 25, TextAlign = LabelTextAlign.Right },
                    new LabelTextElement { SourceKey = "Quantity", XMm = 11, YMm = 38, FontHeightMm = 2, FontWidthMm = 2, WidthMm = 10 },
                    new LabelTextElement { SourceKey = "Specification", XMm = 5, YMm = 25, FontHeightMm = 2, FontWidthMm = 2, WidthMm = 25 },
                    new LabelTextElement { Literal = "M:", XMm = 5, YMm = 0, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 7 },
                    new LabelTextElement { SourceKey = "MaterialCode", XMm = 10, YMm = 0, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 30 },
                    new LabelTextElement { SourceKey = "UniCode", XMm = 11, YMm = 42, FontHeightMm = 2, FontWidthMm = 2, WidthMm = 30 },
                    new LabelTextElement { Literal = "QTY:", XMm = 5, YMm = 38, FontHeightMm = 2, FontWidthMm = 2, WidthMm = 10 },
                    new LabelTextElement { Literal = "S/N:", XMm = 5, YMm = 42, FontHeightMm = 2, FontWidthMm = 2, WidthMm = 10 },
                    new LabelTextElement { SourceKey = "WarehouseName", XMm = 40, YMm = 42, FontHeightMm = 1.8, FontWidthMm = 1.8, WidthMm = 25, TextAlign = LabelTextAlign.Right },
                ],
            },
            Data = new Dictionary<string, string>
            {
                ["UniCode"] = "20260808092529000001",
                ["MaterialName"] = "5m门架O20起升拉线固定支架",
                ["CompanyName"] = "劢微机器人科技（深圳）有限公司",
                ["Quantity"] = "300",
                ["Specification"] = "CAP25096-O20-260807-01",
                ["MaterialCode"] = "ME0100500654",
                ["WarehouseName"] = "WMS原材料仓（浙江）",
            },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.Equal(559, bitmap.Width);
        Assert.Equal(400, bitmap.Height);
        // 用户模板中曾完全缺失的四个字段必须有墨迹
        Assert.True(CountBlack(bitmap, 5, 10, 25, 15) > 200, $"MaterialName 缺失，黑像素={CountBlack(bitmap, 5, 10, 25, 15)}");
        Assert.True(CountBlack(bitmap, 40, 0, 25, 10) > 500, $"CompanyName 缺失，黑像素={CountBlack(bitmap, 40, 0, 25, 10)}");
        Assert.True(CountBlack(bitmap, 5, 25, 25, 10) > 200, $"Specification 缺失，黑像素={CountBlack(bitmap, 5, 25, 25, 10)}");
        Assert.True(CountBlack(bitmap, 40, 42, 25, 6) > 350, $"WarehouseName 缺失，黑像素={CountBlack(bitmap, 40, 42, 25, 6)}");
    }

    [Fact]
    public void Long_cjk_text_should_shrink_to_fit_box()
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

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        var total = 0;
        for (var i = 0; i < bitmap.Pixels.Length; i++) total += System.Numerics.BitOperations.PopCount(bitmap.Pixels[i]);
        Assert.True(CountBlack(bitmap, 5, 10, 25, 15) > 200, $"长中文文本缩小适应后不应消失，区域黑像素={CountBlack(bitmap, 5, 10, 25, 15)}，全图黑像素={total}");
    }

    [Fact]
    public void Text_without_height_and_with_padding_should_render()
    {
        // 旧模板：无 heightMm、有 1mm 内边距（前端默认）——裁剪区不得塌缩
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "old",
                ContractName = "old",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 10, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, PaddingMm = 1 },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "ABCD" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.True(CountBlack(bitmap, 5, 10, 25, 15) > 100, "旧模板（无框高+内边距）文本不应消失");
    }

    [Fact]
    public void Text_with_middle_align_should_center_in_box()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "valign",
                ContractName = "valign",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 10, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, HeightMm = 15, VerticalAlign = LabelVerticalAlign.Middle },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "ABCD" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        // 框 y=10-25mm、字高 3mm：Middle 应在中部（y≈16-19mm），顶部带几乎为空
        var topBand = CountBlack(bitmap, 5, 10, 25, 3);
        var midBand = CountBlack(bitmap, 5, 16, 25, 3);
        Assert.True(midBand > 50, $"Middle 对齐文字应画在中部，midBand={midBand}");
        Assert.True(midBand > topBand * 3, $"Middle 对齐时顶部带应远少于中部：top={topBand} mid={midBand}");
    }

    [Fact]
    public void RenderLabelBitmapPng_should_return_valid_png()
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
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8, WidthMm = 60 },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "ABC-123" },
        };

        var png = new SkiaLabelRenderer().RenderLabelBitmapPng(document, dpi: 203);

        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
    }
}
