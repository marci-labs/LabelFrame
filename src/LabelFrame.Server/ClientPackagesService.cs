using LabelFrame.Core.IO;

namespace LabelFrame.Server;

/// <summary>客户端安装包视图（GET /api/client-packages 列表项）。</summary>
public sealed record ClientPackageView(string FileName, long SizeBytes, DateTimeOffset ModifiedAt, string Url);

/// <summary>
/// 客户端安装包目录服务：
/// 服务端统一分发客户端安装包——目录直放文件与页面上传都支持；文件名一律拒绝路径分隔符 / .. / 非法字符（路径穿越防护），
/// 只允许普通文件名（无子目录）。文件名规范化共享 Core <see cref="SafeFileName"/>（提取）。
/// </summary>
public sealed class ClientPackagesService : FilePackageService<ClientPackageView>
{
    /// <summary>创建服务（目录不存在自动创建）。</summary>
    public ClientPackagesService(string directory)
        : base(directory, nameof(directory))
    {
    }

    /// <summary>保存上传的安装包（文件名路径穿越防护；覆盖同名文件）。</summary>
    public async Task<ClientPackageView> SaveAsync(string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = ResolveSafePath(fileName)
            ?? throw new InvalidOperationException("文件名无效（只允许普通文件名，不允许路径 / 特殊字符）。");

        await using (var stream = File.Create(path))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        return ToView(path);
    }

    protected override ClientPackageView ToView(string path)
    {
        var info = new FileInfo(path);
        return new ClientPackageView(
            info.Name,
            info.Length,
            info.LastWriteTimeUtc,
            $"/api/client-packages/{Uri.EscapeDataString(info.Name)}");
    }
}
