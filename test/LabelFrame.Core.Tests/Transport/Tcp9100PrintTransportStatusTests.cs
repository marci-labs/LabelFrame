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
        // 先让服务端任务就绪（已进入 Accept）再发起连接，避免 CI 高负载下服务端任务调度延迟导致读取超时
        var serverReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            serverReady.SetResult(true);
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var buffer = new byte[256];
            _ = await stream.ReadAsync(buffer);
            var response = System.Text.Encoding.ASCII.GetBytes("0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0\n");
            await stream.WriteAsync(response);
        });

        await serverReady.Task;

        // 超时给足余量（CI 并行跑多个测试程序集时 5 秒可能不够）
        var transport = new Tcp9100PrintTransport("127.0.0.1", port, TimeSpan.FromSeconds(15));
        var status = await transport.GetStatusAsync();

        await serverTask;
        Assert.True(status.IsOnline);
        Assert.True(status.IsPaperOut);
        Assert.True(status.IsPaused);
        listener.Stop();
    }

}