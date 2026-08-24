using System.Text.Json;

namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件包 manifest（zip 根 manifest.json）：
/// pluginId / name / version 必填；description / author / minHostVersion 可选
/// （minHostVersion 本轮仅展示、暂不校验，有厂商插件接入需求时再评估）。
/// </summary>
public sealed record PluginPackageManifest(
    string PluginId,
    string Name,
    string Version,
    string? Description = null,
    string? Author = null,
    string? MinHostVersion = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>解析 manifest JSON；必填字段缺失 / 类型错误抛 InvalidDataException（中文消息）。</summary>
    public static PluginPackageManifest Parse(string json)
    {
        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"manifest.json 不是有效 JSON：{ex.Message}");
        }

        if (dto is null)
        {
            throw new InvalidDataException("manifest.json 内容为空。");
        }

        if (string.IsNullOrWhiteSpace(dto.PluginId))
        {
            throw new InvalidDataException("manifest.json 缺少必填字段 pluginId。");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new InvalidDataException("manifest.json 缺少必填字段 name。");
        }

        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            throw new InvalidDataException("manifest.json 缺少必填字段 version。");
        }

        return new PluginPackageManifest(
            dto.PluginId.Trim(),
            dto.Name.Trim(),
            dto.Version.Trim(),
            dto.Description,
            dto.Author,
            dto.MinHostVersion);
    }

    private sealed record ManifestDto(string? PluginId, string? Name, string? Version, string? Description, string? Author, string? MinHostVersion);
}