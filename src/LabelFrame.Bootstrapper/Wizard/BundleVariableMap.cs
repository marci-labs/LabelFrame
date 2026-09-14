using LabelFrame.Bootstrapper.Topology;

namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>Burn 变量契约（DESIGN §6.3，决策 #122）：问卷解析结果 → Bundle 链条件变量——BA 在确认页写入引擎，Chain 内 MsiPackage 以 Condition 消费。</summary>
/// <remarks>纯函数（net10 测试锚定）；后续品牌插件按 <c>InstallPlugin&lt;Brand&gt;</c> 同构扩展（7/8 #56）。</remarks>
public static class BundleVariableMap
{
    /// <summary>预设 manifest id 变量（standalone / server-win / server-docker / server-linux / client）。</summary>
    public const string PresetVariable = "InstallPreset";

    /// <summary>是否安装 Server MSI（链内 server-msi 条件）。</summary>
    public const string ServerVariable = "InstallServer";

    /// <summary>是否安装 Client MSI（链内 client-msi 条件）。</summary>
    public const string ClientVariable = "InstallClient";

    /// <summary>是否带管理界面（webui 落位归 6/8 #55）。</summary>
    public const string WebUiVariable = "InstallWebUi";

    /// <summary>是否安装 Zebra 品牌插件（plugin-zebra 条目随 7/8 #56 出现）。</summary>
    public const string ZebraPluginVariable = "InstallPluginZebra";

    /// <summary>拓扑计划 → 变量集合（string 值；数值变量由 BA 写引擎时转 long）。</summary>
    public static IReadOnlyDictionary<string, string> ToVariables(TopologyPlan plan)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(plan);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }
#endif

        var ids = new HashSet<string>(plan.Components.Select(item => item.Component.Id), StringComparer.Ordinal);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PresetVariable] = plan.Preset.ToManifestId(),
            [ServerVariable] = Flag(ids.Contains("server-msi")),
            [ClientVariable] = Flag(ids.Contains("client-msi")),
            [WebUiVariable] = Flag(ids.Contains("webui")),
            [ZebraPluginVariable] = Flag(ids.Contains("plugin-zebra")),
        };
    }

    private static string Flag(bool enabled) => enabled ? "1" : "0";
}
