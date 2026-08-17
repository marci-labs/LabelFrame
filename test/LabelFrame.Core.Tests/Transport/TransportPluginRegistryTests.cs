using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

public class TransportPluginRegistryTests
{
    private static (TransportPluginRegistry Registry, TransportPluginContext Context) Create()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        return (registry, new TransportPluginContext(TextWriter.Null, Path.GetTempPath()));
    }

    [Fact]
    public void ListPlugins_should_include_builtin_log_and_tcp9100()
    {
        var (registry, _) = Create();

        var ids = registry.ListPlugins().Select(p => p.Id).ToList();

        Assert.Contains("log", ids);
        Assert.Contains("tcp9100", ids);
        var tcp = Assert.Single(registry.ListPlugins().Where(p => p.Id == "tcp9100"));
        Assert.Equal("TCP 9100", tcp.DisplayName);
        Assert.Contains(tcp.Parameters, p => p.Key == "host" && p.Required);
        Assert.Contains(tcp.Parameters, p => p.Key == "port" && p.Type == TransportParameterType.Int);
    }

    [Fact]
    public void GetPlugin_should_be_case_insensitive()
    {
        var (registry, _) = Create();
        Assert.NotNull(registry.GetPlugin("TCP9100"));
        Assert.Null(registry.GetPlugin("missing"));
    }

    [Fact]
    public void CreateTransport_log_should_use_host_log_writer()
    {
        var (registry, context) = Create();
        var transport = registry.CreateTransport("log", new TransportPluginParameters(), context);
        Assert.IsType<LogPrintTransport>(transport);
    }

    [Fact]
    public void CreateTransport_tcp9100_should_build_with_params()
    {
        var (registry, context) = Create();
        var transport = registry.CreateTransport(
            "tcp9100",
            new TransportPluginParameters(new Dictionary<string, string> { ["host"] = "192.168.1.50", ["port"] = "9100" }),
            context);
        Assert.IsType<Tcp9100PrintTransport>(transport);
    }

    [Fact]
    public void CreateTransport_missing_plugin_should_throw()
    {
        var (registry, context) = Create();
        Assert.Throws<InvalidOperationException>(() => registry.CreateTransport("nope", new TransportPluginParameters(), context));
    }

    [Fact]
    public void Describe_should_use_plugin_describe()
    {
        var (registry, _) = Create();
        Assert.Equal("LOG", registry.Describe("log", new TransportPluginParameters()));
        Assert.Equal(
            "TCP 192.168.1.50:9100",
            registry.Describe("tcp9100", new TransportPluginParameters(new Dictionary<string, string> { ["host"] = "192.168.1.50", ["port"] = "9100" })));
        Assert.Equal("nope", registry.Describe("nope", new TransportPluginParameters()));
    }

    [Fact]
    public void Duplicate_register_should_override()
    {
        var (registry, context) = Create();
        registry.Register(new SampleOverridePlugin());
            var plugin = registry.GetPlugin("log");
        Assert.Equal("覆盖", registry.GetPlugin("log")!.DisplayName);
    }

    private sealed class SampleOverridePlugin : ITransportPlugin
    {
        public string Id => "log";
        public string DisplayName => "覆盖";
        public string Description => string.Empty;
        public IReadOnlyList<TransportParameterSpec> Parameters => [];
        public string Describe(TransportPluginParameters parameters) => "OVERRIDE";
        public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
            => new LogPrintTransport(context.HostLog);
    }
}
