using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Layout;

namespace LabelFrame.Server;

/// <summary>路由 API 的 JSON 选项（版式元素转换器 + 枚举字符串）。</summary>
public static class RoutingJson
{
    /// <summary>共享 JSON 选项。</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };
}