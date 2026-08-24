using LabelFrame.Core.IO;

namespace LabelFrame.Server;

/// <summary>
/// 文件包目录服务基类（客户端安装包 / 传输插件包共享）：目录管理、路径穿越防护、列表 / 查询 / 删除。
/// 子类实现 <see cref="SaveAsync"/> 的校验与 <see cref="ToView"/> 的视图（文件名一律经 <see cref="SafeFileName"/> 规范化）。
/// </summary>
public abstract class FilePackageService<TView>
    where TView : class
{
    /// <summary>包目录（绝对路径，构造时已确保存在）。</summary>
    private protected string DirectoryPath { get; }

    /// <summary>创建服务（目录不存在自动创建）。</summary>
    protected FilePackageService(string directory, string directoryArgName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("目录不能为空。", directoryArgName);
        }

        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>包列表（按文件修改时间倒序）。</summary>
    public IReadOnlyList<TView> List()
        => Directory.GetFiles(DirectoryPath)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => ToView(info.FullName))
            .ToList();

    /// <summary>取包视图（不存在 / 文件名非法返回 null）。</summary>
    public TView? Get(string fileName)
        => Resolve(fileName) is { } path ? ToView(path) : null;

    /// <summary>取下载文件路径（不存在 / 文件名非法返回 null）。</summary>
    public string? GetDownloadPath(string fileName) => Resolve(fileName);

    /// <summary>删除包（不存在 / 文件名非法返回 false）。</summary>
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

    /// <summary>规范化文件名并拼出目录内路径；文件名非法返回 null（路径穿越防护）。</summary>
    protected string? ResolveSafePath(string? fileName)
        => SafeFileName.Normalize(fileName) is { } safeName ? Path.Combine(DirectoryPath, safeName) : null;

    private string? Resolve(string fileName)
        => ResolveSafePath(fileName) is { } path && File.Exists(path) ? path : null;

    /// <summary>文件路径 → 视图。</summary>
    protected abstract TView ToView(string path);
}
