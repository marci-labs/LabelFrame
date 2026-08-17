namespace LabelFrame.Core.Transport.Plugins;

/// <summary>传输插件上下文默认实现：宿主日志写入器 + 数据目录。</summary>
public sealed class TransportPluginContext : ITransportPluginContext
{
    /// <summary>创建上下文。</summary>
    /// <param name="hostLog">宿主日志写入器（不可为 null）。</param>
    /// <param name="dataDirectory">宿主数据目录（%LOCALAPPDATA%\LabelFrame）。</param>
    public TransportPluginContext(TextWriter hostLog, string dataDirectory)
    {
        HostLog = hostLog ?? throw new ArgumentNullException(nameof(hostLog));
        DataDirectory = dataDirectory;
    }

    /// <inheritdoc />
    public TextWriter HostLog { get; }

    /// <inheritdoc />
    public string DataDirectory { get; }
}
