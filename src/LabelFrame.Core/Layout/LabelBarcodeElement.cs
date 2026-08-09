namespace LabelFrame.Core.Layout;

/// <summary>条码元素（迭代 1 支持 Code128），绑定契约字段键。</summary>
public sealed class LabelBarcodeElement : LabelElement
{
    /// <inheritdoc />
    public override LabelElementType Type => LabelElementType.Barcode;

    /// <summary>绑定的契约字段键（Literal 非空时忽略）。</summary>
    public string SourceKey { get; init; } = string.Empty;

    /// <summary>固定值（不填充字段时使用；为空则按 SourceKey 字段填充）。</summary>
    public string? Literal { get; init; }

    /// <summary>条码高度（毫米）。</summary>
    public double HeightMm { get; init; }

    /// <summary>窄条宽度（ZPL 模块宽度 1-10，默认 2）。</summary>
    public int ModuleWidth { get; init; } = 2;
}