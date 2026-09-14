using System.Text;
using LabelFrame.Bootstrapper.Downloads;
using LabelFrame.Bootstrapper.Manifest;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// 多源回退状态机测试（迭代 61 / #54「范围修订 v2」；DESIGN §6.10 决策 #125）：
/// urls 顺序即优先级（#115）、失败计数逐源推进、源耗尽停止干预、单源清单退化行为、校验失败同样换源。
/// </summary>
public sealed class CacheSourceFallbackTests
{
    private const string ShaA = "3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c";
    private const string ShaB = "4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d";

    /// <summary>构造多源清单（server-msi 两源 / client-msi 单源——镜像位预留现状）。</summary>
    private static InstallManifest MultiSourceManifest()
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "labelframeVersion": "0.27.0",
          "generatedAt": "2026-09-14T03:00:00Z",
          "components": [
            {
              "id": "server-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://main.example/LabelFrame-Server-0.27.0.msi", "https://mirror.example/LabelFrame-Server-0.27.0.msi"],
              "sha256": "{{ShaA}}", "sizeBytes": 11534336, "silentArgs": "",
              "topologies": ["standalone", "server-win", "offline"], "notes": "服务端"
            },
            {
              "id": "client-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://main.example/LabelFrame-Client-0.27.0.msi"],
              "sha256": "{{ShaB}}", "sizeBytes": 12582912, "silentArgs": "",
              "topologies": ["standalone", "client", "offline"], "notes": "客户端"
            }
          ]
        }
        """;
        return InstallManifest.Parse(json);
    }

    [Fact]
    public void First_acquire_resolves_primary_source_without_rotation()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        var outcome = fallback.ResolveAcquire("ServerMsi");

        Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", outcome.Url);
        Assert.Equal(0, outcome.SourceIndex);
        Assert.Equal(2, outcome.SourceCount);
        Assert.False(outcome.Rotated);
        Assert.True(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Acquire_failure_advances_to_next_source_in_manifest_order()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordAcquireFailure("ServerMsi");
        var outcome = fallback.ResolveAcquire("ServerMsi");

        // #115：顺序即优先级——主源失败后提供镜像源，且标记换源（UI 换源提示依据）
        Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
        Assert.Equal("https://mirror.example/LabelFrame-Server-0.27.0.msi", outcome.Url);
        Assert.Equal(1, outcome.SourceIndex);
        Assert.True(outcome.Rotated);
        Assert.True(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Verify_failure_also_advances_source()
    {
        // 坏哈希（下载成功但校验失败）语义 = 换源重取，而非重复同一损坏源
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordVerifyFailure("ServerMsi");

        Assert.Equal("https://mirror.example/LabelFrame-Server-0.27.0.msi", fallback.ResolveAcquire("ServerMsi").Url);
    }

    [Fact]
    public void Exhausted_sources_stop_intervening()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordAcquireFailure("ServerMsi"); // 主源失败
        fallback.RecordAcquireFailure("ServerMsi"); // 镜像源失败 → 耗尽

        var outcome = fallback.ResolveAcquire("ServerMsi");
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, outcome.Decision);
        Assert.Null(outcome.Url);
        Assert.False(outcome.Rotated);
        Assert.False(fallback.HasUntriedSources("ServerMsi")); // 不再驱动重试（防无源空转）
    }

    [Fact]
    public void Single_source_manifest_fails_closed_without_rotation()
    {
        // 首期生产形态：urls 仅主源一个元素（镜像位预留）——失败即耗尽，结构不变
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        var before = fallback.ResolveAcquire("ClientMsi");
        Assert.Equal("https://main.example/LabelFrame-Client-0.27.0.msi", before.Url);

        fallback.RecordAcquireFailure("ClientMsi");

        var after = fallback.ResolveAcquire("ClientMsi");
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, after.Decision);
        Assert.False(fallback.HasUntriedSources("ClientMsi"));
    }

    [Fact]
    public void Unknown_or_null_package_keeps_engine_source()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        var unknown = fallback.ResolveAcquire("SomeOtherPackage");
        var nullId = fallback.ResolveAcquire(null);

        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, unknown.Decision);
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, nullId.Decision);
        Assert.False(fallback.HasUntriedSources("SomeOtherPackage"));
        Assert.False(fallback.HasUntriedSources(null));

        // 未知包的失败不参与计数（如 Bundle 自身载荷）
        fallback.RecordAcquireFailure("SomeOtherPackage");
        Assert.Equal(0, fallback.FailureCount("SomeOtherPackage"));
    }

    [Fact]
    public void Failure_counters_are_isolated_per_package()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordAcquireFailure("ServerMsi");

        Assert.Equal(1, fallback.FailureCount("ServerMsi"));
        Assert.Equal(0, fallback.FailureCount("ClientMsi"));
        Assert.Equal("https://main.example/LabelFrame-Client-0.27.0.msi", fallback.ResolveAcquire("ClientMsi").Url);
    }

    [Fact]
    public void Reset_clears_counters_for_new_apply()
    {
        // 重试按钮 = 全新 Plan + Apply：失败计数归零、从头按 urls 顺序（§6.10）
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());
        fallback.RecordAcquireFailure("ServerMsi");

        fallback.Reset();

        Assert.Equal(0, fallback.FailureCount("ServerMsi"));
        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", fallback.ResolveAcquire("ServerMsi").Url);
    }

    [Fact]
    public void FromManifest_maps_all_six_chain_packages_from_fixture()
    {
        var manifestJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", "install-manifest.full.json"));
        var manifest = InstallManifest.Parse(manifestJson);
        var fallback = CacheSourceFallback.FromManifest(manifest);

        foreach (var packageId in new[] { "DotNetDesktopRuntime", "WebView2Runtime", "ServerMsi", "ClientMsi", "WebUiPlacement", "ZebraPluginPlacement" })
        {
            var outcome = fallback.ResolveAcquire(packageId);
            Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
            Assert.NotEqual(0, outcome.SourceCount);
        }

        var snapshot = fallback.FailureSnapshot();
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Rotation_indexes_clamp_to_last_source_when_failures_exceed_count()
    {
        // 三源清单失败 5 次：源游标钳制在末位下标，不再覆写（KeepEngineUrl）
        var json = $$"""
        {
          "schemaVersion": 1,
          "labelframeVersion": "0.27.0",
          "generatedAt": "2026-09-14T03:00:00Z",
          "components": [
            {
              "id": "server-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://a.example/s.msi", "https://b.example/s.msi", "https://c.example/s.msi"],
              "sha256": "{{ShaA}}", "sizeBytes": 11534336, "silentArgs": "",
              "topologies": ["standalone"], "notes": "服务端"
            }
          ]
        }
        """;
        var fallback = CacheSourceFallback.FromManifest(InstallManifest.Parse(json));

        var sequence = new List<string>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var outcome = fallback.ResolveAcquire("ServerMsi");
            if (outcome.Url is not null)
            {
                sequence.Add(outcome.Url);
            }

            fallback.RecordAcquireFailure("ServerMsi");
        }

        // 逐源推进 a → b → c，c 之后保持耗尽（不再提供源）
        Assert.Equal(
            new[] { "https://a.example/s.msi", "https://b.example/s.msi", "https://c.example/s.msi" },
            sequence);
        Assert.Equal(5, fallback.FailureCount("ServerMsi"));
        Assert.False(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Outcome_carries_source_metadata_for_ui_hints()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());
        fallback.RecordAcquireFailure("ServerMsi");

        var outcome = fallback.ResolveAcquire("ServerMsi");

        // 进度页「源 i/N」提示与换源文案依赖下标与总数（1 基展示由 BA 层转换）
        Assert.Equal(1, outcome.SourceIndex);
        Assert.Equal(2, outcome.SourceCount);
        Assert.True(outcome.Rotated);
    }

    [Fact]
    public void FromManifest_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => CacheSourceFallback.FromManifest(null!));
    }
}
