using System.Net;
using LabelFrame.Bootstrapper.Tests.TestInfrastructure;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>dry-run 契约断言（AC-03）：全程无网络写入（仅 GET 清单）、无系统改动（无文件 / 目录新增）。</summary>
public sealed class DryRunContractTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Manifests", fileName);

    [Fact]
    public async Task Local_manifest_full_flow_should_not_touch_filesystem()
    {
        // 探针目录：清单文件所在目录（走完整个问卷流程后逐项比对，无任何新增 / 修改）
        var probeRoot = Path.Combine(Path.GetTempPath(), $"labelframe-bootstrapper-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeRoot);
        var manifestPath = Path.Combine(probeRoot, "install-manifest.json");
        File.Copy(FixturePath("install-manifest.current.json"), manifestPath);

        var programDataLabelFrame = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabelFrame");
        var programDataExistedBefore = Directory.Exists(programDataLabelFrame);

        try
        {
            var before = Snapshot(probeRoot);

            var session = new WizardSession(installedPrinterNames: () => ["ZDesigner ZD421-203dpi ZPL"])
            {
                ManifestSource = manifestPath,
            };
            await session.LoadManifestAsync();

            // 走完全部预设 × 开关组合（问卷所有路径）
            foreach (TopologyPreset preset in Enum.GetValues<TopologyPreset>())
            {
                session.Preset = preset;
                session.IncludeWebUi = !session.IncludeWebUi;
                _ = session.BuildPlan();
            }

            Assert.Equal(before, Snapshot(probeRoot));
            if (!programDataExistedBefore)
            {
                Assert.False(Directory.Exists(programDataLabelFrame), "dry-run 不得创建 ProgramData\\LabelFrame");
            }
        }
        finally
        {
            Directory.Delete(probeRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Local_manifest_load_should_issue_zero_network_requests()
    {
        using var handler = new RecordingHttpMessageHandler(File.ReadAllText(FixturePath("install-manifest.current.json")));
        using var http = new HttpClient(handler);

        var session = new WizardSession { ManifestSource = FixturePath("install-manifest.current.json") };
        await session.LoadManifestAsync(http);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Url_manifest_load_should_issue_single_readonly_get_and_never_fetch_components()
    {
        // URL 来源：清单获取是引导全程唯一的网络访问——单次 GET（无网络写入），组件 URL 不被触碰
        using var handler = new RecordingHttpMessageHandler(File.ReadAllText(FixturePath("install-manifest.full.json")));
        using var http = new HttpClient(handler);
        const string manifestUrl = "https://example.invalid/install-manifest.json";

        var session = new WizardSession { ManifestSource = manifestUrl };
        await session.LoadManifestAsync(http);

        session.Preset = TopologyPreset.Standalone;
        session.SetBrandSelected("zebra", true);
        session.IncludeWebUi = true;
        _ = session.BuildPlan();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(manifestUrl, request.RequestUri!.ToString());
    }

    [Fact]
    public void Session_should_always_be_dry_run()
    {
        // 本迭代不存在执行模式：会话没有任何下载 / 安装 / 写入方法（该断言防执行开关被误引入）
        Assert.True(new WizardSession().IsDryRun);
    }

    [Fact]
    public void Dry_run_notice_should_state_no_download_and_no_install()
    {
        // AC-03 UI 明示：确认页横幅必须含「仅预览 / 尚未下载 / 尚未安装」语义
        Assert.Contains("仅预览", DryRunNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("尚未下载", DryRunNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("尚未安装", DryRunNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("改动", DryRunNotice.Banner, StringComparison.Ordinal);
    }

    /// <summary>递归快照（相对路径 + 文件大小 + 修改时间），用于「无系统改动」比对。</summary>
    private static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => $"{Path.GetRelativePath(root, path)}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<dir>")}")
            .Order(StringComparer.Ordinal)];
}
