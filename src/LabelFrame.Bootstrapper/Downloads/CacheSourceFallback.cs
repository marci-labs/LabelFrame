using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Downloads;

/// <summary>多源回退决策结果（#54：BA 消费引擎获取解析事件，按清单 urls 顺序提供下一源）。</summary>
/// <param name="Decision">决策：提供源（覆写引擎下载 URL）或保持引擎当前源。</param>
/// <param name="Url">决策为 <see cref="SourceFallbackDecision.UseSource"/> 时提供的目标源 URL（其余为 null）。</param>
/// <param name="SourceIndex">本次源在 urls 数组中的下标（0 起；未知包为 -1）。</param>
/// <param name="SourceCount">该组件清单 urls 总数（未知包为 0）。</param>
/// <param name="Rotated">本次是否发生换源（失败后前进到下一源，UI「换源提示」依据）。</param>
public sealed record SourceFallbackOutcome(
    SourceFallbackDecision Decision,
    string? Url,
    int SourceIndex,
    int SourceCount,
    bool Rotated);

/// <summary>多源回退决策枚举。</summary>
public enum SourceFallbackDecision
{
    /// <summary>提供 urls[min(失败数, count-1)] 作为下载源（运行时清单是源顺序的权威，DESIGN §6.10）。</summary>
    UseSource,

    /// <summary>不干预（源已耗尽：保留引擎当前 URL，交由失败报告处置）。</summary>
    KeepEngineUrl,
}

/// <summary>
/// 多源回退状态机（迭代 61 / Issue #54「范围修订 v2」；UI 无关，BA 事件层薄接线，本类承载全部决策逻辑供 net10 测试锚定）。
/// </summary>
/// <remarks>
/// <para>
/// 契约（DESIGN §6.2 决策 #115 + §6.10）：清单组件 <c>urls</c> 数组<b>顺序即优先级、逐源回退</b>；首期仅主源一个元素、
/// 镜像位预留——单源清单下本状态机退化为「失败即耗尽」，结构不变、零 schema 变更即可追加源。
/// </para>
/// <para>
/// 推进语义：每个链包维护<b>失败计数</b>（获取失败与获取后校验失败各计一次）——第 N 次获取解析时提供
/// urls[min(N, count-1)]（第 0 次即首源，通常与 Bundle 构建期 DownloadUrl 相同）；计数达到 count 即源耗尽
/// （不再覆写、不再驱动重试）。计数仅在新建 Apply（重试按钮）时复位；包级 / 引擎自身的获取重试不复位
/// （换源正是在重试轮次间发生）。事件线程调用（Burn 引擎回调单线程），无需加锁。
/// </para>
/// <para>
/// WiX 版本对应：Burn v3 的 <c>ResolveSource</c> 事件（BA 以 <c>DownloadSource</c> 提供备选源）在 v4+（本仓 v7）
/// 演进为「<c>CacheAcquireBegin</c> / <c>CacheAcquireComplete</c> 事件内调用 <c>IEngine.SetDownloadSource</c> +
/// 失败时事件返回 <c>Retry</c> 动作」（引擎源码 <c>AcquireContainerOrPayload</c> 的获取 / 重试循环）——
/// 本类即该回退链路的决策核心。
/// </para>
/// </remarks>
public sealed class CacheSourceFallback
{
    private readonly Dictionary<string, int> _failuresByPackage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _urlsByPackage;

    private CacheSourceFallback(Dictionary<string, IReadOnlyList<string>> urlsByPackage)
    {
        _urlsByPackage = urlsByPackage;
    }

    /// <summary>从安装清单构建（链包 id → 该组件 urls；映射表 = <see cref="ChainPackageMap"/>，清单缺组件则该包无回退能力）。</summary>
    public static CacheSourceFallback FromManifest(InstallManifest manifest)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(manifest);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }
#endif

        var urlsByPackage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            if (ChainPackageMap.PackageIdByComponent.TryGetValue(component.Id, out var packageId))
            {
                urlsByPackage[packageId] = component.Urls;
            }
        }

        return new CacheSourceFallback(urlsByPackage);
    }

    /// <summary>获取解析决策：按该包当前失败计数提供源（<see cref="SourceFallbackDecision.UseSource"/>）或保持引擎源（耗尽 / 未知包）。</summary>
    /// <param name="packageOrContainerId">引擎获取解析事件的包（或容器）id——六包链中恒为链包 id。</param>
    public SourceFallbackOutcome ResolveAcquire(string? packageOrContainerId)
    {
        if (packageOrContainerId is null || !_urlsByPackage.TryGetValue(packageOrContainerId, out var urls) || urls.Count == 0)
        {
            return new SourceFallbackOutcome(SourceFallbackDecision.KeepEngineUrl, null, -1, 0, Rotated: false);
        }

        var failures = _failuresByPackage.TryGetValue(packageOrContainerId, out var count) ? count : 0;
        var index = Math.Min(failures, urls.Count - 1);
        var exhausted = failures >= urls.Count;
        return new SourceFallbackOutcome(
            exhausted ? SourceFallbackDecision.KeepEngineUrl : SourceFallbackDecision.UseSource,
            exhausted ? null : urls[index],
            index,
            urls.Count,
            Rotated: !exhausted && failures > 0);
    }

    /// <summary>记录一次获取失败（下载 / 拷贝失败——引擎 CacheAcquireComplete 非零）。</summary>
    public void RecordAcquireFailure(string? packageOrContainerId) => RecordFailure(packageOrContainerId);

    /// <summary>记录一次校验失败（下载完成但哈希不符——引擎 CacheVerifyComplete 非零；坏哈希同样按序换源重取）。</summary>
    public void RecordVerifyFailure(string? packageOrContainerId) => RecordFailure(packageOrContainerId);

    /// <summary>该包是否还有未尝试的源（失败重试的驱动条件：真才覆写引擎 Retry，避免无源空转）。</summary>
    public bool HasUntriedSources(string? packageOrContainerId) =>
        packageOrContainerId is not null
        && _urlsByPackage.TryGetValue(packageOrContainerId, out var urls)
        && (_failuresByPackage.TryGetValue(packageOrContainerId, out var failures) ? failures : 0) < urls.Count;

    /// <summary>该包当前失败计数（源耗尽判定与失败报告「已尝试 N/M 源」展示）。</summary>
    public int FailureCount(string? packageOrContainerId) =>
        packageOrContainerId is not null && _failuresByPackage.TryGetValue(packageOrContainerId, out var failures) ? failures : 0;

    /// <summary>失败计数快照（链包 id → 失败次数；失败报告展示用）。</summary>
    public IReadOnlyDictionary<string, int> FailureSnapshot() =>
        new Dictionary<string, int>(_failuresByPackage, StringComparer.Ordinal);

    /// <summary>全部失败计数复位（新建 Apply——进度页「重试」触发全新 Plan + Apply 时由 BA 调用）。</summary>
    public void Reset() => _failuresByPackage.Clear();

    private void RecordFailure(string? packageOrContainerId)
    {
        if (packageOrContainerId is null || !_urlsByPackage.ContainsKey(packageOrContainerId))
        {
            return; // 未知包（如 Bundle 自身载荷）不参与回退计数
        }

        _failuresByPackage[packageOrContainerId] = FailureCount(packageOrContainerId) + 1;
    }
}
