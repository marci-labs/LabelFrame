using Android.Content;
using Android.Provider;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 宿主配置：服务端地址 + 打印机通讯参数（品牌 / 连接类型 / TCP 地址端口），SharedPreferences 持久化。
/// 设备号由系统唯一码自动生成，不接受配置（避免多台设备互相领作业）。
/// </summary>
public sealed class LabelHostConfig
{
    /// <summary>本地 HTTP 端口（状态页与 JS 桥）。</summary>
    public const int LocalPort = 53970;

    /// <summary>打印机默认品牌（当前唯一；品牌是未来传输插件的路由键，见 DESIGN 决策 #95）。</summary>
    public const string DefaultPrinterBrand = "zebra";

    /// <summary>默认连接类型（TCP；蓝牙等将来随插件扩展）。</summary>
    public const string DefaultConnectionType = "tcp";

    /// <summary>TCP 打印机默认地址（可被 SharedPreferences 覆盖）。</summary>
    public const string DefaultTcpHost = "192.168.1.50";

    /// <summary>TCP 打印机默认端口。</summary>
    public const int DefaultTcpPort = 9100;

    /// <summary>Server 轮询间隔（秒）。</summary>
    public const int PollIntervalSeconds = 5;

    /// <summary>默认 DPI。</summary>
    public const int Dpi = 203;

    /// <summary>Server 地址（为空不启用路由）。</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>打印机品牌（当前仅 Zebra，等未来插件机制扩展）。</summary>
    public string PrinterBrand { get; set; } = DefaultPrinterBrand;

    /// <summary>打印机连接类型（当前仅 TCP）。</summary>
    public string ConnectionType { get; set; } = DefaultConnectionType;

    /// <summary>打印机 IP。</summary>
    public string TcpHost { get; set; } = DefaultTcpHost;

    /// <summary>打印机端口。</summary>
    public int TcpPort { get; set; } = DefaultTcpPort;

    /// <summary>设备号（系统唯一码自动生成）。</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>设备名称（注册 Server 的展示名；默认 PDA + 设备码后 4 位）。</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>数据库路径（宿主私有目录）。</summary>
    public required string DatabasePath { get; set; }

    /// <summary>从 SharedPreferences 加载（旧装机数据自动迁移：tcp_host 键沿用，端口与品牌缺失取默认值）。</summary>
    public static LabelHostConfig Load(Context context)
    {
        var prefs = context.GetSharedPreferences("labelframe", FileCreationMode.Private)!;
        var deviceId = ResolveDeviceId(context, prefs);

        return new LabelHostConfig
        {
            DatabasePath = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "labelframe", "jobs.db"),
            ServerUrl = prefs.GetString("server_url", string.Empty) ?? string.Empty,
            PrinterBrand = prefs.GetString("printer_brand", DefaultPrinterBrand) ?? DefaultPrinterBrand,
            ConnectionType = prefs.GetString("connection_type", DefaultConnectionType) ?? DefaultConnectionType,
            TcpHost = prefs.GetString("tcp_host", DefaultTcpHost) ?? DefaultTcpHost,
            TcpPort = prefs.GetInt("tcp_port", DefaultTcpPort),
            DeviceId = deviceId,
            DeviceName = prefs.GetString("device_name", null) is { Length: > 0 } name ? name : DefaultDeviceName(deviceId),
        };
    }

    /// <summary>
    /// 持久化到 SharedPreferences（null / 空白 / 越界字段保持原值；设备号始终自动生成，不在此传入）。
    /// 注意：传输 / 路由实例在服务启动时创建，保存后需重启宿主服务生效。
    /// </summary>
    public void Persist(
        Context context,
        string? serverUrl = null,
        string? printerBrand = null,
        string? connectionType = null,
        string? tcpHost = null,
        int? tcpPort = null,
        string? deviceName = null)
    {
        ServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? ServerUrl : serverUrl.Trim();
        PrinterBrand = string.IsNullOrWhiteSpace(printerBrand) ? PrinterBrand : printerBrand.Trim();
        ConnectionType = string.IsNullOrWhiteSpace(connectionType) ? ConnectionType : connectionType.Trim();
        TcpHost = string.IsNullOrWhiteSpace(tcpHost) ? TcpHost : tcpHost.Trim();
        if (tcpPort is > 0 and <= 65535)
        {
            TcpPort = tcpPort.Value;
        }

        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? DeviceName : deviceName.Trim();

        var prefs = context.GetSharedPreferences("labelframe", FileCreationMode.Private)!;
        var editor = prefs.Edit();
        if (editor is not null)
        {
            editor.PutString("server_url", ServerUrl);
            editor.PutString("printer_brand", PrinterBrand);
            editor.PutString("connection_type", ConnectionType);
            editor.PutString("tcp_host", TcpHost);
            editor.PutInt("tcp_port", TcpPort);
            editor.PutString("device_name", DeviceName);
            editor.Apply();
        }
    }

    /// <summary>
    /// 设备号取系统唯一码 ANDROID_ID 原值（每台设备唯一、卸载重装不变、恢复出厂后变化——重置后的设备视为新设备）。
    /// Android 8 之前部分设备会重复返回 9774d56d682e25f8，按取不到处理，本地生成随机码兜底。
    /// </summary>
    private static string ResolveDeviceId(Context context, ISharedPreferences prefs)
    {
        var androidId = Settings.Secure.GetString(context.ContentResolver!, Settings.Secure.AndroidId);
        if (!string.IsNullOrWhiteSpace(androidId) && androidId != "9774d56d682e25f8")
        {
            return androidId.ToLowerInvariant();
        }

        var uuid = prefs.GetString("device_uuid", null);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            uuid = Guid.NewGuid().ToString("N");
            var uuidEditor = prefs.Edit();
            uuidEditor?.PutString("device_uuid", uuid);
            uuidEditor?.Apply();
        }

        return uuid;
    }

    /// <summary>默认设备名称：PDA + 设备码后 4 位（Server 目录 / 目标设备选择器展示可读）。</summary>
    private static string DefaultDeviceName(string deviceId) => $"PDA-{deviceId[^4..].ToUpperInvariant()}";
}
