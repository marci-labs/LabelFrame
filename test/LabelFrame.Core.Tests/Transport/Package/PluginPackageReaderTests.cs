using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.TransportPlugin.Sample;

namespace LabelFrame.Core.Tests.Transport.Package;

public class PluginPackageReaderTests
{
    /// <summary>构建插件包 zip：manifest.json + Sample 插件 DLL（测试用）。</summary>
    internal static byte[] BuildPackage(string pluginId = "sample", string name = "示例插件（测试）", string version = "1.0.0", string? manifestJson = null)
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
    public void Read_valid_package_should_return_manifest_and_dll()
    {
        var content = PluginPackageReader.Read(BuildPackage());

        Assert.Equal("sample", content.Manifest.PluginId);
        Assert.Equal("示例插件（测试）", content.Manifest.Name);
        Assert.Equal("1.0.0", content.Manifest.Version);
        Assert.Contains("LabelFrame.TransportPlugin.Sample.dll", content.DllEntries);
    }

    [Fact]
    public void Read_missing_manifest_should_throw()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("other.json").Open().Dispose();
        }

        var ex = Assert.Throws<InvalidDataException>(() => PluginPackageReader.Read(ms.ToArray()));
        Assert.Contains("manifest.json", ex.Message);
    }

    [Fact]
    public void Read_missing_required_field_should_throw()
    {
        Assert.Throws<InvalidDataException>(() => PluginPackageReader.Read(BuildPackage(manifestJson: """{"name":"x","version":"1.0.0"}""")));
        Assert.Throws<InvalidDataException>(() => PluginPackageReader.Read(BuildPackage(manifestJson: """{"pluginId":"x","version":"1.0.0"}""")));
        Assert.Throws<InvalidDataException>(() => PluginPackageReader.Read(BuildPackage(manifestJson: """{"pluginId":"x","name":"x"}""")));
    }

    [Fact]
    public void Read_zip_slip_entry_should_throw()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write("""{"pluginId":"sample","name":"x","version":"1.0.0"}""");
            }
            zip.CreateEntry("../../evil.dll").Open().Dispose();
        }

        var ex = Assert.Throws<InvalidDataException>(() => PluginPackageReader.Read(ms.ToArray()));
        Assert.Contains("不安全", ex.Message);
    }

    [Fact]
    public void Read_non_zip_should_throw()
    {
        Assert.Throws<InvalidDataException>(() => PluginPackageReader.Read(System.Text.Encoding.UTF8.GetBytes("not a zip at all")));
    }

    [Fact]
    public void TryRead_invalid_should_return_false_with_reason()
    {
        Assert.False(PluginPackageReader.TryRead(System.Text.Encoding.UTF8.GetBytes("garbage"), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ExtractTo_should_extract_files_safely()
    {
        var target = Path.Combine(Path.GetTempPath(), $"lfextract-{Guid.NewGuid():N}");
        try
        {
            PluginPackageReader.ExtractTo(BuildPackage(), target);

            Assert.True(File.Exists(Path.Combine(target, PluginPackageReader.ManifestFileName)));
            Assert.True(File.Exists(Path.Combine(target, "LabelFrame.TransportPlugin.Sample.dll")));
        }
        finally
        {
            TryDeleteDirectory(target);
        }
    }

    [Fact]
    public void ExtractTo_should_reject_traversal_entry()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write("""{"pluginId":"sample","name":"x","version":"1.0.0"}""");
            }
            zip.CreateEntry("sub/../../evil.dll").Open().Dispose();
        }

        var target = Path.Combine(Path.GetTempPath(), $"lfextract-{Guid.NewGuid():N}");
        try
        {
            Assert.Throws<InvalidDataException>(() => PluginPackageReader.ExtractTo(ms.ToArray(), target));
        }
        finally
        {
            TryDeleteDirectory(target);
        }
    }

    internal static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败容忍（文件被加载中的程序集锁定）
        }
    }
}