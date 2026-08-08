using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.WinHost.Rendering;

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