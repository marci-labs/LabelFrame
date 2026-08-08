using System.Net.Sockets;
using System.Text;

namespace LabelFrame.Core.Transport;

/// <summary>
/// TCP 9100 打印传输：连接打印机 IP 的 9100 端口并发送指令（Zebra 等网络打印机）。
/// </summary>
public sealed class Tcp9100PrintTransport : IPrintTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;

    /// <summary>创建 TCP 9100 传输。</summary>
    /// <param name="host">打印机主机 / IP。</param>
    /// <param name="port">端口（默认 9100）。</param>
    /// <param name="timeout">连接与发送超时（默认 10 秒）。</param>
    public Tcp9100PrintTransport(string host, int port = 9100, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1-65535 之间。");
        }

        _host = host;
        _port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc />
    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(_host, _port, timeoutCts.Token);
            var payload = System.Text.Encoding.UTF8.GetBytes(command);
            await using var stream = client.GetStream();
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"连接或发送打印机超时（{_host}:{_port}）。");
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"连接打印机失败（{_host}:{_port}）：{ex.Message}", ex);
        }
    }
}