namespace LabelFrame.Core.Transport.Plugins;

/// <summary>传输插件参数类型（前端按此渲染表单控件）。</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:标识符不应包含类型名称",
    Justification = "成员名即对前端的 JSON 契约值（type: String/Int/Bool/Select），改名破坏前后端契约")]
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
