namespace LabelFrame.Core.Layout;

/// <summary>标签版式：尺寸 + 元素清单，引用契约名称与版本，毫米坐标。</summary>
public sealed class LabelLayout
{
    /// <summary>版式名称。</summary>
    public required string Name { get; init; }

    /// <summary>引用的契约名称。</summary>
    public required string ContractName { get; init; }

    /// <summary>引用的契约版本。</summary>
    public required string ContractVersion { get; init; }

    /// <summary>标签宽度（毫米）。</summary>
    public double WidthMm { get; init; }

    /// <summary>标签高度（毫米）。</summary>
    public double HeightMm { get; init; }

    /// <summary>元素清单（编码按清单顺序输出）。</summary>
    public required IReadOnlyList<LabelElement> Elements { get; init; }
}