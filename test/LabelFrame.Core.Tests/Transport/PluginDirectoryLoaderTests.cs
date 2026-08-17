using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Sample;

namespace LabelFrame.Core.Tests.Transport;

public class PluginDirectoryLoaderTests
{
    [Fact]
    public void Load_should_discover_sample_plugin_dll()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 复制示例插件 dll 到插件目录（SampleTransportPlugin 由测试项目引用，产物在测试输出目录）
            var dll = typeof(SampleTransportPlugin).Assembly.Location;
            File.Copy(dll, Path.Combine(dir, Path.GetFileName(dll)));

            var writer = new StringWriter();
            var loaded = PluginDirectoryLoader.Load(dir, writer);

            var plugin = Assert.Single(loaded);
            Assert.Equal("sample", plugin.Plugin.Id);
            Assert.EndsWith(".dll", plugin.AssemblyPath);
            Assert.Empty(writer.ToString());
        }
        finally
        {
            // 插件 dll 被 collectible ALC 锁定（生产「卸载 = 删除文件 + 重启生效」），测试清理容忍失败
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // 文件被加载中的程序集锁定，进程退出后由系统清理
            }
        }
    }

    [Fact]
    public void Load_should_skip_broken_dll_and_log_error()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.dll"), "not a real assembly");

            var writer = new StringWriter();
            var loaded = PluginDirectoryLoader.Load(dir, writer);

            Assert.Empty(loaded);
            Assert.Contains("加载失败", writer.ToString());
        }
        finally
        {
            // 插件 dll 被 collectible ALC 锁定（生产「卸载 = 删除文件 + 重启生效」），测试清理容忍失败
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // 文件被加载中的程序集锁定，进程退出后由系统清理
            }
        }
    }

    [Fact]
    public void Load_missing_directory_should_return_empty()
    {
        var loaded = PluginDirectoryLoader.Load(Path.Combine(Path.GetTempPath(), "no-such-dir-xyz"), TextWriter.Null);
        Assert.Empty(loaded);
    }

    [Fact]
    public void Load_should_create_usable_transport_from_discovered_plugin()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dll = typeof(SampleTransportPlugin).Assembly.Location;
            File.Copy(dll, Path.Combine(dir, Path.GetFileName(dll)));
            var writer = new StringWriter();
            var loaded = PluginDirectoryLoader.Load(dir, writer);

            var registry = new TransportPluginRegistry();
            foreach (var (plugin, path) in loaded)
            {
                registry.Register(plugin, isExternal: true, assemblyPath: path);
            }

            var context = new TransportPluginContext(writer, Path.GetTempPath());
            var transport = registry.CreateTransport("sample", new TransportPluginParameters(), context);

            Assert.NotNull(transport);
            Assert.True(registry.GetPlugin("sample")!.IsExternal);
            Assert.EndsWith(".dll", registry.GetPlugin("sample")!.AssemblyPath);
        }
        finally
        {
            // 插件 dll 被 collectible ALC 锁定（生产「卸载 = 删除文件 + 重启生效」），测试清理容忍失败
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // 文件被加载中的程序集锁定，进程退出后由系统清理
            }
        }
    }
}
