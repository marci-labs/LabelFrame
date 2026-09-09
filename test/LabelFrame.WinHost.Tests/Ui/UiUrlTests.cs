using LabelFrame.WinHost.Ui;

namespace LabelFrame.WinHost.Tests.Ui;

/// <summary>界面地址规则：监听地址规范化（通配 → 127.0.0.1）与窗口内导航边界。</summary>
public class UiUrlTests
{
    [Theory]
    [InlineData("http://0.0.0.0:53960", "http://127.0.0.1:53960/")]
    [InlineData("http://[::]:53960", "http://127.0.0.1:53960/")]
    public void ToLocalUiUrl_规范化通配监听为本机回环(string listenUrl, string expected)
    {
        Assert.Equal(expected, UiUrl.ToLocalUiUrl(listenUrl));
    }

    [Theory]
    [InlineData("http://*:53960")] // Uri 无法解析 * / + / 裸 ::（原实现即透传）
    [InlineData("http://+:53960")]
    [InlineData("http://::53960")]
    [InlineData("http://127.0.0.1:53960")]
    [InlineData("http://localhost:53960/")]
    public void ToLocalUiUrl_非通配地址原样返回(string listenUrl)
    {
        Assert.Equal(listenUrl, UiUrl.ToLocalUiUrl(listenUrl));
    }

    [Fact]
    public void ToLocalUiUrl_无法解析的输入原样返回()
    {
        Assert.Equal("not-a-url", UiUrl.ToLocalUiUrl("not-a-url"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:53960/designer", true)]
    [InlineData("http://localhost:53960/", true)]
    [InlineData("http://[::1]:53960/", true)]
    [InlineData("https://github.com/marci-labs/LabelFrame", false)]
    [InlineData("http://192.168.1.10:8080/help", false)]
    public void IsLocalNavigation_按回环地址判定(string url, bool expected)
    {
        Assert.Equal(expected, UiUrl.IsLocalNavigation(new Uri(url)));
    }
}
