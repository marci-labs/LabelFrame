using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Tests.Transport;

public class Tcp9100PrintTransportTests
{
    [Fact]
    public async Task SendAsync_should_send_command_to_printer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var receivedTask = ReadAllAsync(listener);

        var transport = new Tcp9100PrintTransport("127.0.0.1", port, TimeSpan.FromSeconds(5));
        await transport.SendAsync("^XA^XZ");

        var received = await receivedTask;
        Assert.Equal("^XA^XZ", received);
        listener.Stop();
    }

    [Fact]
    public async Task SendAsync_should_throw_when_printer_unreachable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var transport = new Tcp9100PrintTransport("127.0.0.1", port, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("^XA^XZ"));
        Assert.Contains("连接打印机失败", exception.Message);
    }

    private static async Task<string> ReadAllAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        var received = new StringBuilder();
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
            if (read == 0)
            {
                break;
            }

            received.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
            if (received.Length > 0 && !stream.DataAvailable)
            {
                break;
            }
        }

        return received.ToString();
    }
}