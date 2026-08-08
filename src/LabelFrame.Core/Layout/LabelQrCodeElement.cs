namespace LabelFrame.Core.Layout;

/// <summary>二维码元素，绑定契约字段键。</summary>
public sealed class LabelQrCodeElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.QrCode;

    /// <summary>绑定的契约字段键。</summary>
    public required string SourceKey { get; init; }

    /// <summary>二维码边长（毫米）。</summary>
    public double SizeMm { get; init; }
}