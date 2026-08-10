namespace LabelFrame.Core.Layout;

/// <summary>文本元素，绑定契约字段键。</summary>
public sealed class LabelTextElement : LabelElement
{
    /// <summary>默认画布字体族（图片打印 / 预览用；矢量 ZPL 仍由 <see cref="FontName"/> 决定）。</summary>
    public const string DefaultFontFamily = "Microsoft YaHei";

    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.Text;

    /// <summary>绑定的契约字段键（Literal 非空时忽略）。</summary>
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>固定值（不填充字段时使用；为空则按 SourceKey 字段填充）。</summary>
    public string? Literal { get; init; }

    /// <summary>ZPL 内置字体名（默认 0）。</summary>
    public string FontName { get; init; } = "0";

    /// <summary>字高（毫米）。</summary>
    public double FontHeightMm { get; init; }

    /// <summary>字宽（毫米），0 表示按比例自动。</summary>
    public double FontWidthMm { get; init; }

    /// <summary>文本块宽度（毫米），0 = 不限制（无块对齐）；用于居中对齐与边框。</summary>
    public double WidthMm { get; init; }

    /// <summary>文本块内对齐方式（默认左对齐）。</summary>
    public LabelTextAlign TextAlign { get; init; } = LabelTextAlign.Left;

    /// <summary>文本块高度（毫米）；0 = 未指定（按兜底：max(字高 + 2×最大内边距, 10)）。前端编辑器会保存元素高度，用于垂直对齐。</summary>
    public double HeightMm { get; init; }

    /// <summary>垂直对齐（默认 Middle，与前端一致；Top/Bottom 显式写入，Middle 省略）。</summary>
    public LabelVerticalAlign VerticalAlign { get; init; } = LabelVerticalAlign.Middle;

    /// <summary>画布字体族（默认 Microsoft YaHei；仅图片打印 / 预览用，矢量 ZPL 用 <see cref="FontName"/>）。</summary>
    public string FontFamily { get; init; } = DefaultFontFamily;

    /// <summary>自动换行（默认 false；true 时按框宽换行，超高整体缩小，避免打印丢字）。</summary>
    public bool Wrap { get; init; }

    /// <summary>行距倍数（默认 1.2，相对字高）。</summary>
    public double LineHeight { get; init; } = 1.2;

    /// <summary>溢出处理方式（默认 Shrink 缩小适应；Overflow = 隐藏 / 裁剪，不缩小）。</summary>
    public LabelFitMode FitMode { get; init; } = LabelFitMode.Shrink;

    /// <summary>字体加粗（默认 false；ZPL 用粗体字体变体 / 宽度放大，Skia 用 fontStyle bold）。</summary>
    public bool Bold { get; init; }
}

/// <summary>文本垂直对齐。</summary>
public enum LabelVerticalAlign
{
    /// <summary>顶部对齐。</summary>
    Top,

    /// <summary>垂直居中（默认）。</summary>
    Middle,

    /// <summary>底部对齐。</summary>
    Bottom,
}

/// <summary>文本溢出处理方式。</summary>
public enum LabelFitMode
{
    /// <summary>缩小适应（默认，最小 1.5mm）。</summary>
    Shrink,

    /// <summary>隐藏 / 裁剪溢出内容（不缩小）。</summary>
    Overflow,
}
