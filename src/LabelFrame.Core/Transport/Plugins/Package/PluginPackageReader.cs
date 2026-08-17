using System.IO.Compression;

namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件包 zip 读取（迭代 23 决策 1A/5A）：根 manifest.json + 插件 DLL；
/// zip-slip 防护（拒绝绝对路径 / 盘符 / .. 段）；不落地解压（服务端列表 / 上传校验用）。
/// </summary>
public static class PluginPackageReader
{
    /// <summary>包内清单文件名。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>插件包内容：manifest + 包内 DLL 条目名（zip 相对路径）。</summary>
    public sealed record PluginPackageContent(PluginPackageManifest Manifest, IReadOnlyList<string> DllEntries);

    /// <summary>
    /// 读取插件包：校验 zip / 根 manifest / 必填字段 / 条目名安全（zip-slip）。
    /// 失败抛 InvalidDataException（中文消息）。
    /// </summary>
    public static PluginPackageContent Read(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        using var stream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry(ManifestFileName)
            ?? throw new InvalidDataException($"插件包缺少根 {ManifestFileName}。");
        string manifestJson;
        using (var entryStream = manifestEntry.Open())
        using (var reader = new StreamReader(entryStream))
        {
            manifestJson = reader.ReadToEnd();
        }

        var manifest = PluginPackageManifest.Parse(manifestJson);

        var dlls = new List<string>();
        foreach (var entry in archive.Entries)
        {
            if (!IsSafeEntryName(entry.FullName))
            {
                throw new InvalidDataException($"插件包含不安全的条目名：{entry.FullName}。");
            }

            if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                dlls.Add(entry.FullName);
            }
        }

        if (dlls.Count == 0)
        {
            throw new InvalidDataException("插件包未包含任何 DLL（至少需要实现 ITransportPlugin 的主 DLL）。");
        }

        return new PluginPackageContent(manifest, dlls);
    }

    /// <summary>尝试读取；失败返回 false + 中文原因（服务端列表展示用，invalid 条目仍可删除）。</summary>
    public static bool TryRead(byte[] zipBytes, out PluginPackageContent? content, out string? error)
    {
        try
        {
            content = Read(zipBytes);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            content = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>安全解压到目标目录（zip-slip 防护：仅允许安全相对路径；目录条目跳过）。</summary>
    public static void ExtractTo(byte[] zipBytes, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        Directory.CreateDirectory(targetDirectory);
        var rootFull = Path.GetFullPath(targetDirectory);

        using var stream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // 目录条目
            }

            if (!IsSafeEntryName(entry.FullName))
            {
                throw new InvalidDataException($"插件包含不安全的条目名：{entry.FullName}。");
            }

            var destPath = Path.Combine(rootFull, entry.FullName.Replace('\\', '/'));
            var fullPath = Path.GetFullPath(destPath);
            if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"插件包含越界条目：{entry.FullName}。");
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var entryStream = entry.Open();
            using var fileStream = File.Create(fullPath);
            entryStream.CopyTo(fileStream);
        }
    }

    /// <summary>条目名安全校验（zip-slip 防护）：拒绝绝对路径 / 盘符 / . 与 .. 段 / 非法字符。</summary>
    public static bool IsSafeEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return false;
        }

        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }

            if (segment.Contains(':') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}