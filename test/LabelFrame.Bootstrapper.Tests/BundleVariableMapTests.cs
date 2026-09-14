using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>Burn 变量契约（DESIGN §6.3，决策 #122）：问卷解析结果 → Bundle 链条件变量（BA 写入引擎、Chain 条件消费）。</summary>
public sealed class BundleVariableMapTests
{
    private static InstallManifest LoadManifest(string fileName) =>
        InstallManifest.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", fileName)));

    private static TopologyResolver Resolver { get; } = new();

    [Fact]
    public void To_variables_standalone_with_webui_and_zebra_should_set_all_flags()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(["zebra"], StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("standalone", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void To_variables_standalone_default_should_clear_switch_flags()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: false));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("0", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void To_variables_client_preset_should_set_client_only()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.Client,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("client", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        // webui 未标记 client 预设：开关开着也不纳入（topologies 过滤传导到变量）
        Assert.Equal("0", variables[BundleVariableMap.WebUiVariable]);
    }

    [Fact]
    public void To_variables_server_win_should_set_server_and_not_client()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.ServerWin,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("server-win", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
    }

    [Fact]
    public void To_variables_server_docker_should_clear_all_package_flags()
    {
        // Docker 预设无下载组件：全部包变量清零，仅保留预设名（compose 指引由确认页展示）
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.ServerDocker,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("server-docker", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("0", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void To_variables_current_manifest_without_plugin_entry_should_clear_zebra_flag()
    {
        // 迭代 58 产物现状（无 plugin-zebra 条目）：勾选 Zebra 也不置位（选项来源仅为已有条目）
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.current.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(["zebra"], StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ZebraPluginVariable]);
    }
}
