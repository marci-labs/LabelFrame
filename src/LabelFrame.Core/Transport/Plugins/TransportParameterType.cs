namespace LabelFrame.Core.Transport.Plugins;

/// <summary>传输插件参数类型（前端按此渲染表单控件）。</summary>
public enum TransportParameterType
{
    /// <summary>文本输入。</summary>
    String,

    /// <summary>整数输入。</summary>
    Int,

    /// <summary>布尔开关。</summary>
    Bool,

    /// <summary>枚举下拉（配合 Options）。</summary>
    Select,
}
