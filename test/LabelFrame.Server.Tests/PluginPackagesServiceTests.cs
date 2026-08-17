using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.Server.Tests;

public class PluginPackagesServiceTests
{
    private static (PluginPackagesService Service, string Dir) Create()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfplugins-pkg-{Guid.NewGuid():N}");
        return (new PluginPackagesService(dir), dir);
    }

    private static byte[] BuildPackage(string pluginId = "sample", string name = "示例插件", string version = "1.0.0", string? manifestJson = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write(manifestJson ?? $$"""{"pluginId":"{{pluginId}}","name":"{{name}}","version":"{{version}}","description":"测试包"}""");
            }
            zip.CreateEntry("LabelFrame.TransportPlugin.Sample.dll").Open().Dispose();
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Save_then_list_should_return_metadata_with_valid_true()
    {
        var (svc, dir) = Create();
        try
        {
            var saved = await svc.SaveAsync("sample-1.0.0.lfplugin", new MemoryStream(BuildPackage()));
            Assert.True(saved.Valid);
            Assert.Equal("sample", saved.PluginId);
            Assert.Equal("示例插件", saved.Name);
            Assert.Equal("1.0.0", saved.Version);

            var list = svc.List();
            var view = Assert.Single(list);
            Assert.Equal("sample-1.0.0.lfplugin", view.FileName);
            Assert.True(view.Valid);
            Assert.StartsWith("/api/plugin-packages/", view.Url);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_invalid_zip_should_throw_and_not_persist()
    {
        var (svc, dir) = Create();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => svc.SaveAsync("bad.lfplugin", new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_missing_manifest_or_field_should_throw()
    {
        var (svc, dir) = Create();
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("other.json").Open().Dispose();
            }

            await Assert.ThrowsAsync<InvalidDataException>(() => svc.SaveAsync("no-manifest.lfplugin", new MemoryStream(ms.ToArray())));
            await Assert.ThrowsAsync<InvalidDataException>(() => svc.SaveAsync("no-field.lfplugin", new MemoryStream(BuildPackage(manifestJson: """{"pluginId":"x","name":"y"}"""))));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Traversal_file_name_should_be_rejected()
    {
        var (svc, dir) = Create();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => svc.SaveAsync("..\\..\\evil.lfplugin", new MemoryStream(BuildPackage())));
            Assert.Null(svc.GetDownloadPath("..\\..\\evil.lfplugin"));
            Assert.Null(svc.GetDownloadPath("../evil.lfplugin"));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_existing_should_return_true_and_remove()
    {
        var (svc, dir) = Create();
        try
        {
            await svc.SaveAsync("a.lfplugin", new MemoryStream(BuildPackage()));
            Assert.True(svc.Delete("a.lfplugin"));
            Assert.Null(svc.Get("a.lfplugin"));
            Assert.False(svc.Delete("a.lfplugin"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Get_missing_should_return_null()
    {
        var (svc, dir) = Create();
        try
        {
            Assert.Null(svc.Get("no-such.lfplugin"));
            Assert.Null(svc.GetDownloadPath("no-such.lfplugin"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task List_should_mark_preplaced_invalid_file_valid_false()
    {
        var (svc, dir) = Create();
        try
        {
            // 目录直放非法文件（不经 API 上传）：解析失败 → valid:false + 原因，仍列出可删除
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "broken.lfplugin"), "garbage");

            var list = svc.List();
            var view = Assert.Single(list);
            Assert.False(view.Valid);
            Assert.False(string.IsNullOrWhiteSpace(view.InvalidReason));
            Assert.Null(view.PluginId);

            Assert.True(svc.Delete("broken.lfplugin"));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_over_limit_should_be_rejected()
    {
        var (svc, dir) = Create();
        try
        {
            // 65MB 空字节（超 64MB 上限；不落盘）
            var big = new byte[PluginPackageLimits.MaxBytes + 1];
            await Assert.ThrowsAsync<InvalidDataException>(() => svc.SaveAsync("big.lfplugin", new MemoryStream(big)));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}