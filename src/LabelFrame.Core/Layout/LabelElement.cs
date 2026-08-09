namespace LabelFrame.Core.Layout;

/// <summary>
/// 版式元素的抽象基类，所有元素使用毫米坐标。
/// JSON 序列化统一由 <see cref="LabelElementJsonConverter"/> 按 "type" 判别子类型处理。
/// </summary>
public abstract class LabelElement
{
    /// <summary>左上角 X 坐标（毫米）。</summary>
    public double XMm { get; init; }

    /// <summary>左上角 Y 坐标（毫米）。</summary>
    public double YMm { get; init; }

    /// <summary>元素内边距（毫米，当前用于文本内容盒）。</summary>
    public double PaddingMm { get; init; }

    /// <summary>元素边框线宽（毫米，0 = 无边框）。</summary>
    public double BorderMm { get; init; }

    /// <summary>锚定的区域（格子）标识；为空表示绝对定位。</summary>
    public string? RegionId { get; init; }

    /// <summary>区域内水平对齐（RegionId 非空且未指定时默认居中）。</summary>
    public LabelRegionAlign? RegionHAlign { get; init; }

    /// <summary>区域内垂直对齐（RegionId 非空且未指定时默认居中）。</summary>
    public LabelRegionAlign? RegionVAlign { get; init; }

    /// <summary>元素类型。</summary>
    public abstract LabelElementType Type { get; }
}