using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Tests.Samples;

/// <summary>库位码场景样例：契约 + 版式 + 数据，供 golden test 与校验用例复用。</summary>
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

    /// <summary>库位码版式：100mm x 60mm，区域文本 + 库位码文本 + Code128 + 图片占位。</summary>
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
            new LabelImageElement { SourceKey = "logo", XMm = 80, YMm = 4, WidthMm = 10, HeightMm = 10 },
        ],
    };

    /// <summary>构造标签文档。</summary>
    public static LabelDocument CreateDocument(
        string zone = "A-01",
        string locationCode = "A-01-02-03",
        string? remark = null) => new()
    {
        Layout = Layout,
        Data = new Dictionary<string, string>
        {
            ["zone"] = zone,
            ["locationCode"] = locationCode,
            ["remark"] = remark ?? string.Empty,
        },
    };
}