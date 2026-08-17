using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>测试用传输插件注册表 / 连接管理器工厂（迭代 22：注册表 = Core 内置 + WinHost 内置，同生产装配）。</summary>
public static class TestTransportRegistry
{
    /// <summary>生产同款注册表（log / tcp9100 / winspool / zebra）。</summary>
    public static TransportPluginRegistry Create()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        registry.Register(new WinspoolTransportPlugin());
        registry.Register(new ZebraTransportPlugin());
        return registry;
    }

    /// <summary>创建连接管理器（connection.json 可注入临时路径）。</summary>
    public static TransportManager CreateManager(HostOptions? options = null, string? configFilePath = null)
    {
        options ??= new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
        var path = configFilePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        return new TransportManager(
            Create(),
            new TransportPluginContext(TextWriter.Null, System.IO.Path.GetTempPath()),
            options,
            TextWriter.Null,
            path);
    }
}
