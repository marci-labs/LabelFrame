using System.ComponentModel;
using System.Runtime.InteropServices;
using LabelFrame.Core.Transport;

namespace LabelFrame.WinHost.Transport;

/// <summary>
/// Windows 驱动（USB / 已安装打印机）raw 传输：通过 winspool 以 RAW 数据类型
/// 把 ZPL 指令直接发给打印机驱动，不经过打印预览。
/// </summary>
public sealed class RawPrinterTransport : IPrintTransport, IPrinterStatusProvider, LabelFrame.Core.Transport.Plugins.ITestableTransport
{
    private readonly string _printerName;

    /// <summary>创建 Windows 驱动传输。</summary>
    /// <param name="printerName">Windows 打印机名。</param>
    public RawPrinterTransport(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new ArgumentException("必须指定 Windows 打印机名（LABELFRAME_PRINTER）。", nameof(printerName));
        }

        _printerName = printerName;
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        SendCore(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(TestConnection()
            ? null
            : "连接测试失败：无法打开打印机（请检查打印机名是否与系统一致、驱动是否已安装）。");

    /// <summary>连接测试：按名打开打印机（成功即关闭），用于连接管理「先测试后生效」。</summary>
    public bool TestConnection()
    {
        if (!OpenPrinter(_printerName, out var printer, IntPtr.Zero))
        {
            return false;
        }

        ClosePrinter(printer);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>winspool raw 传输无法读回打印机状态，统一按在线处理。</remarks>
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PrinterStatusInfo(true, IsPaperOut: false, IsPaused: false, "驱动模式无法读回打印机状态（默认在线）。"));

    private void SendCore(string command)
    {
        if (!OpenPrinter(_printerName, out var printer, IntPtr.Zero))
        {
            throw CreateException($"无法打开打印机「{_printerName}」。");
        }

        try
        {
            var docInfo = new DOC_INFO_1
            {
                pDocName = "LabelFrame",
                pOutputFile = null,
                pDatatype = "RAW",
            };

            if (!StartDocPrinter(printer, 1, ref docInfo))
            {
                throw CreateException($"开始打印文档失败（打印机「{_printerName}」）。");
            }

            try
            {
                if (!StartPagePrinter(printer))
                {
                    throw CreateException($"开始打印页失败（打印机「{_printerName}」）。");
                }

                try
                {
                    var payload = System.Text.Encoding.UTF8.GetBytes(command);
                    if (!WritePrinter(printer, payload, payload.Length, out var written) || written != payload.Length)
                    {
                        throw CreateException($"写入打印机失败（打印机「{_printerName}」，已写入 {written}/{payload.Length} 字节）。");
                    }
                }
                finally
                {
                    EndPagePrinter(printer);
                }
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    private static InvalidOperationException CreateException(string message)
    {
        var win32Error = Marshal.GetLastWin32Error();
        var detail = new Win32Exception(win32Error).Message;
        return new InvalidOperationException($"{message} 系统错误码 {win32Error}：{detail}");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDatatype;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int pcWritten);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
}
