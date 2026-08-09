using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Templates;

/// <summary>模板包 zip 序列化：manifest.json（name / group / contract / layout）+ images/ 目录。</summary>
public static class TemplatePackageSerializer
{
    /// <summary>包内清单文件名。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>包内图片目录。</summary>
    public const string ImagesDirectory = "images";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };

    /// <summary>导出为 zip 字节。</summary>
    public static byte[] Export(TemplatePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new ManifestDto(package.Name, package.Group, package.Contract, package.Layout, package.TestData);
            var manifestEntry = archive.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
            using (var entryStream = manifestEntry.Open())
            {
                JsonSerializer.Serialize(entryStream, manifest, JsonOptions);
            }

            foreach (var (key, bytes) in package.Images)
            {
                var imageEntry = archive.CreateEntry($"{ImagesDirectory}/{SanitizeKey(key)}", CompressionLevel.Optimal);
                using var imageStream = imageEntry.Open();
                imageStream.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    /// <summary>从 zip 字节导入。</summary>
    public static TemplatePackage Import(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        using var stream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry(ManifestFileName)
            ?? throw new InvalidDataException("模板包缺少 manifest.json。");
        ManifestDto manifest;
        using (var entryStream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<ManifestDto>(entryStream, JsonOptions)
                ?? throw new InvalidDataException("manifest.json 解析为空。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Contract is null || manifest.Layout is null)
        {
            throw new InvalidDataException("manifest.json 缺少 name / contract / layout。");
        }

        var images = new Dictionary<string, byte[]>();
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith($"{ImagesDirectory}/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = Path.GetFileName(entry.FullName);
            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            images[key] = memory.ToArray();
        }

        return new TemplatePackage
        {
            Name = manifest.Name,
            Group = string.IsNullOrWhiteSpace(manifest.Group) ? "默认" : manifest.Group,
            Contract = manifest.Contract,
            Layout = manifest.Layout,
            Images = images,
            TestData = manifest.TestData ?? new Dictionary<string, string>(),
        };
    }

    private static string SanitizeKey(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return string.IsNullOrWhiteSpace(safe) ? "image" : safe;
    }

    private sealed record ManifestDto(
        string? Name,
        string? Group,
        LabelContract? Contract,
        LabelLayout? Layout,
        IReadOnlyDictionary<string, string>? TestData = null);
}