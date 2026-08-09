namespace LabelFrame.Core.Layout;

/// <summary>文本元素，绑定契约字段键。</summary>
public sealed class LabelTextElement : LabelElement
{
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
}