using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class PrinterStatusTests
{
    [Fact]
    public async Task LogPrintTransport_should_report_online()
    {
        using var writer = new StringWriter();
        var transport = new LogPrintTransport(writer);

        var status = await transport.GetStatusAsync();

        Assert.True(status.IsOnline);
    }

    [Fact]
    public async Task RawPrinterTransport_should_report_online_with_note()
    {
        var transport = new RawPrinterTransport("任意打印机名");

        var status = await transport.GetStatusAsync();

        Assert.True(status.IsOnline);
        Assert.Contains("无法读回", status.Message);
    }

    [Fact]
    public async Task ZebraPrinterTransport_should_report_offline_when_unreachable()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var transport = new ZebraPrinterTransport(ZebraTransportKind.Tcp, "127.0.0.1", port);

        var status = await transport.GetStatusAsync();

        Assert.False(status.IsOnline);
    }
}