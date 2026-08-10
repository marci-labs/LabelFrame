using LabelFrame.Core.Transport;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;
using Zebra.Sdk.Printer.Discovery;

namespace LabelFrame.WinHost.Transport;

/// <summary>Zebra SDK 连接类型。</summary>
public enum ZebraTransportKind
{
    /// <summary>TCP/IP 网络打印机（默认 9100）。</summary>
    Tcp,

    /// <summary>USB 直连（ZebraUsbName 为空时自动发现第一台）。</summary>
    Usb,

    /// <summary>Windows 驱动（按打印机名）。</summary>
    Driver,
}

/// <summary>
/// Zebra 官方 Link-OS SDK 传输：统一处理 TCP / USB / Windows 驱动连接，
/// 发送 ZPL 指令；异常统一转换为中文 InvalidOperationException。
/// </summary>
public sealed class ZebraPrinterTransport : IPrintTransport, IPrinterStatusProvider
{
    private readonly ZebraTransportKind _kind;
    private readonly string _address;
    private readonly int _port;
    private readonly string _printerName;
    private readonly string _usbName;

    /// <summary>创建 Zebra SDK 传输。</summary>
    public ZebraPrinterTransport(
        ZebraTransportKind kind,
        string? address = null,
        int port = 9100,
        string? printerName = null,
        string? usbName = null)
    {
        _kind = kind;
        _address = address ?? string.Empty;
        _port = port;
        _printerName = printerName ?? string.Empty;
        _usbName = usbName ?? string.Empty;

        if (kind == ZebraTransportKind.Tcp && string.IsNullOrWhiteSpace(_address))
        {
            throw new ArgumentException("TCP 模式必须指定打印机地址（LABELFRAME_TCP_HOST）。", nameof(address));
        }

        if (kind == ZebraTransportKind.Driver && string.IsNullOrWhiteSpace(_printerName))
        {
            throw new ArgumentException("驱动模式必须指定打印机名（LABELFRAME_PRINTER）。", nameof(printerName));
        }
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => SendCore(command), cancellationToken);
    }

    private void SendCore(string command)
    {
        Connection? connection = null;
        try
        {
            connection = CreateConnection();
            connection.Open();
            connection.Write(System.Text.Encoding.UTF8.GetBytes(command));
        }
        catch (ConnectionException ex)
        {
            throw new InvalidOperationException($"Zebra 打印机连接失败（{DescribeTarget()}）：{ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Zebra 打印机发送失败（{DescribeTarget()}）：{ex.Message}", ex);
        }
        finally
        {
            if (connection is not null)
            {
                try
                {
                    connection.Close();
                }
                catch
                {
                    // 关闭失败不影响发送结果
                }
            }
        }
    }

    private Connection CreateConnection() => _kind switch
    {
        ZebraTransportKind.Tcp => new TcpConnection(_address, _port),
        ZebraTransportKind.Usb => string.IsNullOrWhiteSpace(_usbName)
            ? DiscoverUsbConnection()
            : new UsbConnection(_usbName),
        ZebraTransportKind.Driver => new DriverPrinterConnection(_printerName),
        _ => throw new InvalidOperationException($"不支持的 Zebra 连接类型：{_kind}。"),
    };

    private static Connection DiscoverUsbConnection()
    {
        var printers = UsbDiscoverer.GetZebraUsbPrinters();
        if (printers is null || printers.Count == 0)
        {
            throw new InvalidOperationException("未发现 Zebra USB 打印机，请检查连接或配置 ZebraUsbName。");
        }

        return printers[0].GetConnection();
    }

    /// <summary>连接测试：建立连接后关闭（SDK 统一处理 TCP / USB / 驱动），用于连接管理「先测试后生效」。</summary>
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            Connection? connection = null;
            try
            {
                connection = CreateConnection();
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (connection is not null)
                {
                    try
                    {
                        connection.Close();
                    }
                    catch
                    {
                        // 忽略关闭异常
                    }
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>通过 Zebra 官方 SDK 查询打印机状态（缺纸 / 暂停 / 就绪）。</remarks>
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => GetStatusCore(), cancellationToken);
    }

    private PrinterStatusInfo GetStatusCore()
    {
        Connection? connection = null;
        try
        {
            connection = CreateConnection();
            connection.Open();
            // 3.x 的 PrinterStatus 无公开状态字段；详细状态（缺纸/暂停）待真实设备联调
            // （可用 ~HS 或 SGD 命令扩展，见 docs/DESIGN.md §5）
            return new PrinterStatusInfo(true, IsPaperOut: false, IsPaused: false, "已连接（详细状态待真实设备联调）。");
        }
        catch (ConnectionException ex)
        {
            return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"连接失败：{ex.Message}");
        }
        finally
        {
            if (connection is not null)
            {
                try
                {
                    connection.Close();
                }
                catch
                {
                    // 忽略关闭异常
                }
            }
        }
    }

    private string DescribeTarget() => _kind switch
    {
        ZebraTransportKind.Tcp => $"{_address}:{_port}",
        ZebraTransportKind.Usb => string.IsNullOrWhiteSpace(_usbName) ? "USB（自动发现）" : $"USB:{_usbName}",
        ZebraTransportKind.Driver => $"驱动:{_printerName}",
        _ => _kind.ToString(),
    };
}