using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.WinHost.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

public class GdiTextRasterizerTests
{
    private static LabelDocument CreateDocument(string value) => new()
    {
        Layout = new LabelLayout
        {
            Name = "raster",
            ContractName = "raster",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelTextElement { SourceKey = "name", XMm = 5, YMm = 5, FontHeightMm = 5, FontWidthMm = 5 },
            ],
        },
        Data = new Dictionary<string, string> { ["name"] = value },
    };

    [Fact]
    public void Chinese_text_should_be_replaced_by_image_with_bitmap()
    {
        var document = CreateDocument("库位A-01");
        var rasterizer = new GdiTextRasterizer();

        var result = rasterizer.Rasterize(document, dpi: 203);

        var image = Assert.IsType<LabelImageElement>(Assert.Single(result.Layout.Elements));
        Assert.True(result.Images.ContainsKey(image.SourceKey));
        var bitmap = result.Images[image.SourceKey];
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
        Assert.Contains(bitmap.Pixels, b => b != 0);
    }

    [Fact]
    public void Ascii_text_should_stay_as_native_text()
    {
        var document = CreateDocument("A-01-02-03");
        var rasterizer = new GdiTextRasterizer();

        var result = rasterizer.Rasterize(document, dpi: 203);

        Assert.Same(document, result);
        Assert.IsType<LabelTextElement>(Assert.Single(result.Layout.Elements));
        Assert.Empty(result.Images);
    }

    [Fact]
    public void Render_should_produce_non_empty_bitmap()
    {
        var rasterizer = new GdiTextRasterizer();

        var bitmap = rasterizer.Render("中文标签", fontHeightMm: 5, dpi: 203);

        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
        Assert.Contains(bitmap.Pixels, b => b != 0);
    }
}