namespace LabelFrame.Server;

/// <summary>客户端安装包视图（GET /api/client-packages 列表项）。</summary>
public sealed record ClientPackageView(string FileName, long SizeBytes, DateTimeOffset ModifiedAt, string Url);

/// <summary>
/// 客户端安装包目录服务（迭代 22 §2.3 / §5.4，决策 #71）：
/// 服务端统一分发客户端安装包——目录直放文件与页面上传都支持；文件名一律拒绝路径分隔符 / .. / 非法字符（路径穿越防护），
/// 只允许普通文件名（无子目录）。
/// </summary>
public sealed class ClientPackagesService
{
    private readonly string _directory;

    /// <summary>创建服务（目录不存在自动创建）。</summary>
    public ClientPackagesService(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("安装包目录不能为空。", nameof(directory));
        }

        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>安装包列表（按修改时间倒序）。</summary>
    public IReadOnlyList<ClientPackageView> List()
        => Directory.GetFiles(_directory)
            .Select(ToView)
            .OrderByDescending(v => v.ModifiedAt)
            .ToList();

    /// <summary>保存上传的安装包（文件名路径穿越防护；覆盖同名文件）。</summary>
    public async Task<ClientPackageView> SaveAsync(string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var safeName = NormalizeFileName(fileName)
            ?? throw new InvalidOperationException("文件名无效（只允许普通文件名，不允许路径 / 特殊字符）。");

        var path = Path.Combine(_directory, safeName);
        await using (var stream = File.Create(path))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        return ToView(path);
    }

    /// <summary>取安装包视图（不存在 / 文件名非法返回 null）。</summary>
    public ClientPackageView? Get(string fileName)
    {
        var path = Resolve(fileName);
        return path is null ? null : ToView(path);
    }

    /// <summary>取下载文件路径（不存在 / 文件名非法返回 null）。</summary>
    public string? GetDownloadPath(string fileName) => Resolve(fileName);

    /// <summary>删除安装包（不存在 / 文件名非法返回 false）。</summary>
    public bool Delete(string fileName)
    {
        var path = Resolve(fileName);
        if (path is null)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string? Resolve(string fileName)
    {
        var safeName = NormalizeFileName(fileName);
        if (safeName is null)
        {
            return null;
        }

        var path = Path.Combine(_directory, safeName);
        return File.Exists(path) ? path : null;
    }

    private ClientPackageView ToView(string path)
    {
        var info = new FileInfo(path);
        return new ClientPackageView(
            info.Name,
            info.Length,
            info.LastWriteTimeUtc,
            $"/api/client-packages/{Uri.EscapeDataString(info.Name)}");
    }

    /// <summary>文件名规范化：仅允许普通文件名（拒绝路径分隔符 / .. / 非法字符，路径穿越防护）。</summary>
    private static string? NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = fileName.Trim();
        if (name is "." or ".." || name.Contains('/') || name.Contains('\\'))
        {
            return null;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return name;
    }
}
