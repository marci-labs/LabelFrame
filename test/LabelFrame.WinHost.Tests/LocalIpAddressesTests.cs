using System.Net;
using System.Net.Sockets;

namespace LabelFrame.WinHost.Tests;

/// <summary>本机 IPv4 枚举（状态栏显示用）。</summary>
public class LocalIpAddressesTests
{
    [Fact]
    public void Enumerate_ipv4_should_return_valid_non_loopback_ips()
    {
        var ips = LocalIpAddresses.EnumerateIpv4();

        foreach (var ip in ips)
        {
            Assert.True(IPAddress.TryParse(ip, out var address), $"无法解析为 IP：{ip}");
            Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
            Assert.False(IPAddress.IsLoopback(address));
        }

        Assert.Equal(ips.Count, ips.Distinct().Count());
    }
}