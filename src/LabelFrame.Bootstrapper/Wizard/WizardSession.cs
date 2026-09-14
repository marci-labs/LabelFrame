using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Printing;
using LabelFrame.Bootstrapper.Topology;

namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>问卷会话（UI 无关的核心状态机）：manifest 来源 → 已加载清单 → 预设 / 品牌多选 / 管理界面开关 → 组件集合（dry-run）。</summary>
/// <remarks>
/// dry-run 契约（Issue #53 AC-03、DESIGN §6.3 实现要点）：本类没有任何下载、写入或系统改动方法——
/// 全程只读（清单获取 + 已装打印机名枚举）；下载归 #54、安装归 #55。
/// </remarks>
public sealed class WizardSession
{
    /// <summary>稳定通道清单 URL（GitHub latest 语义，DESIGN §6.2）。</summary>
    public const string StableChannelManifestUrl = "https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json";

    private readonly ITopologyResolver _resolver;
    private readonly Func<IReadOnlyList<string>> _installedPrinterNames;

    public WizardSession(ITopologyResolver? resolver = null, Func<IReadOnlyList<string>>? installedPrinterNames = null)
    {
        _resolver = resolver ?? new TopologyResolver();
        _installedPrinterNames = installedPrinterNames ?? InstalledPrinters.GetNames;
    }

    /// <summary>本迭代恒为 dry-run：会话不存在执行动作（测试断言锚点，防止误引入执行模式）。</summary>
    public bool IsDryRun { get; } = true;

    /// <summary>清单来源：本地路径或 URL（默认稳定通道）。</summary>
    public string ManifestSource { get; set; } = StableChannelManifestUrl;

    /// <summary>已加载并校验的安装清单（问卷后续步骤的前提）。</summary>
    public InstallManifest? Manifest { get; private set; }

    /// <summary>已选拓扑预设（问卷第 2 步）。</summary>
    public TopologyPreset? Preset { get; set; }

    /// <summary>已选打印机品牌集合（问卷第 3 步；选项来源仅为 manifest 已有 plugin-&lt;brand&gt; 条目）。</summary>
    public IReadOnlySet<string> SelectedBrands { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>勾选 / 取消品牌（品牌页写入口；保持集合只读视图不变）。</summary>
    public void SetBrandSelected(string brand, bool selected)
    {
        var mutable = (ISet<string>)SelectedBrands;
        if (selected)
        {
            mutable.Add(brand);
        }
        else
        {
            mutable.Remove(brand);
        }
    }

    /// <summary>是否带管理界面（问卷第 4 步开关）。</summary>
    public bool IncludeWebUi { get; set; }

    /// <summary>当前清单可选择的品牌（无 plugin-* 条目则为空，品牌页展示说明）。</summary>
    public IReadOnlyList<string> AvailableBrands => Manifest is null ? [] : TopologyResolver.AvailableBrands(Manifest);

    /// <summary>加载并校验清单（本地路径 = 文件读取；URL = 单次只读 GET）。加载成功后按已装打印机名预选品牌（决议 2）。</summary>
    public async Task LoadManifestAsync(HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        Manifest = await InstallManifestLoader.LoadAsync(ManifestSource, http, cancellationToken).ConfigureAwait(false);

        // 品牌预选（决议 2）：仅 Zebra——驱动名含 ZDesigner 且清单有对应条目才预勾选；其余品牌从零勾选
        SelectedBrands = new HashSet<string>(
            PrinterBrandDetector.DetectPreselectedBrands(_installedPrinterNames(), AvailableBrands), StringComparer.Ordinal);
    }

    /// <summary>按当前问卷答案计算组件集合（确认页数据源；#54 / #55 的消费入口）。</summary>
    public TopologyPlan BuildPlan()
    {
        if (Manifest is null)
        {
            throw new InvalidOperationException("尚未加载安装清单，无法计算组件集合。");
        }

        if (Preset is null)
        {
            throw new InvalidOperationException("尚未选择部署形态（拓扑预设），无法计算组件集合。");
        }

        return _resolver.Resolve(Manifest, Preset.Value, new TopologyOptions(SelectedBrands, IncludeWebUi));
    }
}
