namespace LabelFrame.Bootstrapper.Topology;

/// <summary>拓扑预设（DESIGN §6.3，决策 #116）：有限枚举 + 仅两项自由开关，不提供任意组件勾选。</summary>
/// <remarks>PC 引导问卷的五预设；<c>offline</c>（离线全量包）是一等形态但不进问卷枚举（断网自动切换后置 #75），<c>pda</c> 经服务端下载中心分发（#52）。</remarks>
public enum TopologyPreset
{
    /// <summary>单机一体：一台 Windows PC 承载 Server 服务 + Client 同机。</summary>
    Standalone,

    /// <summary>服务端 · Windows 服务（分离部署）。</summary>
    ServerWin,

    /// <summary>服务端 · Docker（无下载组件，compose 生成 + 镜像拉取指引）。</summary>
    ServerDocker,

    /// <summary>服务端 · Linux systemd（归档下载 + 部署指引）。</summary>
    ServerLinux,

    /// <summary>追加打印客户端（已有服务端的网络追加一台打印 PC）。</summary>
    Client,
}

public static class TopologyPresetExtensions
{
    /// <summary>manifest 拓扑标记 id（§6.2 topologies 枚举中的预设部分）。</summary>
    public static string ToManifestId(this TopologyPreset preset) => preset switch
    {
        TopologyPreset.Standalone => "standalone",
        TopologyPreset.ServerWin => "server-win",
        TopologyPreset.ServerDocker => "server-docker",
        TopologyPreset.ServerLinux => "server-linux",
        TopologyPreset.Client => "client",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "未知拓扑预设。"),
    };

    /// <summary>问卷展示名（中文单语，REQUIREMENTS §7 边界：i18n 不进 setup 问卷）。</summary>
    public static string DisplayName(this TopologyPreset preset) => preset switch
    {
        TopologyPreset.Standalone => "单机一体",
        TopologyPreset.ServerWin => "服务端 · Windows 服务",
        TopologyPreset.ServerDocker => "服务端 · Docker",
        TopologyPreset.ServerLinux => "服务端 · Linux systemd",
        TopologyPreset.Client => "追加打印客户端",
        _ => preset.ToString(),
    };

    /// <summary>问卷描述（一句话说明场景）。</summary>
    public static string Description(this TopologyPreset preset) => preset switch
    {
        TopologyPreset.Standalone => "一台 Windows 电脑承载服务端与打印客户端（Server 服务 + Client 同机），适合一机一打印机的最小部署。",
        TopologyPreset.ServerWin => "Windows 服务器上以 Windows 服务运行服务端，局域网内其他电脑各自安装打印客户端。",
        TopologyPreset.ServerDocker => "在 Docker 宿主上以容器运行服务端（本向导只生成指引，不下载组件）。",
        TopologyPreset.ServerLinux => "Ubuntu 裸机以 systemd 运行服务端（下载 Linux 归档并给出部署指引）。",
        TopologyPreset.Client => "网络中已有服务端，本机只作为打印客户端接入。",
        _ => string.Empty,
    };
}
