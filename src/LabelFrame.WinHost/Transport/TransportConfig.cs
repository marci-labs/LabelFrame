using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelFrame.WinHost.Transport;

/// <summary>连接配置（当前生效的连接方式与参数；单一连接）。</summary>
public sealed class TransportConfig
{
    /// <summary>连接方式。</summary>
    public TransportMode Mode { get; set; } = TransportMode.Log;

    /// <summary>TCP 打印机主机 / IP（Tcp 或 Zebra-Tcp 用）。</summary>
    public string TcpHost { get; set; } = string.Empty;

    /// <summary>TCP 端口（默认 9100）。</summary>
    public int TcpPort { get; set; } = 9100;

    /// <summary>Windows 驱动打印机名（WindowsDriver 或 Zebra-Driver 用）。</summary>
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>Zebra 连接类型（Transport=Zebra 时生效）。</summary>
    public ZebraTransportKind ZebraKind { get; set; } = ZebraTransportKind.Tcp;

    /// <summary>Zebra USB 打印机名（为空时自动发现第一台）。</summary>
    public string ZebraUsbName { get; set; } = string.Empty;

    /// <summary>目标描述（用于状态栏 / 徽标 / 消息）。</summary>
    public string Describe() => Mode switch
    {
        TransportMode.Log => "LOG（模拟打印）",
        TransportMode.Tcp => $"TCP {TcpHost}:{TcpPort}",
        TransportMode.WindowsDriver => $"WindowsDriver {PrinterName}",
        TransportMode.Zebra => ZebraKind switch
        {
            ZebraTransportKind.Tcp => $"Zebra TCP {TcpHost}:{TcpPort}",
            ZebraTransportKind.Usb => string.IsNullOrWhiteSpace(ZebraUsbName) ? "Zebra USB（自动发现）" : $"Zebra USB {ZebraUsbName}",
            ZebraTransportKind.Driver => $"Zebra 驱动 {PrinterName}",
            _ => "Zebra",
        },
        _ => Mode.ToString(),
    };

    /// <summary>序列化为 JSON（connection.json）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>从 JSON 反序列化（失败返回 null）。</summary>
    public static TransportConfig? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TransportConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}