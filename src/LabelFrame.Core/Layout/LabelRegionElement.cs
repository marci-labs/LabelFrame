namespace LabelFrame.Core.Layout;

/// <summary>
/// 区域（格子）容器：用于把版面划分成多个格子，元素通过 RegionId 锚定并在格内对齐。
/// 边框线宽由 <see cref="LabelElement.BorderMm"/> 控制（0 = 不打印边框）。
/// </summary>
public sealed class LabelRegionElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.Region;

    /// <summary>区域标识，元素通过 RegionId 引用。</summary>
    public required string Id { get; init; }

    /// <summary>区域宽度（毫米）。</summary>
    public double WidthMm { get; init; }

    /// <summary>区域高度（毫米）。</summary>
    public double HeightMm { get; init; }
}