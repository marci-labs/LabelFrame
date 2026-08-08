namespace LabelFrame.Core.Transport;

/// <summary>
/// 日志传输（模拟打印机）：把收到的指令写入 <see cref="TextWriter"/>，
/// 用于没有真实打印机时的联调验证。
/// </summary>
public sealed class LogPrintTransport : IPrintTransport
{
    private readonly TextWriter _writer;

    /// <summary>创建日志传输。</summary>
    /// <param name="writer">指令输出目标。</param>
    public LogPrintTransport(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        _writer.WriteLine("=== LabelFrame 模拟打印机 ===");
        _writer.WriteLine(command);
        _writer.WriteLine("=== 输出结束 ===");
        return Task.CompletedTask;
    }
}