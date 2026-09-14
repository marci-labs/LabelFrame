namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>Bundle 链包 id ↔ manifest 组件 id 映射（DESIGN §6.9 链序表；BA 进度 / 完成 / 失败页的展示名来源）。</summary>
/// <remarks>与 <c>packaging/bootstrapper/Bundle.wxs</c> 的 Chain 包 id 一一对应（后续品牌插件按 <c>&lt;Brand&gt;PluginPlacement</c> 同构扩展）。</remarks>
public static class ChainPackageMap
{
    /// <summary>链包 id → manifest 组件 id。</summary>
    public static readonly IReadOnlyDictionary<string, string> ComponentByPackageId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DotNetDesktopRuntime"] = "runtime-desktop",
            ["WebView2Runtime"] = "runtime-webview2",
            ["ServerMsi"] = "server-msi",
            ["ClientMsi"] = "client-msi",
            ["WebUiPlacement"] = "webui",
            ["ZebraPluginPlacement"] = "plugin-zebra",
        };

    /// <summary>manifest 组件 id → 链包 id（反向）。</summary>
    public static readonly IReadOnlyDictionary<string, string> PackageIdByComponent =
        ComponentByPackageId.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
}
