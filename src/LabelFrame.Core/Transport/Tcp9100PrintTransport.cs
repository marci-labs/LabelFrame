using System.Net.Sockets;
using System.Text;

namespace LabelFrame.Core.Transport;

/// <summary>
/// TCP 9100 打印传输：连接打印机 IP 的 9100 端口并发送指令（Zebra 等网络打印机）。
/// 状态查询：发送 ~HS 主机状态查询并做基础解析。
/// </summary>
public sealed class Tcp9100PrintTransport : IPrintTransport, IPrinterStatusProvider
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

    /// <summary>连接测试：尝试 TCP 连接（3 秒超时），成功返回 true。</summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(_host, _port, timeoutCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Zebra ~HS 主机状态响应为逗号分隔字段：第 2 个字段为暂停位，第 5 个字段为缺纸位；
    /// 字段映射以真实设备联调为准（见 docs/DESIGN.md §5）。
    /// </remarks>
    public async Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(_host, _port, timeoutCts.Token);
            await using var stream = client.GetStream();
            var payload = System.Text.Encoding.UTF8.GetBytes("~HS");
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var buffer = new byte[1024];
            var response = new StringBuilder();
            while (response.Length < 1024)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
                if (read == 0)
                {
                    break;
                }

                response.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                if (response.ToString().Contains('\n'))
                {
                    break;
                }
            }

            var text = response.ToString().Trim();
            if (text.Length == 0)
            {
                return new PrinterStatusInfo(true, IsPaperOut: false, IsPaused: false, "已连接，但状态无响应。");
            }

            var fields = text.Split(',');
            var paperOut = fields.Length > 4 && fields[4] == "1";
            var paused = fields.Length > 1 && fields[1] == "1";
            return new PrinterStatusInfo(true, paperOut, paused, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PrinterStatusInfo(true, IsPaperOut: false, IsPaused: false, "状态查询超时。");
        }
        catch (SocketException ex)
        {
            return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"连接失败：{ex.Message}");
        }
    }
}