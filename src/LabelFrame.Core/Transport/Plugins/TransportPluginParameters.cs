namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件参数值访问器：弱类型字典（string → string 持久化），按规格类型取强类型值。
/// </summary>
public sealed class TransportPluginParameters
{
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>创建参数访问器。</summary>
    /// <param name="values">参数字典（键 → 字符串值；缺省为空）。</param>
    public TransportPluginParameters(IReadOnlyDictionary<string, string>? values = null)
    {
        _values = values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>原始参数字典。</summary>
    public IReadOnlyDictionary<string, string> Raw => _values;

    /// <summary>是否包含键。</summary>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>取字符串（缺失返回 null）。</summary>
    public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>取字符串（缺失 / 空返回默认值）。</summary>
    public string GetString(string key, string defaultValue)
    {
        var v = GetString(key);
        return string.IsNullOrWhiteSpace(v) ? defaultValue : v;
    }

    /// <summary>取整数（缺失 / 解析失败返回 null）。</summary>
    public int? GetInt(string key) => int.TryParse(GetString(key), out var v) ? v : null;

    /// <summary>取整数（缺失 / 解析失败返回默认值）。</summary>
    public int GetInt(string key, int defaultValue) => GetInt(key) ?? defaultValue;

    /// <summary>取布尔（缺失 / 解析失败返回 null）。</summary>
    public bool? GetBool(string key) => bool.TryParse(GetString(key), out var v) ? v : null;

    /// <summary>取布尔（缺失 / 解析失败返回默认值）。</summary>
    public bool GetBool(string key, bool defaultValue) => GetBool(key) ?? defaultValue;

    /// <summary>取 Select 枚举值（与 GetString 等价）。</summary>
    public string? GetSelect(string key) => GetString(key);
}
