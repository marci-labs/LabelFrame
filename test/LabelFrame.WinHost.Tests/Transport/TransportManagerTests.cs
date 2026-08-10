using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class TransportManagerTests
{
    private static (TransportManager Manager, string ConfigPath) Create(string? configFilePath = null, TransportMode defaultMode = TransportMode.Log)
    {
        var options = new HostOptions { Transport = defaultMode, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
        var path = configFilePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        return (new TransportManager(options, TextWriter.Null, path), path);
    }

    [Fact]
    public async Task ApplyAsync_tcp_without_host_should_fail()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Tcp, TcpHost = "", TcpPort = 9100 }, testOnly: false);
        Assert.False(result.Ok);
        Assert.Contains("tcpHost", result.Message);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
    }

    [Fact]
    public async Task ApplyAsync_windows_driver_without_printer_should_fail()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.WindowsDriver, PrinterName = "" }, testOnly: false);
        Assert.False(result.Ok);
        Assert.Contains("printerName", result.Message);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
    }

    [Fact]
    public async Task ApplyAsync_log_should_succeed_and_persist()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Log }, testOnly: false);

        Assert.True(result.Ok);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
        Assert.True(File.Exists(path));
        Assert.Contains("Log", File.ReadAllText(path));
    }

    [Fact]
    public async Task ApplyAsync_tcp_unreachable_should_fail_and_keep_log()
    {
        var (manager, path) = Create();
        // 127.0.0.1:1 通常立即拒绝连接；失败应保持原连接（Log）且不持久化
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Tcp, TcpHost = "127.0.0.1", TcpPort = 1 }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TestOnly_should_not_switch_or_persist()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Log }, testOnly: true);

        Assert.True(result.Ok);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Startup_should_prefer_persisted_connection()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Mode":"Tcp","TcpHost":"192.168.1.50","TcpPort":9100}""");
        try
        {
            var (manager, _) = Create(path, defaultMode: TransportMode.Log);
            Assert.Equal(TransportMode.Tcp, manager.CurrentConfig.Mode);
            Assert.Equal("192.168.1.50", manager.CurrentConfig.TcpHost);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Tcp_connection_test_should_detect_listener_open_and_closed()
    {
        // 本地监听：开启时连接成功返回 true；关闭后连接拒绝返回 false（两向判定稳定）
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var open = await new Tcp9100PrintTransport("127.0.0.1", port).TestConnectionAsync();
            Assert.True(open);
        }
        finally
        {
            listener.Stop();
        }

        var closed = await new Tcp9100PrintTransport("127.0.0.1", port).TestConnectionAsync();
        Assert.False(closed);
    }
}