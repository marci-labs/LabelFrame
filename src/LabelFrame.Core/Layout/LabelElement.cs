namespace LabelFrame.Core.Layout;

/// <summary>版式元素的抽象基类，所有元素使用毫米坐标。</summary>
public abstract class LabelElement
{
    /// <summary>左上角 X 坐标（毫米）。</summary>
    public double XMm { get; init; }

    /// <summary>左上角 Y 坐标（毫米）。</summary>
    public double YMm { get; init; }

    /// <summary>元素类型。</summary>
    public abstract LabelElementType Type { get; }
}