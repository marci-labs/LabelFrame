namespace LabelFrame.Core.Layout;

/// <summary>图片元素，绑定图片数据键（迭代 1 编码为占位）。</summary>
public sealed class LabelImageElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.Image;

    /// <summary>绑定的图片数据键。</summary>
    public required string SourceKey { get; init; }

    /// <summary>图片显示宽度（毫米）。</summary>
    public double WidthMm { get; init; }

    /// <summary>图片显示高度（毫米）。</summary>
    public double HeightMm { get; init; }
}