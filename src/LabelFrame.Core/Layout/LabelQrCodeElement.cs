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

    /// <summary>纠错级别（默认 M，与前端一致）。</summary>
    public LabelQrEcc QrEcc { get; init; } = LabelQrEcc.M;

    /// <summary>静区模块数（默认 2）。</summary>
    public int QrMargin { get; init; } = 2;
}

/// <summary>二维码纠错级别（L / M / Q / H，容错递增）。</summary>
public enum LabelQrEcc
{
    /// <summary>约 7% 容错。</summary>
    L,

    /// <summary>约 15% 容错（默认）。</summary>
    M,

    /// <summary>约 25% 容错。</summary>
    Q,

    /// <summary>约 30% 容错。</summary>
    H,
}
