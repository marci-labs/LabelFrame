using System.Net;
using LabelFrame.WinHost.Ui;

namespace LabelFrame.WinHost.Tests.Ui;

/// <summary>界面就绪等待：窗口加载态期间按间隔探测本地服务，就绪 / 超时 / 探测异常三类路径。</summary>
public class UiReadinessTests
{
    [Fact]
    public async Task 立即就绪_直接返回真()
    {
        var result = await UiReadiness.WaitUntilReadyAsync(
            () => Task.FromResult(true),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromSeconds(1));

        Assert.True(result);
    }

    [Fact]
    public async Task 探测失败若干次后就绪_返回真()
    {
        var attempts = 0;
        var result = await UiReadiness.WaitUntilReadyAsync(
            () =>
            {
                attempts++;
                return Task.FromResult(attempts >= 3);
            },
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(5));

        Assert.True(result);
        Assert.True(attempts >= 3);
    }

    [Fact]
    public async Task 探测抛异常_视为未就绪并继续等待()
    {
        var attempts = 0;
        var result = await UiReadiness.WaitUntilReadyAsync(
            () =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new HttpRequestException("connection refused");
                }

                return Task.FromResult(true);
            },
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(5));

        Assert.True(result);
        Assert.True(attempts >= 2);
    }

    [Fact]
    public async Task 一直未就绪_超时返回假()
    {
        var result = await UiReadiness.WaitUntilReadyAsync(
            () => Task.FromResult(false),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(60));

        Assert.False(result);
    }

    private static int GetFreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task HttpProbe_任意HTTP响应即视为就绪()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
            catch
            {
                // 探测端关闭后的监听异常忽略
            }
        });

        var probe = UiReadiness.HttpProbe(new Uri($"http://127.0.0.1:{port}/healthz"), TimeSpan.FromSeconds(2));

        Assert.True(await probe());
        listener.Stop();
    }

    [Fact]
    public async Task HttpProbe_连接失败抛出由等待循环吞掉()
    {
        var port = GetFreePort(); // 已释放端口，无人监听，探测必然连接失败

        var probe = UiReadiness.HttpProbe(new Uri($"http://127.0.0.1:{port}/healthz"), TimeSpan.FromMilliseconds(200));
        var result = await UiReadiness.WaitUntilReadyAsync(probe, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(50));

        Assert.False(result);
    }
}
