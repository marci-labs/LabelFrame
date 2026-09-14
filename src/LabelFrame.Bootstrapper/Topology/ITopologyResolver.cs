using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Topology;

/// <summary>「预设 + 开关 → 组件集合」解析契约（DESIGN §6.3，形状细化见决策 #121）——专项 5/8（#54 下载引擎）与 6/8（#55 安装编排）的公共契约。</summary>
public interface ITopologyResolver
{
    /// <summary>按预设与开关计算组件集合：核心组件 = topologies 命中预设即纳入；开关组件（webui / plugin-<brand>）由开关决定且仍受 topologies 过滤；dependsOn 闭包纳入。</summary>
    /// <param name="manifest">安装清单（已通过 schema 校验）。</param>
    /// <param name="preset">拓扑预设（offline 不进问卷枚举；server-docker 返回空集合 + compose 指引）。</param>
    /// <param name="options">仅有的两项自由开关：品牌多选（brand id）+ 是否带管理界面。</param>
    TopologyPlan Resolve(InstallManifest manifest, TopologyPreset preset, TopologyOptions options);
}

/// <summary>问卷仅有的两项自由开关（决策 #116）。</summary>
/// <param name="SelectedBrands">选中的品牌 id 集合（如 "zebra"，映射 manifest 的 plugin-&lt;brand&gt; 条目）。</param>
/// <param name="IncludeWebUi">是否带管理界面（webui 组件落位服务端 plugins/web-ui；server-docker 等价于启用镜像内置界面）。</param>
public sealed record TopologyOptions(IReadOnlySet<string> SelectedBrands, bool IncludeWebUi);

/// <summary>计划安装的组件：清单条目 + 目标安装位置描述（目录约定对齐 DEPLOY；实际落位 / 修改语义由 #55 定案）。</summary>
public sealed record PlannedComponent(ManifestComponent Component, string InstallTarget);

/// <summary>解析结果：供确认页展示与 #54 / #55 消费的结构化组件清单。</summary>
/// <param name="Preset">命中的拓扑预设。</param>
/// <param name="Components">按依赖序排列的组件清单（含依赖闭包；同层按清单声明序）。server-docker 为空集合。</param>
/// <param name="TotalSizeBytes">全部组件体积合计（字节）。</param>
/// <param name="DockerComposeGuidance">仅 server-docker 非空：compose 生成与镜像拉取指引（无下载组件）。</param>
public sealed record TopologyPlan(
    TopologyPreset Preset,
    IReadOnlyList<PlannedComponent> Components,
    long TotalSizeBytes,
    string? DockerComposeGuidance);
