namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件参数规格：前端按此动态渲染参数表单（文本 / 数字 / 开关 / 下拉），
/// 后端据此做必填与类型校验。
/// </summary>
public sealed record TransportParameterSpec(
    string Key,
    string Label,
    TransportParameterType Type,
    bool Required = false,
    string? DefaultValue = null,
    IReadOnlyList<TransportParameterOption>? Options = null,
    string? Hint = null);
