using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Tests.Json;

public class TemplateJsonTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new LabelElementJsonConverter() },
    };

    [Fact]
    public void Layout_elements_should_round_trip_with_type_discriminator()
    {
        var layout = new LabelLayout
        {
            Name = "location-label-100x60",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
                new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
            ],
        };

        var json = JsonSerializer.Serialize(layout, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelLayout>(json, Options);

        Assert.NotNull(roundTrip);
        Assert.Equal(2, roundTrip!.Elements.Count);
        Assert.IsType<LabelTextElement>(roundTrip.Elements[0]);
        Assert.IsType<LabelBarcodeElement>(roundTrip.Elements[1]);
        Assert.Equal("zone", ((LabelTextElement)roundTrip.Elements[0]).SourceKey);
        Assert.Contains("\"type\":\"text\"", json);
    }

    [Fact]
    public void Contract_fields_should_round_trip_with_string_enum()
    {
        var contract = new LabelContract
        {
            Name = "location-label",
            Version = "1.0",
            Fields =
            [
                new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true, Type = LabelFieldType.Text },
            ],
        };

        var json = JsonSerializer.Serialize(contract, Options);
        var roundTrip = JsonSerializer.Deserialize<LabelContract>(json, Options);

        Assert.NotNull(roundTrip);
        Assert.Equal("text", Assert.Single(roundTrip!.Fields).Type.ToString().ToLowerInvariant());
    }
}