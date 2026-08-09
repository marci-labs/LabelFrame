namespace LabelFrame.Core.Layout;

/// <summary>版式元素类型。</summary>
public enum LabelElementType
{
    /// <summary>文本。</summary>
    Text,

    /// <summary>条码（迭代 1 支持 Code128）。</summary>
    Barcode,

    /// <summary>二维码。</summary>
    QrCode,

    /// <summary>图片。</summary>
    Image,

    /// <summary>线。</summary>
    Line,

    /// <summary>区域（格子）容器。</summary>
    Region,
}