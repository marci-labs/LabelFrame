namespace LabelFrame.Core.Transport;

/// <summary>打印传输：把打印机指令送到打印机。</summary>
public interface IPrintTransport
{
    /// <summary>发送打印机指令。</summary>
    /// <param name="command">打印机指令（如 ZPL）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendAsync(string command, CancellationToken cancellationToken = default);
}