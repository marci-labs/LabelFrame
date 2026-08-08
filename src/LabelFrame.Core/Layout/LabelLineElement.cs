namespace LabelFrame.Core.Layout;

/// <summary>线元素：从 (XMm, YMm) 到 (X2Mm, Y2Mm)。</summary>
public sealed class LabelLineElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.Line;

    /// <summary>终点 X 坐标（毫米）。</summary>
    public double X2Mm { get; init; }

    /// <summary>终点 Y 坐标（毫米）。</summary>
    public double Y2Mm { get; init; }

    /// <summary>线宽（毫米）。</summary>
    public double ThicknessMm { get; init; }
}