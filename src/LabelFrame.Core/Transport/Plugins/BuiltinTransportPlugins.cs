using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 内置传输插件：Core 提供 Log（模拟）与 TCP 9100（跨平台网络打印机）；
/// Windows 驱动（winspool）与 Zebra SDK 由 WinHost 以同一接口注册。
/// </summary>
public static class BuiltinTransportPlugins
{
    /// <summary>Core 内置插件集合。</summary>
    public static IReadOnlyList<ITransportPlugin> CreateCorePlugins()
        => [new LogTransportPlugin(), new Tcp9100TransportPlugin()];
}

/// <summary>Log 模拟传输插件（默认 / 联调）。</summary>
public sealed class LogTransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "log";

    /// <inheritdoc />
    public string DisplayName => "Log（模拟打印）";

    /// <inheritdoc />
    public string Description => "日志模拟传输：不连接打印机，作业按成功处理（无真实打印机时联调用）。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => [];

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters) => "LOG";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new LogPrintTransport(context.HostLog);
}

/// <summary>TCP 9100 网络打印机传输插件。</summary>
public sealed class Tcp9100TransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "tcp9100";

    /// <inheritdoc />
    public string DisplayName => "TCP 9100";

    /// <inheritdoc />
    public string Description => "TCP 9100 网络打印机（Zebra 等）：连接打印机 IP 的 9100 端口发送指令，状态查询用 ~HS。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, Required: true, Hint: "如 192.168.1.50"),
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100", Hint: "默认 9100"),
        new TransportParameterSpec("timeoutSeconds", "超时（秒）", TransportParameterType.Int, DefaultValue: "10"),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters p) => $"TCP {p.GetString("host", "?")}:{p.GetInt("port", 9100)}";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters p, ITransportPluginContext context)
        => new Tcp9100PrintTransport(
            p.GetString("host") ?? throw new ArgumentException("缺少打印机地址（host）。"),
            p.GetInt("port", 9100),
            TimeSpan.FromSeconds(p.GetInt("timeoutSeconds", 10)));
}
