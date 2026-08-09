namespace LabelFrame.Core.Layout;

/// <summary>二维码元素，绑定契约字段键。</summary>
public sealed class LabelQrCodeElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.QrCode;

    /// <summary>绑定的契约字段键（Literal 非空时忽略）。</summary>
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>固定值（不填充字段时使用；为空则按 SourceKey 字段填充）。</summary>
    public string? Literal { get; init; }

    /// <summary>二维码边长（毫米）。</summary>
    public double SizeMm { get; init; }
}