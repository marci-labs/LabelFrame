using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Topology;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>「预设 + 开关 → 组件集合」全矩阵（AC-02：对照 DESIGN §6.3 映射表；含 AC-01 场景）。</summary>
public sealed class TopologyResolverTests
{
    private readonly TopologyResolver _resolver = new();

    private static InstallManifest LoadManifest(string fileName) =>
        InstallManifest.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", fileName)));

    private static InstallManifest Current => LoadManifest("install-manifest.current.json");

    private static InstallManifest Full => LoadManifest("install-manifest.full.json");

    private static TopologyOptions Options(IEnumerable<string>? brands = null, bool webUi = false) =>
        new(brands?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal), webUi);

    private static void AssertIds(TopologyPlan plan, params string[] expected) =>
        Assert.Equal(expected.ToList(), plan.Components.Select(item => item.Component.Id).ToList());

    // ---- AC-01：当前清单形态（迭代 63 起产物：plugin-zebra 官方插件条目已收录）----

    [Fact]
    public void Resolve_current_manifest_standalone_zebra_webui_should_be_server_client_webui_plugin()
    {
        // AC-01：单机一体 + Zebra + 带管理界面 → Server MSI + Client MSI + 管理界面 zip + Zebra 官方插件
        var plan = _resolver.Resolve(Current, TopologyPreset.Standalone, Options(["zebra"], webUi: true));

        AssertIds(plan, "server-msi", "client-msi", "webui", "plugin-zebra");
        Assert.Equal(11534336L + 12582912L + 4194304L + 9437184L, plan.TotalSizeBytes);
        Assert.Null(plan.DockerComposeGuidance);
    }

    [Fact]
    public void Resolve_pre_plugin_manifest_zebra_selected_should_not_add_component()
    {
        // 存量 Release 清单（≤0.26 无 plugin 条目）：勾选 Zebra 不额外添加组件（品牌页无可选项）
        var plan = _resolver.Resolve(
            LoadManifest("install-manifest.pre-plugin.json"),
            TopologyPreset.Standalone,
            Options(["zebra"], webUi: false));

        AssertIds(plan, "server-msi", "client-msi");
    }

    // ---- AC-02：全矩阵（full fixture：含 runtime 条目 + dependsOn 闭包 + plugin-zebra 条目）----

    [Fact]
    public void Resolve_standalone_default_should_include_core_with_dependency_closure()
    {
        // 单机一体核心：runtime-desktop、runtime-webview2、server-msi、client-msi；依赖闭包按依赖序排列
        var plan = _resolver.Resolve(Full, TopologyPreset.Standalone, Options());

        AssertIds(plan, "runtime-desktop", "server-msi", "runtime-webview2", "client-msi");
        Assert.Equal(58396456L + 11534336L + 22020096L + 12582912L, plan.TotalSizeBytes);
    }

    [Fact]
    public void Resolve_standalone_webui_zebra_should_add_switch_components()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.Standalone, Options(["zebra"], webUi: true));

        AssertIds(plan, "webui", "runtime-desktop", "server-msi", "runtime-webview2", "client-msi", "plugin-zebra");
        // 依赖序不变式：runtime-desktop 在 server-msi / client-msi 之前，runtime-webview2 在 client-msi 之前
        var ids = plan.Components.Select(item => item.Component.Id).ToList();
        Assert.True(ids.IndexOf("runtime-desktop") < ids.IndexOf("server-msi"));
        Assert.True(ids.IndexOf("runtime-desktop") < ids.IndexOf("client-msi"));
        Assert.True(ids.IndexOf("runtime-webview2") < ids.IndexOf("client-msi"));
    }

    [Fact]
    public void Resolve_server_win_default_should_be_runtime_and_server_msi()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.ServerWin, Options());

        AssertIds(plan, "runtime-desktop", "server-msi");
    }

    [Fact]
    public void Resolve_server_win_webui_should_prepend_webui()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.ServerWin, Options(webUi: true));

        AssertIds(plan, "webui", "runtime-desktop", "server-msi");
        var webUi = Assert.Single(plan.Components, item => item.Component.Id == "webui");
        Assert.Contains("plugins", webUi.InstallTarget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LabelFrame", webUi.InstallTarget, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_server_docker_should_be_empty_with_compose_guidance()
    {
        // Docker 预设：无下载组件（镜像完整性由 registry digest 保证），空集合 + compose 产物描述
        var planOff = _resolver.Resolve(Full, TopologyPreset.ServerDocker, Options());
        var planOn = _resolver.Resolve(Full, TopologyPreset.ServerDocker, Options(webUi: true));

        Assert.Empty(planOff.Components);
        Assert.Equal(0, planOff.TotalSizeBytes);
        Assert.NotNull(planOff.DockerComposeGuidance);
        Assert.Contains("docker-compose", planOff.DockerComposeGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ghcr.io/marci-labs/labelframe-server", planOff.DockerComposeGuidance, StringComparison.Ordinal);
        Assert.NotEqual(planOff.DockerComposeGuidance, planOn.DockerComposeGuidance);
        Assert.Contains("管理界面", planOn.DockerComposeGuidance, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_server_linux_default_should_be_linux_archive_only()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.ServerLinux, Options());

        AssertIds(plan, "linux-server");
        var archive = Assert.Single(plan.Components);
        Assert.Contains("/opt/labelframe/server", archive.InstallTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_server_linux_webui_should_target_linux_plugin_dir()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.ServerLinux, Options(webUi: true));

        AssertIds(plan, "webui", "linux-server");
        var webUi = Assert.Single(plan.Components, item => item.Component.Id == "webui");
        Assert.Contains("/var/lib/labelframe/server/plugins/web-ui", webUi.InstallTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_client_default_should_include_runtimes_and_client_msi()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.Client, Options());

        AssertIds(plan, "runtime-desktop", "runtime-webview2", "client-msi");
    }

    [Fact]
    public void Resolve_client_webui_switch_should_be_filtered_by_topologies()
    {
        // webui 未标记 client 预设：开关打开也不纳入（开关组件仍受 topologies 过滤）
        var plan = _resolver.Resolve(Full, TopologyPreset.Client, Options(webUi: true));

        AssertIds(plan, "runtime-desktop", "runtime-webview2", "client-msi");
    }

    [Fact]
    public void Resolve_client_zebra_should_append_brand_plugin()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.Client, Options(["zebra"]));

        AssertIds(plan, "runtime-desktop", "runtime-webview2", "client-msi", "plugin-zebra");
        var plugin = Assert.Single(plan.Components, item => item.Component.Id == "plugin-zebra");
        Assert.Contains("plugins", plugin.InstallTarget, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_unknown_brand_should_not_add_anything()
    {
        // 品牌选项来源仅为 manifest 已有条目：清单没有的品牌勾选无效
        var plan = _resolver.Resolve(Full, TopologyPreset.Client, Options(["honeywell", "tsc"]));

        AssertIds(plan, "runtime-desktop", "runtime-webview2", "client-msi");
    }

    // ---- 安装位置（对齐 DEPLOY 目录约定）----

    [Fact]
    public void Resolve_install_targets_should_align_with_deploy_conventions()
    {
        var plan = _resolver.Resolve(Full, TopologyPreset.Standalone, Options());

        Assert.All(plan.Components, item => Assert.False(string.IsNullOrWhiteSpace(item.InstallTarget)));
        Assert.Contains("LabelFrame", Assert.Single(plan.Components, item => item.Component.Id == "server-msi").InstallTarget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LabelFrame", Assert.Single(plan.Components, item => item.Component.Id == "client-msi").InstallTarget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET Desktop Runtime", Assert.Single(plan.Components, item => item.Component.Id == "runtime-desktop").InstallTarget, StringComparison.Ordinal);
        Assert.Contains("WebView2", Assert.Single(plan.Components, item => item.Component.Id == "runtime-webview2").InstallTarget, StringComparison.Ordinal);
    }

    // ---- 清单健壮性 --------

    [Fact]
    public void Resolve_dependency_cycle_should_be_rejected()
    {
        // 环状依赖（a → b → a）：拒绝而非死循环 / 错序输出
        var json = """
        {
          "schemaVersion": 1,
          "labelframeVersion": "0.26.0",
          "generatedAt": "2026-09-14T03:00:00Z",
          "components": [
            {
              "id": "comp-a", "type": "msi", "version": "1.0.0",
              "dependsOn": ["comp-b"],
              "urls": ["https://example.invalid/a.msi"],
              "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
              "sizeBytes": 1024, "silentArgs": "",
              "topologies": ["standalone"], "notes": ""
            },
            {
              "id": "comp-b", "type": "msi", "version": "1.0.0",
              "dependsOn": ["comp-a"],
              "urls": ["https://example.invalid/b.msi"],
              "sha256": "2222222222222222222222222222222222222222222222222222222222222222",
              "sizeBytes": 1024, "silentArgs": "",
              "topologies": ["standalone"], "notes": ""
            }
          ]
        }
        """;

        var manifest = InstallManifest.Parse(json);
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(manifest, TopologyPreset.Standalone, Options()));
    }

    [Fact]
    public void Resolve_null_arguments_should_throw()
    {
        Assert.Throws<ArgumentNullException>(() => _resolver.Resolve(null!, TopologyPreset.Standalone, Options()));
        Assert.Throws<ArgumentNullException>(() => _resolver.Resolve(Current, TopologyPreset.Standalone, null!));
    }
}
