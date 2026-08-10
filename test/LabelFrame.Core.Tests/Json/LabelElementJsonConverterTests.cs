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
    public void Literal_should_round_trip()
    {
        var element = new LabelTextElement { SourceKey = string.Empty, Literal = "库位标签", XMm = 1, YMm = 2, FontHeightMm = 5, FontWidthMm = 5 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal("库位标签", text.Literal);
        Assert.Contains("literal", json);
    }

    [Fact]
    public void Preview_value_should_round_trip_for_field_mode_elements()
    {
        var element = new LabelTextElement { SourceKey = "locationCode", PreviewValue = "A-01-02-03", XMm = 5, YMm = 5, FontHeightMm = 5, FontWidthMm = 5 };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal("A-01-02-03", text.PreviewValue);
        Assert.Contains("\"previewValue\":\"A-01-02-03\"", json);
    }

    [Fact]
    public void Missing_preview_value_should_be_null_and_literal_mode_should_not_emit_it()
    {
        const string fieldJson = """{"type":"text","xMm":5,"yMm":5,"sourceKey":"k","fontHeightMm":5,"fontWidthMm":5}""";
        var field = Assert.IsType<LabelTextElement>(JsonSerializer.Deserialize<LabelElement>(fieldJson, Options));
        Assert.Null(field.PreviewValue);

        var literal = new LabelTextElement { Literal = "x", XMm = 0, YMm = 0, FontHeightMm = 5, FontWidthMm = 5 };
        var literalJson = JsonSerializer.Serialize<LabelElement>(literal, Options);
        Assert.DoesNotContain("previewValue", literalJson);
    }
    [Fact]
    public void Text_height_and_vertical_align_should_round_trip()
    {
        var element = new LabelTextElement { SourceKey = "k", XMm = 5, YMm = 10, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 25, HeightMm = 15, VerticalAlign = LabelVerticalAlign.Middle };

        var json = JsonSerializer.Serialize<LabelElement>(element, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelElement>(json, Options);

        var text = Assert.IsType<LabelTextElement>(roundTrip);
        Assert.Equal(15, text.HeightMm);
        Assert.Equal(LabelVerticalAlign.Middle, text.VerticalAlign);
        Assert.Contains("\"heightMm\":15", json);
        Assert.Contains("\"verticalAlign\":\"Middle\"", json);
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