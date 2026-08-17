using LabelFrame.Core.IO;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.WinHost.Transport;

/// <summary>已安装插件视图（GET /api/plugins/installed，迭代 23 §5.2）。</summary>
public sealed record InstalledPluginView(
    string PluginId,
    string Name,
    string Version,
    string? Description,
    bool Loaded,
    string? LoadError,
    string? PackageDir,
    string Source,
    DateTimeOffset? InstalledAt);


/// <summary>
/// 客户端插件安装 / 卸载服务（迭代 23 §5.2 / §6.2，决策 3A/4A/5A/6A）：
/// 安装 = 三层校验（zip + manifest / 内置 id 拒绝 / 临时 ALC 预检）→ 解压到 plugins/&lt;pluginId&gt;/（覆盖旧目录）→ 重启生效；
/// 卸载 = 删除 plugins/&lt;pluginId&gt;/ → 重启生效；运行时热卸载不做。
/// </summary>
public sealed class PluginInstaller
{
    private readonly string _pluginsPath;
    private readonly ITransportPluginRegistry _registry;
    private readonly TextWriter _hostLog;
    private readonly IReadOnlyDictionary<string, string> _lastLoadErrors;

    /// <summary>创建安装服务（插件目录不存在自动创建）。</summary>
    public PluginInstaller(string pluginsPath, ITransportPluginRegistry registry, TextWriter hostLog,
        IReadOnlyDictionary<string, string>? lastLoadErrors = null)
    {
        _pluginsPath = pluginsPath ?? throw new ArgumentNullException(nameof(pluginsPath));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hostLog = hostLog ?? throw new ArgumentNullException(nameof(hostLog));
        _lastLoadErrors = lastLoadErrors ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 已安装插件列表：子目录安装包（source=package，读 manifest）+ 平铺手动 DLL（source=manual，只读）。
    /// loaded = 注册表已装配（该插件 ID 来自本目录）；manifest 损坏的包目录仍列出并给出原因。
    /// </summary>
    public IReadOnlyList<InstalledPluginView> ListInstalled()
    {
        var views = new List<InstalledPluginView>();
        if (!Directory.Exists(_pluginsPath))
        {
            return views;
        }

        // 子目录安装包
        foreach (var dir in Directory.GetDirectories(_pluginsPath))
        {
            var manifestPath = Path.Combine(dir, PluginPackageReader.ManifestFileName);
            var dirName = Path.GetFileName(dir);
            if (!File.Exists(manifestPath))
            {
                continue; // 非安装包子目录（管理员手动组织），不列出
            }

            try
            {
                var manifest = PluginPackageManifest.Parse(File.ReadAllText(manifestPath));
                var loaded = IsLoadedFrom(dir, manifest.PluginId);
                var loadError = loaded ? null : FindLoadError(dir);
                views.Add(new InstalledPluginView(
                    manifest.PluginId,
                    manifest.Name,
                    manifest.Version,
                    manifest.Description,
                    loaded,
                    LoadError: loadError,
                    dirName,
                    Source: "package",
                    Directory.GetCreationTimeUtc(dir)));
            }
            catch (Exception ex)
            {
                views.Add(new InstalledPluginView(
                    dirName,
                    dirName,
                    "?",
                    null,
                    Loaded: false,
                    $"manifest 解析失败：{ex.Message}",
                    dirName,
                    Source: "package",
                    Directory.GetCreationTimeUtc(dir)));
            }
        }

        // 平铺手动 DLL（无安装包归属，只读）
        foreach (var dll in Directory.GetFiles(_pluginsPath, "*.dll"))
        {
            var descriptor = _registry.ListPlugins()
                .FirstOrDefault(d => string.Equals(d.AssemblyPath, dll, StringComparison.OrdinalIgnoreCase));
            views.Add(new InstalledPluginView(
                descriptor?.Id ?? Path.GetFileNameWithoutExtension(dll),
                descriptor?.DisplayName ?? Path.GetFileNameWithoutExtension(dll),
                "?",
                null,
                descriptor is not null,
                LoadError: descriptor is not null ? null : (_lastLoadErrors.TryGetValue(dll, out var msg) ? msg : null),
                PackageDir: null,
                Source: "manual",
                File.GetLastWriteTimeUtc(dll)));
        }

        return views;
    }

    /// <summary>
    /// 安装插件包：三层校验（zip + 根 manifest / 内置 id 拒绝 / 临时 ALC 预检核对插件 id）→
    /// 解压到 plugins/&lt;pluginId&gt;/（覆盖旧目录）。失败抛 InvalidDataException / IOException（中文消息）。
    /// </summary>
    public async Task<InstalledPluginView> InstallAsync(Stream packageStream, string? fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        using var buffer = new MemoryStream();
        await packageStream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new InvalidDataException("插件包为空。");
        }

        if (buffer.Length > PluginPackageLimits.MaxBytes)
        {
            throw new InvalidDataException($"插件包超过大小上限（{PluginPackageLimits.Display}）。");
        }

        var bytes = buffer.ToArray();

        // ① zip 完整性 + 根 manifest + 必填字段 + zip-slip（PluginPackageReader.Read 内部校验）
        var content = PluginPackageReader.Read(bytes);

        // ② 内置插件 ID 拒绝（决策 6A）
        var existing = _registry.GetPlugin(content.Manifest.PluginId);
        if (existing is { IsExternal: false })
        {
            throw new InvalidDataException($"插件 ID「{content.Manifest.PluginId}」与内置插件冲突，禁止安装（决策 6A）。");
        }

        // pluginId 目录名安全校验（防解压路径穿越）
        var safeId = SafeFileName.Normalize(content.Manifest.PluginId)
            ?? throw new InvalidDataException($"pluginId「{content.Manifest.PluginId}」不是合法的插件目录名。");

        // ③ 临时目录解压 + 预检（临时 collectible ALC 发现插件并核对 id，不 Create 传输实例）
        var tempDir = Path.Combine(Path.GetTempPath(), $"lfplugin-install-{Guid.NewGuid():N}");
        try
        {
            PluginPackageReader.ExtractTo(bytes, tempDir);
            var discovered = PluginProbe.DiscoverPluginIds(tempDir, _hostLog);
            if (discovered.Count == 0)
            {
                throw new InvalidDataException("插件包内未发现 ITransportPlugin 实现。");
            }

            if (!discovered.Contains(content.Manifest.PluginId, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"manifest.pluginId「{content.Manifest.PluginId}」与插件实际 ID（{string.Join(" / ", discovered)}）不一致。");
            }

            // 覆盖安装：删除旧目录（已加载插件被 Windows 文件锁占用 → 明确提示重启后重试）
            Directory.CreateDirectory(_pluginsPath);
            var targetDir = Path.Combine(_pluginsPath, safeId);
            if (Directory.Exists(targetDir))
            {
                try
                {
                    Directory.Delete(targetDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"插件「{content.Manifest.PluginId}」正在使用中（DLL 被客户端占用），请重启客户端后重试。");
                }
            }

            try
            {
                Directory.Move(tempDir, targetDir);
            }
            catch (IOException)
            {
                // 跨卷回退：复制 + 清理临时目录
                CopyDirectory(tempDir, targetDir);
                TryDeleteDirectory(tempDir);
            }

            _hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 已安装插件包：{content.Manifest.PluginId}（{content.Manifest.Name} {content.Manifest.Version}，重启后生效）。");
            return new InstalledPluginView(
                content.Manifest.PluginId,
                content.Manifest.Name,
                content.Manifest.Version,
                content.Manifest.Description,
                Loaded: false,
                LoadError: null,
                Path.GetFileName(targetDir),
                Source: "package",
                Directory.GetCreationTimeUtc(targetDir));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 卸载已安装插件包：删除 plugins/&lt;pluginId&gt;/（仅 source=package，需包内 manifest）。
    /// 失败抛 InvalidDataException / IOException（中文消息）。
    /// </summary>
    public void Uninstall(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new InvalidDataException("缺少插件 ID。");
        }

        var safeId = SafeFileName.Normalize(pluginId)
            ?? throw new InvalidDataException($"pluginId「{pluginId}」不是合法的插件目录名。");

        var targetDir = Path.Combine(_pluginsPath, safeId);
        if (!Directory.Exists(targetDir))
        {
            throw new InvalidDataException($"插件「{pluginId}」未安装。");
        }

        if (!File.Exists(Path.Combine(targetDir, PluginPackageReader.ManifestFileName)))
        {
            throw new InvalidDataException("该插件为手动放置（无安装包 manifest），不支持界面卸载，请手动删除插件目录。");
        }

        try
        {
            Directory.Delete(targetDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"插件「{pluginId}」正在使用中（DLL 被客户端占用），请重启客户端后重试。");
        }

        _hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 已卸载插件包：{pluginId}（重启后生效）。");
    }

    /// <summary>取目录内首个加载失败的 DLL 错误消息（未加载时透出启动期 loadError，迭代 23 附二拍板）。</summary>
    private string? FindLoadError(string dir)
        => _lastLoadErrors.FirstOrDefault(kv => kv.Key.StartsWith(dir, StringComparison.OrdinalIgnoreCase)).Value;
    /// <summary>插件是否从指定目录加载（注册表描述 AssemblyPath 位于该目录下且 id 匹配）。</summary>
    private bool IsLoadedFrom(string dir, string pluginId)
    {
        var descriptor = _registry.GetPlugin(pluginId);
        return descriptor is { IsExternal: true } &&
               !string.IsNullOrEmpty(descriptor.AssemblyPath) &&
               descriptor.AssemblyPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var sub in Directory.GetDirectories(source))
        {
            CopyDirectory(sub, Path.Combine(target, Path.GetFileName(sub)));
        }
    }

    private static void TryDeleteDirectory(string dir)
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
            // 临时目录清理失败不阻断（文件被 collectible ALC 锁定，进程退出后由系统清理）
        }
    }
}
