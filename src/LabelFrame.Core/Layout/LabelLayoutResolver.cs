namespace LabelFrame.Core.Layout;

/// <summary>元素解析后的边界（毫米）。</summary>
public readonly record struct ElementBounds(double XMm, double YMm, double WidthMm, double HeightMm);

/// <summary>
/// 版式解析：索引区域（格子），并按区域 + 对齐计算元素实际位置与尺寸。
/// ZPL 编码与预览渲染共用，保证一致。
/// </summary>
public static class LabelLayoutResolver
{
    /// <summary>索引区域元素（按 Id）。</summary>
    public static IReadOnlyDictionary<string, LabelRegionElement> IndexRegions(LabelLayout layout)
        => layout.Elements.OfType<LabelRegionElement>().ToDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>解析元素边界：未锚定区域时返回绝对坐标 + 自然尺寸。</summary>
    public static ElementBounds ResolveBounds(LabelElement element, IReadOnlyDictionary<string, LabelRegionElement> regions)
    {
        double width;
        double height;
        switch (element)
        {
            case LabelTextElement text:
                width = text.WidthMm;
                height = text.FontHeightMm;
                break;
            case LabelBarcodeElement barcode:
                width = barcode.HeightMm * 2.5;
                height = barcode.HeightMm;
                break;
            case LabelQrCodeElement qrCode:
                width = qrCode.SizeMm;
                height = qrCode.SizeMm;
                break;
            case LabelImageElement image:
                width = image.WidthMm;
                height = image.HeightMm;
                break;
            case LabelRegionElement regionElement:
                width = regionElement.WidthMm;
                height = regionElement.HeightMm;
                break;
            default:
                return new ElementBounds(element.XMm, element.YMm, 0, 0);
        }

        if (string.IsNullOrEmpty(element.RegionId) || !regions.TryGetValue(element.RegionId, out var region))
        {
            return new ElementBounds(element.XMm, element.YMm, width, height);
        }

        // 文本在区域内未显式指定宽度时，用区域内宽（减内边距）作为块宽
        if (element is LabelTextElement && width <= 0)
        {
            width = region.WidthMm - element.PaddingMm * 2;
        }

        var factorX = AlignFactor(element.RegionHAlign ?? LabelRegionAlign.Center);
        var factorY = AlignFactor(element.RegionVAlign ?? LabelRegionAlign.Center);
        var x = region.XMm + (region.WidthMm - width) * factorX;
        var y = region.YMm + (region.HeightMm - height) * factorY;
        return new ElementBounds(x, y, width, height);
    }

    private static double AlignFactor(LabelRegionAlign align) => align switch
    {
        LabelRegionAlign.Start => 0,
        LabelRegionAlign.Center => 0.5,
        LabelRegionAlign.End => 1,
        _ => 0.5,
    };
}