using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Sample;

/// <summary>示例传输插件（测试用）：模拟外部厂商插件接入——发送后记录到上下文日志，状态恒在线，连接测试恒成功。</summary>
public sealed class SampleTransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "sample";

    /// <inheritdoc />
    public string DisplayName => "示例插件（测试）";

    /// <inheritdoc />
    public string Description => "外部 DLL 目录加载示例：发送记录到宿主日志，连接测试恒成功（迭代 22 插件机制验证）。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("prefix", "日志前缀", TransportParameterType.String, Required: true, DefaultValue: "SAMPLE"),
        new TransportParameterSpec("count", "模拟次数", TransportParameterType.Int, DefaultValue: "1"),
        new TransportParameterSpec("enabled", "是否启用", TransportParameterType.Bool, DefaultValue: "true"),
        new TransportParameterSpec("kind", "模式", TransportParameterType.Select, DefaultValue: "A", Options: new[]
        {
            new TransportParameterOption("A", "模式 A"),
            new TransportParameterOption("B", "模式 B"),
        }),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters) => $"SAMPLE({parameters.GetString("prefix", "SAMPLE")})";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new SamplePrintTransport(context, parameters.GetString("prefix", "SAMPLE"));
}

/// <summary>示例传输：发送写一行到宿主日志；连接测试恒成功；状态恒在线。</summary>
public sealed class SamplePrintTransport : IPrintTransport, IPrinterStatusProvider, ITestableTransport
{
    private readonly ITransportPluginContext _context;
    private readonly string _prefix;

    public SamplePrintTransport(ITransportPluginContext context, string prefix)
    {
        _context = context;
        _prefix = prefix;
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        _context.HostLog.WriteLine($"[SAMPLE] {_prefix} 收到 {command.Length} 字节。");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PrinterStatusInfo(true, false, false, "示例插件在线。"));
}
