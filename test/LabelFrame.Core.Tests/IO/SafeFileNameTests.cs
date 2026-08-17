using LabelFrame.Core.IO;

namespace LabelFrame.Core.Tests.IO;

public class SafeFileNameTests
{
    [Theory]
    [InlineData("a.lfplugin")]
    [InlineData("LabelFrame.TransportPlugin.Sample.dll")]
    [InlineData("名字 含空格.lfplugin")]
    public void Normalize_should_keep_plain_names(string name)
    {
        Assert.Equal(name, SafeFileName.Normalize(name));
    }

    [Theory]
    [InlineData("../evil.lfplugin")]
    [InlineData("..\\evil.lfplugin")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b.lfplugin")]
    [InlineData("a\\b.lfplugin")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a:b.lfplugin")]
    [InlineData("a*b.lfplugin")]
    public void Normalize_should_reject_unsafe_names(string name)
    {
        Assert.Null(SafeFileName.Normalize(name));
    }
}