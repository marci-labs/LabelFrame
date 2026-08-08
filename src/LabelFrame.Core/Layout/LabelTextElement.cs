namespace LabelFrame.Core.Layout;

/// <summary>文本元素，绑定契约字段键。</summary>
public sealed class LabelTextElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.Text;

    /// <summary>绑定的契约字段键。</summary>
    public required string SourceKey { get; init; }

    /// <summary>ZPL 内置字体名（默认 0）。</summary>
    public string FontName { get; init; } = "0";

    /// <summary>字高（毫米）。</summary>
    public double FontHeightMm { get; init; }

    /// <summary>字宽（毫米），0 表示按比例自动。</summary>
    public double FontWidthMm { get; init; }
}