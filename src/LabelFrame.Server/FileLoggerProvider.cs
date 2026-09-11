using Microsoft.Extensions.Logging;

namespace LabelFrame.Server;

/// <summary>
/// 极简文件日志（决策 #108）：按日轮转的 UTF-8 文本文件 + 超期清理，
/// 供 Linux 部署把日志目录挂载到宿主机直接查看。`LABELFRAME_SERVER_LOG_FILE` 为基准路径，
/// 实际写入 <名>-&lt;yyyyMMdd&gt;.log（与客户端 app-*.log 口径一致）。
/// 启动防护：路径无效（目录不存在且不可创建 / 磁盘不可用）时构造**不抛异常**——
/// <see cref="FileChannelEnabled"/> 为 false，由调用方输出中文告警后跳过文件通道，宿主正常启动；
/// 运行中防护：每日轮转开新文件失败继续写旧文件（下次写入再试），实际写入失败自我禁用并告警一次，
/// <see cref="ILogger.Log{TState}"/> 永不抛出（不打断请求路径）。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _basePath;
    private readonly int _retainedFileCountLimit;
    private readonly object _gate = new();
    private TextWriter? _writer;
    private DateOnly _currentDate;
    private bool _fileChannelEnabled;

    /// <summary>文件通道是否可用（构造成功为 true；运行中写入失败后自我禁用为 false）。</summary>
    public bool FileChannelEnabled
    {
        get { lock (_gate) return _fileChannelEnabled; }
    }

    /// <summary>文件通道不可用原因（构造失败时供调用方输出告警；通道可用为 null）。</summary>
    public string? InactiveReason { get; }

    /// <summary>创建文件日志提供器。</summary>
    /// <param name="path">日志基准路径（如 .../server.log；实际写入 server-yyyyMMdd.log）。</param>
    /// <param name="retainedFileCountLimit">按日文件保留上限（默认 31；0 或负值 = 不清理）。</param>
    public FileLoggerProvider(string path, int retainedFileCountLimit = 31)
    {
        _basePath = path;
        _retainedFileCountLimit = retainedFileCountLimit;
        try
        {
            var today = Today();
            _writer = OpenWriter(today);
            _currentDate = today;
            _fileChannelEnabled = true;
            CleanupOverRetained();
        }
        catch (Exception ex)
        {
            // 启动防护：目录不存在且不可创建 / 磁盘不可用等——记录原因、不抛异常，
            // 由调用方告警后跳过文件通道（服务正常启动）。
            InactiveReason = ex.Message;
            _fileChannelEnabled = false;
            _writer = null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _fileChannelEnabled = false;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>当日文件路径：<名>-yyyyMMdd<扩展名>（基准路径 server.log → server-20260911.log）。</summary>
    private string DatedPath(DateOnly date)
    {
        var directory = Path.GetDirectoryName(_basePath);
        var fileName = $"{Path.GetFileNameWithoutExtension(_basePath)}-{date:yyyyMMdd}{Path.GetExtension(_basePath)}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static TextWriter OpenWriter(DateOnly date, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return TextWriter.Synchronized(new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        });
    }

    private TextWriter OpenWriter(DateOnly date) => OpenWriter(date, DatedPath(date));

    /// <summary>日志行写入入口：永不抛出。轮转失败退回旧文件继续写；写入失败自我禁用并告警一次。</summary>
    private void WriteLine(string line)
    {
        lock (_gate)
        {
            if (!_fileChannelEnabled)
            {
                return;
            }

            try
            {
                TryRotate();
                _writer!.WriteLine(line);
            }
            catch (Exception ex)
            {
                DisableFileChannel($"写入日志文件失败：{ex.Message}");
            }
        }
    }

    /// <summary>按日轮转：跨天时开新文件、关旧文件并清理超期。失败不抛出（退回旧文件，下次写入再试）。</summary>
    private void TryRotate()
    {
        var today = Today();
        if (today == _currentDate)
        {
            return;
        }

        var newWriter = OpenWriter(today);
        var oldWriter = _writer;
        _writer = newWriter;
        _currentDate = today;
        try
        {
            oldWriter?.Dispose();
        }
        catch (IOException)
        {
            // 旧文件关闭失败不影响新文件写入
        }

        CleanupOverRetained();
    }

    /// <summary>清理超期文件：匹配 <名>-yyyyMMdd<扩展名> 且日期可解析者，按文件名序（即时间序）删最旧，保留最近 limit 个（含当日）。</summary>
    private void CleanupOverRetained()
    {
        if (_retainedFileCountLimit <= 0)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_basePath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            var prefix = $"{Path.GetFileNameWithoutExtension(_basePath)}-";
            var extension = Path.GetExtension(_basePath);
            var files = Directory.GetFiles(directory, $"{prefix}*{extension}")
                .Where(file =>
                {
                    var datePart = Path.GetFileNameWithoutExtension(file)[prefix.Length..];
                    return datePart.Length == 8 && DateOnly.TryParseExact(datePart, "yyyyMMdd", out _);
                })
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();
            var excess = files.Count - _retainedFileCountLimit;
            for (var i = 0; i < excess; i++)
            {
                File.Delete(files[i]);
            }
        }
        catch (Exception)
        {
            // 清理失败（文件被占用等）不影响写入，下次轮转再试
        }
    }

    /// <summary>运行中自我禁用：关闭文件、控制台告警一次（后续日志仅控制台 / 其他通道）。</summary>
    private void DisableFileChannel(string reason)
    {
        _fileChannelEnabled = false;
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // 关闭失败即可，通道已禁用
        }

        _writer = null;
        try
        {
            Console.Error.WriteLine(
                $"[LabelFrame] 警告：日志文件通道已停用（{_basePath}）：{reason}。后续日志仅输出到控制台，服务继续运行；请检查 LABELFRAME_SERVER_LOG_FILE 配置与磁盘状态。");
        }
        catch (Exception)
        {
            // 无控制台（Windows 服务）时告警无处展示，忽略
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
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

            provider.WriteLine(line);
        }
    }
}
