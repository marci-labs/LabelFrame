using System.Net;
using System.Net.Sockets;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class ZebraPrinterTransportTests
{
    [Fact]
    public void Constructor_should_require_tcp_address()
    {
        Assert.Throws<ArgumentException>(() => new ZebraPrinterTransport(ZebraTransportKind.Tcp));
    }

    [Fact]
    public void Constructor_should_require_driver_printer_name()
    {
        Assert.Throws<ArgumentException>(() => new ZebraPrinterTransport(ZebraTransportKind.Driver));
    }

    [Fact]
    public async Task SendAsync_to_unreachable_printer_should_throw_clear_error()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var transport = new ZebraPrinterTransport(ZebraTransportKind.Tcp, "127.0.0.1", port);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("^XA^XZ"));
        Assert.Contains("Zebra", exception.Message);
    }
}