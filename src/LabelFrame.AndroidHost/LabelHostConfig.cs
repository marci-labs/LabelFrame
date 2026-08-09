using Android.Content;

namespace LabelFrame.AndroidHost;

/// <summary>宿主配置：本地端口、打印机、Server 路由（SharedPreferences 可覆盖默认值）。</summary>
public sealed class LabelHostConfig
{
    /// <summary>本地 HTTP 端口（PDA 网页直连）。</summary>
    public const int LocalPort = 53970;

    /// <summary>TCP 打印机默认地址（可被 SharedPreferences 覆盖）。</summary>
    public const string DefaultTcpHost = "192.168.1.50";

    /// <summary>TCP 打印机默认端口。</summary>
    public const int TcpPort = 9100;

    /// <summary>Server 轮询间隔（秒）。</summary>
    public const int PollIntervalSeconds = 5;

    /// <summary>默认 DPI。</summary>
    public const int Dpi = 203;

    /// <summary>打印机地址。</summary>
    public string TcpHost { get; set; } = DefaultTcpHost;

    /// <summary>Server 地址（为空不启用路由）。</summary>
    public string? ServerUrl { get; set; }

    /// <summary>PC 单机服务地址（PDA 测试模式：拉模板 / 回传日志，为空不启用）。</summary>
    public string? PcHostUrl { get; set; }

    /// <summary>设备标识（注册到 Server）。</summary>
    public string DeviceId { get; set; } = "android-pda-1";

    /// <summary>数据库目录（宿主私有目录）。</summary>
    public required string DatabasePath { get; set; }

    /// <summary>从 SharedPreferences 加载覆盖。</summary>
    public static LabelHostConfig Load(Context context)
    {
        var prefs = context.GetSharedPreferences("labelframe", FileCreationMode.Private)!;
        var config = new LabelHostConfig
        {
            DatabasePath = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "labelframe", "jobs.db"),
            TcpHost = prefs.GetString("tcp_host", DefaultTcpHost)!,
            ServerUrl = prefs.GetString("server_url", null),
            PcHostUrl = prefs.GetString("pc_host", null),
            DeviceId = prefs.GetString("device_id", "android-pda-1")!,
        };
        return config;
    }
}