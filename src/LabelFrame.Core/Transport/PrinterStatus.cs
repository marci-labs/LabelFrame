namespace LabelFrame.Core.Transport;

/// <summary>打印机状态信息。</summary>
public sealed record PrinterStatusInfo(bool IsOnline, bool IsPaperOut, bool IsPaused, string? Message);

/// <summary>打印机状态查询接口（测试页 / 在线状态）。</summary>
public interface IPrinterStatusProvider
{
    /// <summary>查询打印机状态。</summary>
    Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default);
}