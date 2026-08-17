using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Sample;

namespace LabelFrame.Core.Tests.Transport;

public class PluginDirectoryLoaderLoadWithErrorsTests
{
    [Fact]
    public void LoadWithErrors_broken_dll_should_return_error_with_path_and_message()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.dll"), "not a real assembly");

            var writer = new StringWriter();
            var result = PluginDirectoryLoader.LoadWithErrors(dir, writer);

            Assert.Empty(result.Plugins);
            var error = Assert.Single(result.Errors);
            Assert.EndsWith("broken.dll", error.AssemblyPath);
            Assert.False(string.IsNullOrWhiteSpace(error.Error));
            Assert.Contains("加载失败", writer.ToString()); // 日志仍保留
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void LoadWithErrors_good_dll_should_return_plugin_with_no_errors()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dll = typeof(SampleTransportPlugin).Assembly.Location;
            File.Copy(dll, Path.Combine(dir, Path.GetFileName(dll)));

            var writer = new StringWriter();
            var result = PluginDirectoryLoader.LoadWithErrors(dir, writer);

            var plugin = Assert.Single(result.Plugins);
            Assert.Equal("sample", plugin.Plugin.Id);
            Assert.Empty(result.Errors);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void LoadWithErrors_missing_directory_should_return_empty()
    {
        var result = PluginDirectoryLoader.LoadWithErrors(Path.Combine(Path.GetTempPath(), "no-such-dir-xyz"), TextWriter.Null);
        Assert.Empty(result.Plugins);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Load_should_delegate_to_LoadWithErrors_plugins()
    {
        // 兼容验证：Load = LoadWithErrors().Plugins，坏 DLL 时 Load 返回空且日志保留
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.dll"), "garbage");
            var writer = new StringWriter();
            Assert.Empty(PluginDirectoryLoader.Load(dir, writer));
            Assert.Contains("加载失败", writer.ToString());
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 插件 dll 被 collectible ALC 锁定，进程退出后由系统清理
        }
    }
}