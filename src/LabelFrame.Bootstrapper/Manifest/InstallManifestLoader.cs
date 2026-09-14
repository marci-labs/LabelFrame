namespace LabelFrame.Bootstrapper.Manifest;

/// <summary>安装清单加载器：来源支持本地路径（含离线全量包内嵌清单场景）或 URL（稳定通道 / 指定版本 Release）。</summary>
/// <remarks>
/// dry-run 契约锚点（DESIGN §6.3 实现要点、Issue #53 AC-03）：清单获取是引导全程唯一的网络访问——
/// 只读 GET、不带任何写语义；本地路径则零网络访问。下载 / 安装能力归 #54 / #55，本轮不存在。
/// </remarks>
public static class InstallManifestLoader
{
    private static readonly HttpClient SharedClient = new();

    /// <summary>加载并解析清单。<paramref name="http"/> 可注入用于测试（记录请求以断言「仅一次 GET」）。</summary>
    public static async Task<InstallManifest> LoadAsync(string source, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InstallManifestFormatException("清单来源为空：请填写本地文件路径或 URL。");
        }

        var json = IsHttpUrl(source)
            ? await DownloadAsync(source, http ?? SharedClient, cancellationToken).ConfigureAwait(false)
            : await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);

        return InstallManifest.Parse(json);
    }

    private static async Task<string> DownloadAsync(string url, HttpClient http, CancellationToken cancellationToken)
    {
        // 只读 GET（无网络写入；AC-03 测试断言锚点）
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallManifestFormatException($"下载清单失败：HTTP {(int)response.StatusCode}（{url}）。");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsHttpUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
