using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.TransportPlugin.Sample;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class PluginInstallerTests
{
    private static (PluginInstaller Installer, TransportPluginRegistry Registry, string PluginsDir) Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfinstall-{Guid.NewGuid():N}");
        var registry = TestTransportRegistry.Create();
        return (new PluginInstaller(dir, registry, TextWriter.Null), registry, dir);
    }

    /// <summary>构建插件包 zip：manifest.json + Sample 插件 DLL。</summary>
    private static byte[] BuildPackage(string pluginId = "sample", string name = "示例插件（测试）", string version = "1.0.0", string? manifestJson = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write(manifestJson ?? $$"""{"pluginId":"{{pluginId}}","name":"{{name}}","version":"{{version}}","description":"测试包"}""");
                }
            
            var dll = zip.CreateEntry("LabelFrame.TransportPlugin.Sample.dll");
            using var dllStream = dll.Open();
            var bytes = File.ReadAllBytes(typeof(SampleTransportPlugin).Assembly.Location);
            dllStream.Write(bytes);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Install_valid_package_should_create_package_dir_and_return_view()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            var view = await installer.InstallAsync(new MemoryStream(BuildPackage()), "sample.lfplugin", CancellationToken.None);

            Assert.Equal("sample", view.PluginId);
            Assert.Equal("示例插件（测试）", view.Name);
            Assert.Equal("1.0.0", view.Version);
            Assert.Equal("package", view.Source);
            Assert.False(view.Loaded); // 未重启：注册表尚未装配

            var dir = Path.Combine(pluginsDir, "sample");
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(Path.Combine(dir, PluginPackageReader.ManifestFileName)));
            Assert.True(File.Exists(Path.Combine(dir, "LabelFrame.TransportPlugin.Sample.dll")));
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_invalid_zip_should_throw()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(new MemoryStream(Encoding.UTF8.GetBytes("not a zip")), "bad.lfplugin", CancellationToken.None));
            Assert.False(Directory.Exists(pluginsDir));
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_builtin_id_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                installer.InstallAsync(new MemoryStream(BuildPackage(pluginId: "log")), "log.lfplugin", CancellationToken.None));
            Assert.Contains("内置插件", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_id_mismatch_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            // manifest.pluginId=other，但包内 DLL 实际实现 id=sample
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                installer.InstallAsync(new MemoryStream(BuildPackage(pluginId: "other")), "other.lfplugin", CancellationToken.None));
            Assert.Contains("不一致", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_no_plugin_implementation_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
                using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                {
                    w.Write("""{"pluginId":"sample","name":"x","version":"1.0.0"}""");
                }
                zip.CreateEntry("README.txt").Open().Dispose(); // 无 DLL
            }

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                installer.InstallAsync(new MemoryStream(ms.ToArray()), "x.lfplugin", CancellationToken.None));
            Assert.Contains("DLL", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_overwrite_same_plugin_id_should_replace_files()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage(version: "1.0.0", name: "旧版")), "a.lfplugin", CancellationToken.None);
            var view = await installer.InstallAsync(new MemoryStream(BuildPackage(version: "2.0.0", name: "新版")), "b.lfplugin", CancellationToken.None);

            Assert.Equal("2.0.0", view.Version);
            Assert.Equal("新版", view.Name);
            var dir = Path.Combine(pluginsDir, "sample");
            Assert.True(Directory.Exists(dir));
            Assert.Contains(Path.GetFileName(typeof(SampleTransportPlugin).Assembly.Location), Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Uninstall_should_remove_package_dir()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage()), "a.lfplugin", CancellationToken.None);
            installer.Uninstall("sample");

            Assert.False(Directory.Exists(Path.Combine(pluginsDir, "sample")));
            Assert.Throws<InvalidDataException>(() => installer.Uninstall("sample")); // 再次卸载报未安装
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public void Uninstall_not_installed_should_throw()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => installer.Uninstall("no-such"));
            Assert.Contains("未安装", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Uninstall_manual_dir_without_manifest_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginsDir, "manual"));
            File.WriteAllText(Path.Combine(pluginsDir, "manual", "x.dll"), "not real");

            var ex = Assert.Throws<InvalidDataException>(() => installer.Uninstall("manual"));
            Assert.Contains("手动放置", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
        public async Task List_should_show_package_and_manual_with_loaded_state()
    {
        var (installer, registry, pluginsDir) = Create();
        try
        {
            // 手动平铺 DLL（非插件文件 → 重启后也不会装配）
            Directory.CreateDirectory(pluginsDir);
            File.WriteAllText(Path.Combine(pluginsDir, "manual.dll"), "not a real plugin assembly");

            // 安装包
            await installer.InstallAsync(new MemoryStream(BuildPackage()), "a.lfplugin", CancellationToken.None);

            var before = installer.ListInstalled();
            Assert.Equal(2, before.Count);
            var pkg = Assert.Single(before.Where(v => v.Source == "package"));
            var manual = Assert.Single(before.Where(v => v.Source == "manual"));
            Assert.False(pkg.Loaded); // 未重启
            Assert.False(manual.Loaded);
            Assert.Equal("manual", manual.PluginId);

            // 模拟重启后：从插件目录重新装配注册表 → 安装包 Loaded=true；manual.dll 非插件 → 仍 false
            foreach (var (plugin, path) in PluginDirectoryLoader.Load(pluginsDir, TextWriter.Null))
            {
                registry.RegisterExternal(plugin, path, TextWriter.Null);
            }

            var after = installer.ListInstalled();
            var pkgAfter = Assert.Single(after.Where(v => v.Source == "package"));
            var manualAfter = Assert.Single(after.Where(v => v.Source == "manual"));
            Assert.True(pkgAfter.Loaded);
            Assert.False(manualAfter.Loaded);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public void List_should_report_load_error_for_broken_package_dll()
    {
        // 场景：manifest 正常但 DLL 损坏（启动加载失败），lastLoadErrors 透出原因（迭代 23 附二拍板）
        var dir = Path.Combine(Path.GetTempPath(), $"lfinstall-{Guid.NewGuid():N}");
        var registry = TestTransportRegistry.Create();
        var packageDir = Path.Combine(dir, "sample");
        Directory.CreateDirectory(packageDir);
        var brokenDll = Path.Combine(packageDir, "LabelFrame.TransportPlugin.Sample.dll");
        try
        {
            File.WriteAllText(Path.Combine(packageDir, "manifest.json"), """{"pluginId":"sample","name":"示例插件","version":"1.0.0"}""");
            File.WriteAllText(brokenDll, "not a real assembly");

            var installer = new PluginInstaller(dir, registry, TextWriter.Null,
                new Dictionary<string, string> { [brokenDll] = "测试加载失败原因：缺依赖" });

            var pkg = Assert.Single(installer.ListInstalled());
            Assert.Equal("package", pkg.Source);
            Assert.False(pkg.Loaded);
            Assert.Contains("测试加载失败原因：缺依赖", pkg.LoadError);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void List_should_report_load_error_for_broken_manual_dll()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfinstall-{Guid.NewGuid():N}");
        var registry = TestTransportRegistry.Create();
        Directory.CreateDirectory(dir);
        var manualDll = Path.Combine(dir, "broken-manual.dll");
        try
        {
            File.WriteAllText(manualDll, "garbage");

            var installer = new PluginInstaller(dir, registry, TextWriter.Null,
                new Dictionary<string, string> { [manualDll] = "测试加载失败原因：非托管依赖缺失" });

            var manual = Assert.Single(installer.ListInstalled());
            Assert.Equal("manual", manual.Source);
            Assert.False(manual.Loaded);
            Assert.Contains("测试加载失败原因：非托管依赖缺失", manual.LoadError);
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
