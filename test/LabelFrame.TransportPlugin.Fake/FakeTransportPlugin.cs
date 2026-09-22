using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Fake;

/// <summary>
/// fake 传输插件（测试 / PDA Spike 用，迭代 96 / #185）：纯托管轻量档插件——发送内容记录到宿主日志与
/// 数据目录落盘文件（<c>fake-transport-sent.txt</c>），连接测试恒成功、状态恒在线；
/// 用于验证外置插件通道「加载 → 发现 → 配置路由 → 测试打印」完整链路（无需真实打印机）。
/// </summary>
public sealed class FakeTransportPlugin : ITransportPlugin
{
    /// <summary>发送内容落盘文件名（插件数据目录内；PDA 走查经 adb run-as / pull 取证）。</summary>
    public const string SinkFileName = "fake-transport-sent.txt";

    /// <inheritdoc />
    public string Id => "labelframe-transport-fake";

    /// <inheritdoc />
    public string DisplayName => "假想品牌（通道验证）";

    /// <inheritdoc />
    public string Description => "外置插件通道验证用假想品牌：发送内容记录到日志与数据目录，不连接真实打印机。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("host", "打印机 IP 地址", TransportParameterType.String, Required: true, DefaultValue: "127.0.0.1", Hint: "fake 传输不实际连接，仅参与配置与路由"),
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100"),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters)
        => $"假想品牌 {parameters.GetString("host", "127.0.0.1")}:{parameters.GetInt("port", 9100)}";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new FakePrintTransport(
            context,
            parameters.GetString("host", "127.0.0.1"),
            parameters.GetInt("port", 9100));
}

/// <summary>fake 传输：发送写入宿主日志 + 数据目录落盘；连接测试恒成功；状态恒在线。</summary>
public sealed class FakePrintTransport : IPrintTransport, IPrinterStatusProvider, ITestableTransport
{
    private readonly ITransportPluginContext _context;
    private readonly string _host;
    private readonly int _port;

    /// <summary>创建 fake 传输。</summary>
    public FakePrintTransport(ITransportPluginContext context, string host, int port)
    {
        _context = context;
        _host = host;
        _port = port;
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 落盘留痕（走查取证）：一条发送一行「时间 + 字节数 + 内容」；写失败不影响链路（日志仍有记录）
            Directory.CreateDirectory(_context.DataDirectory);
            File.AppendAllText(
                Path.Combine(_context.DataDirectory, FakeTransportPlugin.SinkFileName),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{System.Text.Encoding.UTF8.GetByteCount(command)}B\t{command.ReplaceLineEndings(" ")}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _context.HostLog.WriteLine($"[FAKE] 发送内容落盘失败（不影响模拟结果）：{ex.Message}");
        }

        _context.HostLog.WriteLine($"[FAKE] 模拟发送到 {_host}:{_port}：{command.Length} 字符。");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PrinterStatusInfo(true, false, false, "假想品牌通道正常（未连接真实打印机）。"));
}
