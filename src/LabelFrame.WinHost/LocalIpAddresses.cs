using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LabelFrame.WinHost;

/// <summary>本机网络信息（客户端状态栏显示本机 IP）。</summary>
public static class LocalIpAddresses
{
    /// <summary>枚举本机 IPv4 地址（仅启用的非回环地址，去重后返回）。</summary>
    public static IReadOnlyList<string> EnumerateIpv4()
    {
        var result = new List<string>();
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address.Address))
                    {
                        continue;
                    }

                    var text = address.Address.ToString();
                    if (!result.Contains(text))
                    {
                        result.Add(text);
                    }
                }
            }
        }
        catch
        {
            // 网络枚举失败不影响宿主启动（状态栏留空）
        }

        return result;
    }
}