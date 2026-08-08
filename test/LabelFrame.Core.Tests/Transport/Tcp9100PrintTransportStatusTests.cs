using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Tests.Transport;

public class Tcp9100PrintTransportStatusTests
{
    [Fact]
    public async Task GetStatusAsync_should_parse_hs_response()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var buffer = new byte[256];
            _ = await stream.ReadAsync(buffer);
            var response = System.Text.Encoding.ASCII.GetBytes("0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0\n");
            await stream.WriteAsync(response);
        });

        var transport = new Tcp9100PrintTransport("127.0.0.1", port, TimeSpan.FromSeconds(5));
        var status = await transport.GetStatusAsync();

        await serverTask;
        Assert.True(status.IsOnline);
        Assert.True(status.IsPaperOut);
        Assert.True(status.IsPaused);
        listener.Stop();
    }

}