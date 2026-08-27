namespace LabelFrame.WinHost.Transport;

/// <summary>Zebra SDK 连接类型；保留在公共配置模型中用于旧 connection.json 兼容。</summary>
public enum ZebraTransportKind
{
    /// <summary>TCP/IP 网络打印机（默认 9100）。</summary>
    Tcp,

    /// <summary>USB 直连（ZebraUsbName 为空时自动发现第一台）。</summary>
    Usb,

    /// <summary>Windows 驱动（按打印机名）。</summary>
    Driver,
}
