using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.WinHost.Tests.Samples;

/// <summary>库位码场景样例（与 Core.Tests 同构），供提交服务测试复用。</summary>
public static class LocationLabelSamples
{
    /// <summary>库位码契约 v1.0。</summary>
    public static LabelContract Contract { get; } = new()
    {
        Name = "location-label",
        Version = "1.0",
        Fields =
        [
            new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            new LabelField { Key = "zone", DisplayName = "区域", IsRequired = true },
            new LabelField { Key = "remark", DisplayName = "备注" },
        ],
    };

    /// <summary>库位码版式：100mm x 60mm。</summary>
    public static LabelLayout Layout { get; } = new()
    {
        Name = "location-label-100x60",
        ContractName = "location-label",
        ContractVersion = "1.0",
        WidthMm = 100,
        HeightMm = 60,
        Elements =
        [
            new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
            new LabelTextElement { SourceKey = "locationCode", XMm = 5, YMm = 14, FontHeightMm = 8, FontWidthMm = 8 },
            new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
        ],
    };
}