namespace LabelFrame.Core.Contracts;

/// <summary>契约中的单个字段定义。</summary>
public sealed class LabelField
{
    /// <summary>字段键，版式元素通过该键绑定数据。</summary>
    public required string Key { get; init; }

    /// <summary>展示名称（中文），用于错误提示与文档。</summary>
    public required string DisplayName { get; init; }

    /// <summary>是否必填；必填缺失时校验拒绝打印。</summary>
    public bool IsRequired { get; init; }

    /// <summary>字段值类型。</summary>
    public LabelFieldType Type { get; init; } = LabelFieldType.Text;

    /// <summary>可选格式约束（正则），迭代 1 仅作契约元数据，校验暂不执行。</summary>
    public string? Pattern { get; init; }
}