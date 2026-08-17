using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class TransportConfigTests
{
    [Fact]
    public void FromJson_legacy_tcp_should_migrate_to_plugin_params()
    {
        var config = TransportConfig.FromJson("""{"Mode":"Tcp","TcpHost":"192.168.1.50","TcpPort":9100}""");

        Assert.NotNull(config);
        Assert.Equal("tcp9100", config!.PluginId);
        Assert.Equal("192.168.1.50", config.Params["host"]);
        Assert.Equal("9100", config.Params["port"]);
        Assert.Equal(TransportMode.Tcp, config.Mode);
        Assert.Equal("192.168.1.50", config.TcpHost);
    }

    [Fact]
    public void FromJson_legacy_windows_driver_should_migrate()
    {
        var config = TransportConfig.FromJson("""{"Mode":"WindowsDriver","PrinterName":"ZDesigner ZD421"}""");

        Assert.NotNull(config);
        Assert.Equal("winspool", config!.PluginId);
        Assert.Equal("ZDesigner ZD421", config.Params["printerName"]);
    }

    [Fact]
    public void FromJson_legacy_zebra_should_migrate_kind_and_params()
    {
        var config = TransportConfig.FromJson("""{"Mode":"Zebra","ZebraKind":"Tcp","TcpHost":"10.0.0.9","TcpPort":9100}""");

        Assert.NotNull(config);
        Assert.Equal("zebra", config!.PluginId);
        Assert.Equal("Tcp", config.Params["kind"]);
        Assert.Equal("10.0.0.9", config.Params["host"]);
    }

    [Fact]
    public void ToJson_should_round_trip_plugin_format()
    {
        var config = new TransportConfig
        {
            PluginId = "tcp9100",
            Params = new Dictionary<string, string> { ["host"] = "192.168.1.50", ["port"] = "9100" },
        };

        var json = config.ToJson();
        var restored = TransportConfig.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("tcp9100", restored!.PluginId);
        Assert.Equal("192.168.1.50", restored.Params["host"]);
        Assert.Equal(TransportMode.Tcp, restored.Mode); // 旧字段同步写出
    }

    [Fact]
    public void FromJson_invalid_should_return_null()
    {
        Assert.Null(TransportConfig.FromJson("not json"));
    }

    [Fact]
    public void MapModeToPluginId_should_cover_all_modes()
    {
        Assert.Equal("log", TransportConfig.MapModeToPluginId(TransportMode.Log));
        Assert.Equal("tcp9100", TransportConfig.MapModeToPluginId(TransportMode.Tcp));
        Assert.Equal("winspool", TransportConfig.MapModeToPluginId(TransportMode.WindowsDriver));
        Assert.Equal("zebra", TransportConfig.MapModeToPluginId(TransportMode.Zebra));
    }
}
