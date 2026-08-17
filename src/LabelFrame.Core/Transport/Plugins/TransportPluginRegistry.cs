using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件注册表实现：注册（内置 / 外部）→ 按 ID 装配。插件 ID 忽略大小写。
/// </summary>
public sealed class TransportPluginRegistry : ITransportPluginRegistry
{
    private readonly Dictionary<string, (TransportPluginDescriptor Descriptor, ITransportPlugin Plugin)> _plugins =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册插件（内置 IsExternal=false；外部 DLL 由目录加载器注册并附来源路径）。</summary>
    public void Register(ITransportPlugin plugin, bool isExternal = false, string? assemblyPath = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.Id))
        {
            throw new ArgumentException("插件 ID 不能为空。", nameof(plugin));
        }

        _plugins[plugin.Id] = (
            new TransportPluginDescriptor(plugin.Id, plugin.DisplayName, plugin.Description, plugin.Parameters, isExternal, assemblyPath),
            plugin);
    }

    /// <summary>
    /// 注册外部插件（迭代 23 决策 6A）：外部插件不允许覆盖内置 / 非外部插件 ID——
    /// 冲突时记录日志并跳过（返回 false），避免误装 / 恶意包覆盖内置功能。
    /// </summary>
    public bool RegisterExternal(ITransportPlugin plugin, string? assemblyPath, TextWriter hostLog)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(hostLog);
        if (string.IsNullOrWhiteSpace(plugin.Id))
        {
            throw new ArgumentException("插件 ID 不能为空。", nameof(plugin));
        }

        if (_plugins.TryGetValue(plugin.Id, out var existing) && !existing.Descriptor.IsExternal)
        {
            hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 外部传输插件 {plugin.Id}（{plugin.DisplayName}）与内置插件 ID 冲突，已跳过（决策 6A）。");
            return false;
        }

        Register(plugin, isExternal: true, assemblyPath: assemblyPath);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<TransportPluginDescriptor> ListPlugins()
        => _plugins.Values.Select(p => p.Descriptor).ToList();

    /// <inheritdoc />
    public TransportPluginDescriptor? GetPlugin(string id)
        => id is not null && _plugins.TryGetValue(id, out var p) ? p.Descriptor : null;

    /// <inheritdoc />
    public IPrintTransport CreateTransport(string id, TransportPluginParameters parameters, ITransportPluginContext context)
    {
        if (id is null || !_plugins.TryGetValue(id, out var p))
        {
            throw new InvalidOperationException($"传输插件不存在：{id}。");
        }

        return p.Plugin.Create(parameters, context);
    }

    /// <inheritdoc />
    public string Describe(string id, TransportPluginParameters parameters)
        => id is not null && _plugins.TryGetValue(id, out var p) ? p.Plugin.Describe(parameters) : id ?? "未知插件";
}
