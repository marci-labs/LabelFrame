using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;

namespace LabelFrame.Core.Tests.Templates;

public class TemplatePackageTests
{
    private static TemplatePackage CreatePackage(string name = "location-label") => new()
    {
        Name = name,
        Group = "项目A",
        Contract = new LabelContract
        {
            Name = "location-label",
            Version = "1.0",
            Fields =
            [
                new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            ],
        },
        Layout = new LabelLayout
        {
            Name = "location-label-100x60",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelTextElement { SourceKey = "locationCode", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 },
                new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
            ],
        },
        Images = new Dictionary<string, byte[]> { ["logo.png"] = new byte[] { 1, 2, 3, 4 } },
    };

    [Fact]
    public void Export_import_should_round_trip()
    {
        var zip = TemplatePackageSerializer.Export(CreatePackage());

        var imported = TemplatePackageSerializer.Import(zip);

        Assert.Equal("location-label", imported.Name);
        Assert.Equal("项目A", imported.Group);
        Assert.Equal("location-label", imported.Contract.Name);
        Assert.Equal(2, imported.Layout.Elements.Count);
        Assert.IsType<LabelBarcodeElement>(imported.Layout.Elements[1]);
        Assert.True(imported.Images.ContainsKey("logo.png"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, imported.Images["logo.png"]);
    }

    [Fact]
    public void Import_without_manifest_should_fail()
    {
        Assert.Throws<InvalidDataException>(() => TemplatePackageSerializer.Import(new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }
}