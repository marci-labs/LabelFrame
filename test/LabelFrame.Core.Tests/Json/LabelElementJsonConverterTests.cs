using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Tests.Json;

public class LabelElementJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };

    [Theory]
    [InlineData("""{"type":"text","xMm":5,"yMm":4,"sourceKey":"zone","fontHeightMm":5,"fontWidthMm":5}""")]
    [InlineData("""{"type":"barcode","xMm":5,"yMm":26,"sourceKey":"code","heightMm":22,"moduleWidth":2}""")]
    [InlineData("""{"type":"qrcode","xMm":1,"yMm":1,"sourceKey":"qr","sizeMm":20}""")]
    [InlineData("""{"type":"image","xMm":80,"yMm":4,"sourceKey":"logo","widthMm":10,"heightMm":10}""")]
    [InlineData("""{"type":"line","xMm":0,"yMm":0,"x2Mm":10,"y2Mm":10,"thicknessMm":0.5}""")]
    [InlineData("""{"type":"region","xMm":5,"yMm":5,"id":"top","widthMm":60,"heightMm":30,"borderMm":0.3}""")]
    [InlineData("""{"type":"text","xMm":0,"yMm":0,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5,"widthMm":40,"textAlign":"Center","paddingMm":1,"borderMm":0.3,"regionId":"top","regionHAlign":"Center","regionVAlign":"End"}""")]
    public void Read_should_parse_each_element_type(string json)
    {
        var element = JsonSerializer.Deserialize<LabelElement>(json, Options);

        Assert.NotNull(element);
        Assert.Equal(element.Type.ToString().ToLowerInvariant(), GetTypeName(element));
    }

    [Fact]
    public void Round_trip_should_preserve_properties()
    {
        var element = new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var barcode = Assert.IsType<LabelBarcodeElement>(roundTrip);
        Assert.Equal("locationCode", barcode.SourceKey);
        Assert.Equal(5, barcode.XMm);
        Assert.Equal(26, barcode.YMm);
        Assert.Equal(22, barcode.HeightMm);
        Assert.Equal(2, barcode.ModuleWidth);
        Assert.Contains("\"type\":\"barcode\"", json);
    }

    [Fact]
    public void Region_and_style_should_round_trip()
    {
        var element = new LabelTextElement
        {
            SourceKey = "k",
            XMm = 0,
            YMm = 0,
            FontHeightMm = 5,
            FontWidthMm = 5,
            WidthMm = 40,
            TextAlign = LabelTextAlign.Center,
            PaddingMm = 1,
            BorderMm = 0.3,
            RegionId = "top",
            RegionHAlign = LabelRegionAlign.Center,
            RegionVAlign = LabelRegionAlign.End,
        };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal(40, text.WidthMm);
        Assert.Equal(LabelTextAlign.Center, text.TextAlign);
        Assert.Equal(1, text.PaddingMm);
        Assert.Equal(0.3, text.BorderMm);
        Assert.Equal("top", text.RegionId);
        Assert.Equal(LabelRegionAlign.End, text.RegionVAlign);
    }

    [Fact]
    public void Unknown_type_should_throw_json_exception()
    {
        const string json = """{"type":"hexagon","xMm":0,"yMm":0}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LabelElement>(json, Options));
    }

    private static string GetTypeName(LabelElement element) => element switch
    {
        LabelTextElement => "text",
        LabelBarcodeElement => "barcode",
        LabelQrCodeElement => "qrcode",
        LabelImageElement => "image",
        LabelLineElement => "line",
        LabelRegionElement => "region",
        _ => throw new InvalidOperationException(),
    };
}