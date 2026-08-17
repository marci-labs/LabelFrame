using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

public class TransportPluginParametersTests
{
    private static TransportPluginParameters Create() => new(new Dictionary<string, string>
    {
        ["host"] = "192.168.1.50",
        ["port"] = "9100",
        ["enabled"] = "true",
        ["off"] = "false",
        ["kind"] = "Usb",
    });

    [Fact]
    public void Typed_getters_should_parse_string_values()
    {
        var p = Create();
        Assert.Equal("192.168.1.50", p.GetString("host"));
        Assert.Equal(9100, p.GetInt("port"));
        Assert.True(p.GetBool("enabled"));
        Assert.False(p.GetBool("off"));
        Assert.Equal("Usb", p.GetSelect("kind"));
    }

    [Fact]
    public void Missing_keys_should_fall_back_to_defaults()
    {
        var p = new TransportPluginParameters();
        Assert.Null(p.GetString("host"));
        Assert.Equal("default", p.GetString("host", "default"));
        Assert.Null(p.GetInt("port"));
        Assert.Equal(42, p.GetInt("port", 42));
        Assert.Null(p.GetBool("enabled"));
        Assert.True(p.GetBool("enabled", true));
        Assert.False(p.ContainsKey("host"));
    }

    [Fact]
    public void Invalid_values_should_fall_back_to_defaults()
    {
        var p = new TransportPluginParameters(new Dictionary<string, string> { ["port"] = "abc", ["enabled"] = "yes" });
        Assert.Null(p.GetInt("port"));
        Assert.Equal(1, p.GetInt("port", 1));
        Assert.Null(p.GetBool("enabled"));
    }

    [Fact]
    public void Raw_should_expose_original_dictionary()
    {
        var p = Create();
        Assert.Equal("192.168.1.50", p.Raw["host"]);
    }
}
