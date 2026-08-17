using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Transport;

/// <summary>Zebra 官方 Link-OS SDK 传输插件（TCP / USB / 驱动统一连接）。</summary>
public sealed class ZebraTransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "zebra";

    /// <inheritdoc />
    public string DisplayName => "Zebra";

    /// <inheritdoc />
    public string Description => "Zebra 官方 Link-OS SDK：TCP / USB（自动发现）/ Windows 驱动统一连接与打印机状态（Win10+）。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("kind", "连接类型", TransportParameterType.Select, Required: true, DefaultValue: "Tcp", Options: new[]
        {
            new TransportParameterOption("Tcp", "TCP"),
            new TransportParameterOption("Usb", "USB（自动发现）"),
            new TransportParameterOption("Driver", "Windows 驱动"),
        }),
        new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, Required: false, Hint: "kind=Tcp 时必填，如 192.168.1.50"),
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100"),
        new TransportParameterSpec("printerName", "Windows 打印机名", TransportParameterType.String, Required: false, Hint: "kind=Driver 时必填"),
        new TransportParameterSpec("usbName", "Zebra USB 打印机名", TransportParameterType.String, Required: false, Hint: "kind=Usb 时为空自动发现第一台"),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters p)
    {
        var kind = p.GetString("kind") ?? "Tcp";
        return kind switch
        {
            "Usb" => string.IsNullOrWhiteSpace(p.GetString("usbName")) ? "Zebra USB（自动发现）" : $"Zebra USB {p.GetString("usbName")}",
            "Driver" => $"Zebra 驱动 {p.GetString("printerName") ?? "?"}",
            _ => $"Zebra TCP {p.GetString("host", "?")}:{p.GetInt("port", 9100)}",
        };
    }

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters p, ITransportPluginContext context)
    {
        var kind = Enum.TryParse<ZebraTransportKind>(p.GetString("kind"), ignoreCase: true, out var parsed) ? parsed : ZebraTransportKind.Tcp;
        return new ZebraPrinterTransport(
            kind,
            p.GetString("host") ?? string.Empty,
            p.GetInt("port", 9100),
            p.GetString("printerName") ?? string.Empty,
            p.GetString("usbName") ?? string.Empty);
    }
}
