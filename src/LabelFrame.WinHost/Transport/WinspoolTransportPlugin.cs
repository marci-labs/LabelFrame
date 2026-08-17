using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Transport;

/// <summary>Windows 驱动（winspool raw）传输插件。</summary>
public sealed class WinspoolTransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "winspool";

    /// <inheritdoc />
    public string DisplayName => "Windows 驱动";

    /// <inheritdoc />
    public string Description => "Windows 驱动（USB / 已安装打印机）：通过 winspool 以 RAW 方式发送指令，无需打印机 IP。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("printerName", "Windows 打印机名", TransportParameterType.String, Required: true, Hint: "如 ZDesigner ZD421-203dpi ZPL"),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters p)
        => string.IsNullOrWhiteSpace(p.GetString("printerName"))
            ? "WindowsDriver ?"
            : $"WindowsDriver {p.GetString("printerName")}";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters p, ITransportPluginContext context)
        => new RawPrinterTransport(
            p.GetString("printerName") ?? throw new ArgumentException("缺少 Windows 打印机名（printerName）。"));
}
