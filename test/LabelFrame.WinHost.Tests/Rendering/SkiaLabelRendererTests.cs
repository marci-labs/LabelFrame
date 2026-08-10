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

    [Fact]
    public void Wrap_true_text_should_wrap_into_multiple_lines()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "wrap",
                ContractName = "wrap",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, HeightMm = 15, Wrap = true, VerticalAlign = LabelVerticalAlign.Top },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "5m门架O20起升拉线固定支架" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        // 15 字符 @3mm、块宽 25mm → 至少两行：第一行带与第二行带都应有墨迹
        var firstBand = CountBlack(bitmap, 5, 5, 25, 3);
        var secondBand = CountBlack(bitmap, 5, 8, 25, 3);
        Assert.True(firstBand > 50, $"wrap=true 第一行应有墨迹：{firstBand}");
        Assert.True(secondBand > 50, $"wrap=true 第二行应有墨迹：{secondBand}");
    }

    [Fact]
    public void Wrap_true_text_over_height_should_shrink_to_fit_box()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "wrap-fit",
                ContractName = "wrap-fit",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, HeightMm = 6, Wrap = true, VerticalAlign = LabelVerticalAlign.Top },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "5m门架O20起升拉线固定支架" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.True(CountBlack(bitmap, 5, 5, 25, 6) > 200, $"wrap=true 超高整体缩小后框内应有墨迹：{CountBlack(bitmap, 5, 5, 25, 6)}");
        Assert.Equal(0, CountBlack(bitmap, 5, 11, 25, 5));
    }

    [Fact]
    public void Overflow_should_keep_font_size_while_shrink_reduces_it()
    {
        LabelDocument Doc(LabelFitMode fitMode) => new()
        {
            Layout = new LabelLayout
            {
                Name = "fit",
                ContractName = "fit",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 10, HeightMm = 8, FitMode = fitMode, VerticalAlign = LabelVerticalAlign.Top },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "5m门架O20起升拉线固定支架" },
        };

        var shrink = new SkiaLabelRenderer().RenderLabelBitmap(Doc(LabelFitMode.Shrink), dpi: 203);
        var overflow = new SkiaLabelRenderer().RenderLabelBitmap(Doc(LabelFitMode.Overflow), dpi: 203);

        // 框内 2~3mm 带：overflow 保持 3mm 字号（墨迹约到框内 3mm）有墨迹；shrink 缩到最小 1.5mm 则无
        var midBand = CountBlack(overflow, 5, 7, 10, 1);
        Assert.True(midBand > 0, $"overflow 保持字号，框内 2~3mm 带应有墨迹：{midBand}");
        Assert.Equal(0, CountBlack(shrink, 5, 7, 10, 1));
    }

    [Fact]
    public void Text_with_custom_font_family_should_render()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "font",
                ContractName = "font",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 5, FontWidthMm = 5, WidthMm = 40, FontFamily = "Arial" },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "ABCDEF" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.True(CountBlack(bitmap, 5, 5, 40, 10) > 50, "自定义 fontFamily 的文本应正常渲染");
    }

    [Fact]
    public void Qr_code_ecc_and_margin_should_render_with_quiet_zone()
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
                    new LabelQrCodeElement { SourceKey = "qr", XMm = 5, YMm = 5, SizeMm = 20, QrEcc = LabelQrEcc.H, QrMargin = 4 },
                ],
            },
            Data = new Dictionary<string, string> { ["qr"] = "LABELFRAME-DEMO-001" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.True(CountBlack(bitmap, 5, 5, 20, 20) > 100, "二维码应有墨迹");
        // 静区：qrMargin=4 模块，外圈 1mm 应为空白
        Assert.Equal(0, CountBlack(bitmap, 5, 5, 20, 1));
        Assert.Equal(0, CountBlack(bitmap, 5, 24, 20, 1));
        Assert.Equal(0, CountBlack(bitmap, 5, 5, 1, 20));
        Assert.Equal(0, CountBlack(bitmap, 24, 5, 1, 20));
    }

    [Fact]
    public void Qr_code_ecc_variants_should_render()
    {
        foreach (var ecc in new[] { LabelQrEcc.L, LabelQrEcc.M, LabelQrEcc.Q, LabelQrEcc.H })
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
                        new LabelQrCodeElement { SourceKey = "qr", XMm = 5, YMm = 5, SizeMm = 20, QrEcc = ecc, QrMargin = 2 },
                    ],
                },
                Data = new Dictionary<string, string> { ["qr"] = "LABELFRAME-DEMO-001" },
            };

            var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

            Assert.True(CountBlack(bitmap, 5, 5, 20, 20) > 100, $"ECC {ecc} 二维码应渲染");
        }
    }

    [Fact]
    public void Barcode_display_value_should_render_bottom_text_or_bars_to_bottom()
    {
        LabelDocument Doc(bool displayValue) => new()
        {
            Layout = new LabelLayout
            {
                Name = "bc",
                ContractName = "bc",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelBarcodeElement { SourceKey = "code", XMm = 5, YMm = 5, HeightMm = 22, ModuleWidth = 2, DisplayValue = displayValue },
                ],
            },
            Data = new Dictionary<string, string> { ["code"] = "1234567890" },
        };

        var withText = new SkiaLabelRenderer().RenderLabelBitmap(Doc(true), dpi: 203);
        var withoutText = new SkiaLabelRenderer().RenderLabelBitmap(Doc(false), dpi: 203);

        // displayValue=true：底部 0.5mm 为文字基线以下空白（数字无下伸笔画）
        Assert.Equal(0, CountBlack(withText, 5, 26.5, 55, 0.5));
        // displayValue=false：条码条贯穿到底，底部 0.5mm 有墨迹
        Assert.True(CountBlack(withoutText, 5, 26.5, 55, 0.5) > 0, "displayValue=false 时条码条应画到底部");
        // 文字带（底部约 15% 高度）应有数字墨迹
        Assert.True(CountBlack(withText, 5, 22, 55, 4) > 20, "displayValue=true 时底部应绘制数值文字");
    }

    [Fact]
    public void Text_asymmetric_padding_should_inset_content()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "pad",
                ContractName = "pad",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, HeightMm = 12, PaddingHMm = 2, PaddingVMm = 1, VerticalAlign = LabelVerticalAlign.Middle },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "ABCD" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        // 左 1mm 与上 0.5mm 应为内边距空白
        Assert.Equal(0, CountBlack(bitmap, 5, 5, 1, 12));
        Assert.Equal(0, CountBlack(bitmap, 5, 5, 25, 0.5));
        // 内容区中部应有墨迹
        Assert.True(CountBlack(bitmap, 7, 9, 20, 3) > 20, $"双边内边距后内容区应有墨迹：{CountBlack(bitmap, 7, 9, 20, 3)}");
    }

    [Fact]
    public void Old_template_without_height_should_center_in_fallback_box()
    {
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
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25 },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "ABCD" },
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        // 无 heightMm：框高兜底 = max(字高 + 2×内边距, 10) = 10mm；默认 Middle 居中 → 顶部带为空、中部有墨迹
        Assert.Equal(0, CountBlack(bitmap, 5, 5, 25, 2));
        Assert.True(CountBlack(bitmap, 5, 8, 25, 3) > 20, $"旧模板默认 Middle 应在兜底框中部：{CountBlack(bitmap, 5, 8, 25, 3)}");
    }

    [Fact]
    public void Bold_text_should_render_thicker_than_regular()
    {
        LabelDocument Doc(bool bold) => new()
        {
            Layout = new LabelLayout
            {
                Name = "bold",
                ContractName = "bold",
                ContractVersion = "1.0",
                WidthMm = 70,
                HeightMm = 50,
                Elements =
                [
                    new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 4, FontWidthMm = 4, WidthMm = 40, Bold = bold },
                ],
            },
            Data = new Dictionary<string, string> { ["t"] = "加粗测试" },
        };

        var regular = new SkiaLabelRenderer().RenderLabelBitmap(Doc(false), dpi: 203);
        var bold = new SkiaLabelRenderer().RenderLabelBitmap(Doc(true), dpi: 203);

        var regularInk = CountBlack(regular, 5, 5, 40, 10);
        var boldInk = CountBlack(bold, 5, 5, 40, 10);
        Assert.True(regularInk > 100, $"常规文本应有墨迹：{regularInk}");
        Assert.True(boldInk > regularInk, $"加粗文本墨迹应多于常规：bold={boldInk} regular={regularInk}");
    }
}
