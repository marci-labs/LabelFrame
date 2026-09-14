using System.IO.Compression;
using System.Text.Json;
using LabelFrame.Bootstrapper.Prerequisites;

namespace LabelFrame.Bootstrapper.Placement;

/// <summary>落位决策结果（.lfplugin 的 #123 版本比较口径：覆盖 / 同版幂等 / 降级拒绝）。</summary>
public enum PlacementOutcome
{
    /// <summary>已解压落位（新装或新版本覆盖）。</summary>
    Placed,

    /// <summary>同版本幂等跳过（不重复解压，日志留痕）。</summary>
    SkippedSameVersion,

    /// <summary>降级拒绝（需先卸载再安装）。</summary>
    RefusedDowngrade,
}

/// <summary>
/// 非 MSI 组件落位（迭代 62，决策 #124 机制 A：ExePackage 包装解压工具；本类为工具内核，net48 / net10 双腿可测）。
/// 目标目录约定对齐 DEPLOY §5 / §6 与 <c>InstallTargets</c>：webui zip → <c>plugins\web-ui</c>；<c>.lfplugin</c> → <c>plugins\&lt;pluginId&gt;\</c>。
/// </summary>
/// <remarks>
/// zip 均按「清空目标目录后整包解压」执行（目录内容完全由包拥有，避免覆盖残留陈旧文件）；
/// <c>.lfplugin</c> 额外执行 #123 版本比较——包内 manifest.json.version 与已装目录 manifest.json.version：
/// 新版本覆盖、相同版本幂等跳过、旧版本拒绝。比较语义对齐决策 #123（双方可解析 <see cref="Version"/> 按其比较，否则字符串 Ordinal）。
/// </remarks>
public static class PayloadPlacer
{
    /// <summary>webui zip：覆盖落位（无独立版本概念，重跑重写同内容）。</summary>
    public static PlacementOutcome PlaceArchive(string archivePath, string targetDir)
    {
        ValidateArchive(archivePath);
        ReplaceTarget(archivePath, targetDir);
        return PlacementOutcome.Placed;
    }

    /// <summary>.lfplugin：包内 manifest.json 版本与已装版本比较后落位（#123 口径）。</summary>
    public static PlacementOutcome PlacePlugin(string archivePath, string targetDir)
    {
        ValidateArchive(archivePath);

        var incomingVersion = ReadPluginVersionFromArchive(archivePath)
            ?? throw new InvalidOperationException($"插件包 {archivePath} 缺少根 manifest.json 或其 version 字段，拒绝落位。");

        var installedManifestPath = Path.Combine(targetDir, "manifest.json");
        if (File.Exists(installedManifestPath))
        {
            var installedVersion = ReadPluginVersionFromFile(installedManifestPath);
            if (installedVersion is not null)
            {
                var order = ComparePluginVersions(incomingVersion, installedVersion);
                if (order == 0)
                {
                    return PlacementOutcome.SkippedSameVersion;
                }

                if (order < 0)
                {
                    return PlacementOutcome.RefusedDowngrade;
                }
            }
        }

        ReplaceTarget(archivePath, targetDir);
        return PlacementOutcome.Placed;
    }

    /// <summary>读取包内（zip 根）manifest.json 的 version（不存在 / 非法返回 null）。</summary>
    internal static string? ReadPluginVersionFromArchive(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException($"插件包 {archivePath} 缺少根 manifest.json（.lfplugin 契约：pluginId / name / version 必填）。");
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;
    }

    private static string? ReadPluginVersionFromFile(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;
    }

    /// <summary>版本比较（#123：双方可解析 System.Version 按其比较（缺失段视为 0），否则字符串 Ordinal）。&gt;0 = 左新，&lt;0 = 左旧。</summary>
    public static int ComparePluginVersions(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            throw new ArgumentException("版本号不能为空。", nameof(left));
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            throw new ArgumentException("版本号不能为空。", nameof(right));
        }

        var leftVersion = RuntimeProbe.ParseVersion(left);
        var rightVersion = RuntimeProbe.ParseVersion(right);
        if (leftVersion is not null && rightVersion is not null)
        {
            // 缺失段视为 0（Version.CompareTo 把未指定段按 -1 判小，与 #123 的 1.0 == 1.0.0 语义不符——对齐 Core 的 PluginVersionComparer）
            return Normalize(leftVersion).CompareTo(Normalize(rightVersion));
        }

        return string.Compare(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }

    private static Version Normalize(Version version)
        => new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));

    private static void ValidateArchive(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"落位包不存在：{archivePath}", archivePath);
        }
    }

    /// <summary>清空目标目录后整包解压（目录内容完全由包拥有；解压原子性以「先解压到同级临时目录再替换」近似）。</summary>
    private static void ReplaceTarget(string archivePath, string targetDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(targetDir));
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidOperationException($"目标目录不合法：{targetDir}");
        }

        Directory.CreateDirectory(parent);

        var staging = targetDir + ".staging";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        try
        {
            ZipFile.ExtractToDirectory(archivePath, staging);

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.Move(staging, targetDir);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (IOException)
                {
                    // 残留清理失败不阻断落位结果（下次落位会先行清理）
                }
            }
        }
    }
}
