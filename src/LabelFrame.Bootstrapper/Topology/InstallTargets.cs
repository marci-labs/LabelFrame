using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Topology;

/// <summary>目标安装位置描述（确认页展示；目录约定对齐 docs/DEPLOY.md §2 MSI / §4 Ubuntu / §5 管理界面 / §6 插件分发）。</summary>
/// <remarks>dry-run 只描述不落位；实际安装 / 修改语义由专项 6/8（#55）定案，届时此映射升级为结构化路径。</remarks>
public static class InstallTargets
{
    private const string LinuxDataDir = "/var/lib/labelframe/server";

    /// <summary>按组件条目与预设给出目标安装位置描述。</summary>
    public static string Describe(ManifestComponent component, TopologyPreset preset)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return component.Type switch
        {
            "msi" => component.Id switch
            {
                "server-msi" => $"{programFiles}\\LabelFrame\\Server（Windows 服务 LabelFrameServer；数据目录 {programData}\\LabelFrame\\server）",
                "client-msi" => $"{programFiles}\\LabelFrame\\Client",
                _ => $"{programFiles}\\LabelFrame\\{component.Id}",
            },
            "webui-zip" => preset == TopologyPreset.ServerLinux
                ? $"{LinuxDataDir}/plugins/web-ui（放入即生效，无需重启）"
                : $"{programData}\\LabelFrame\\server\\plugins\\web-ui（放入即生效，无需重启）",
            "archive" => component.Id == "linux-server"
                ? $"/opt/labelframe/server（systemd 部署；数据目录 {LinuxDataDir}）"
                : $"/opt/labelframe/{component.Id}",
            "lfplugin" => $"{programData}\\LabelFrame\\Client\\plugins\\{BrandPluginMap.PluginIdOf(TopologyResolver.BrandIdOf(component.Id)) ?? component.Id}",
            "runtime" => component.Id switch
            {
                "runtime-desktop" => ".NET Desktop Runtime（系统级全局安装）",
                "runtime-webview2" => "WebView2 Evergreen 运行时（系统级全局安装）",
                _ => "系统运行时（系统级全局安装）",
            },
            _ => $"{programData}\\LabelFrame\\{component.Id}",
        };
    }

    /// <summary>server-docker 预设的 compose 产物描述（无下载组件；镜像完整性由 registry digest 机制保证）。</summary>
    public static string DockerComposeGuidance(bool includeWebUi) =>
        "Docker 形态无本地安装组件：将由后续版本的安装编排生成 docker-compose 文件（镜像 ghcr.io/marci-labs/labelframe-server）"
        + (includeWebUi
            ? "，并启用镜像内置的管理界面（不下载插件 zip）。"
            : "，保持镜像默认无头（不安装管理界面）。");
}
