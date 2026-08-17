using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Sample;

namespace LabelFrame.Core.Tests.Transport;

public class PluginDirectoryLoaderSubdirTests
{
    /// <summary>插件目录 = 平铺 + 子目录（迭代 23 决策 3A：安装包放 plugins/&lt;pluginId&gt;/，手动 DLL 平铺）。</summary>
    [Fact]
    public void Load_should_scan_flat_and_subdirectory_dlls()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "sample"));
        try
        {
            var dll = typeof(SampleTransportPlugin).Assembly.Location;
            var bytes = File.ReadAllBytes(dll);

            // 平铺手动 DLL（复制到子目录同名文件避免与测试输出目录冲突）
            File.WriteAllBytes(Path.Combine(dir, "manual.dll"), bytes);
            // 子目录安装包
            File.WriteAllBytes(Path.Combine(dir, "sample", Path.GetFileName(dll)), bytes);

            var writer = new StringWriter();
            var loaded = PluginDirectoryLoader.Load(dir, writer);

            Assert.Equal(2, loaded.Count);
            Assert.All(loaded, p => Assert.Equal("sample", p.Plugin.Id));
            Assert.Contains(loaded, p => Path.GetDirectoryName(p.AssemblyPath) == dir);
            Assert.Contains(loaded, p => Path.GetDirectoryName(p.AssemblyPath) == Path.Combine(dir, "sample"));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Load_should_scan_installed_package_subdirectory_after_restart()
    {
        // 模拟安装包目录布局：plugins/sample/*.dll + manifest.json（重启后注册表装配）
        var dir = Path.Combine(Path.GetTempPath(), $"lfplugins-{Guid.NewGuid():N}");
        var packageDir = Path.Combine(dir, "sample");
        Directory.CreateDirectory(packageDir);
        try
        {
            var dll = typeof(SampleTransportPlugin).Assembly.Location;
            File.Copy(dll, Path.Combine(packageDir, Path.GetFileName(dll)));
            File.WriteAllText(Path.Combine(packageDir, "manifest.json"), """{"pluginId":"sample","name":"示例插件","version":"1.0.0"}""");

            var writer = new StringWriter();
            var loaded = PluginDirectoryLoader.Load(dir, writer);

            var plugin = Assert.Single(loaded);
            Assert.Equal("sample", plugin.Plugin.Id);
            Assert.StartsWith(packageDir, plugin.AssemblyPath);
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