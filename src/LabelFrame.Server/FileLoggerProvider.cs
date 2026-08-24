using Microsoft.Extensions.Logging;

namespace LabelFrame.Server;

/// <summary>
/// 极简文件日志：把 ILogger 输出追加写入 UTF-8 文本文件，
/// 供 Linux 部署把日志目录挂载到宿主机直接查看（LABELFRAME_SERVER_LOG_FILE）。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = TextWriter.Synchronized(new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        });
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_writer, _gate, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private sealed class FileLogger(TextWriter writer, object gate, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel.ToString().ToUpperInvariant()}] [{category}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (gate)
            {
                writer.WriteLine(line);
            }
        }
    }
}
