using LabelFrame.Core.Layout;

namespace LabelFrame.Studio.ViewModels;

/// <summary>画布元素类型。</summary>
public enum EditorElementType
{
    Text,
    Barcode,
    QrCode,
    Image,
    Line,
    Region,
}

/// <summary>可编辑的版式元素（迭代 8D：容器化显示、多选、实时通知）。</summary>
public sealed class LayoutElementViewModel : ObservableObject
{
    public LayoutElementViewModel(EditorElementType type)
    {
        Type = type;
        switch (type)
        {
            case EditorElementType.Text:
                XMm = 5; YMm = 5; FontHeightMm = 5; FontWidthMm = 5;
                break;
            case EditorElementType.Barcode:
                XMm = 5; YMm = 20; HeightMm = 20;
                break;
            case EditorElementType.QrCode:
                XMm = 5; YMm = 20; WidthMm = 20; HeightMm = 20;
                break;
            case EditorElementType.Image:
                XMm = 5; YMm = 20; WidthMm = 20; HeightMm = 20;
                break;
            case EditorElementType.Line:
                XMm = 5; YMm = 5; X2Mm = 60; Y2Mm = 5; ThicknessMm = 0.5;
                break;
            case EditorElementType.Region:
                XMm = 5; YMm = 5; WidthMm = 60; HeightMm = 30; Id = "container1"; BorderMm = 0.3;
                break;
        }
    }

    public EditorElementType Type { get; }

    /// <summary>元素中文显示名。</summary>
    public string DisplayName => Type switch
    {
        EditorElementType.Text => "文本",
        EditorElementType.Barcode => "条码",
        EditorElementType.QrCode => "二维码",
        EditorElementType.Image => "图片",
        EditorElementType.Line => "线",
        EditorElementType.Region => "容器",
        _ => Type.ToString(),
    };

    public string DisplayLabel => Type == EditorElementType.Region ? $"容器 ({Id})" : $"{DisplayName} ({SourceKey})";

    public bool IsText => Type == EditorElementType.Text;
    public bool IsRect => Type is EditorElementType.Barcode or EditorElementType.QrCode or EditorElementType.Image;
    public bool IsLine => Type == EditorElementType.Line;
    public bool IsContainer => Type == EditorElementType.Region;

    private double _xMm;
    public double XMm { get => _xMm; set { if (SetProperty(ref _xMm, value)) OnPropertyChanged(nameof(DisplayLabel)); } }

    private double _yMm;
    public double YMm { get => _yMm; set { if (SetProperty(ref _yMm, value)) OnPropertyChanged(nameof(DisplayLabel)); } }

    private double _widthMm;
    public double WidthMm { get => _widthMm; set => SetProperty(ref _widthMm, value); }

    private double _heightMm;
    public double HeightMm { get => _heightMm; set => SetProperty(ref _heightMm, value); }

    private double _x2Mm;
    public double X2Mm { get => _x2Mm; set => SetProperty(ref _x2Mm, value); }

    private double _y2Mm;
    public double Y2Mm { get => _y2Mm; set => SetProperty(ref _y2Mm, value); }

    private double _thicknessMm = 0.5;
    public double ThicknessMm { get => _thicknessMm; set => SetProperty(ref _thicknessMm, value); }

    private string _sourceKey = string.Empty;
    public string SourceKey { get => _sourceKey; set { if (SetProperty(ref _sourceKey, value)) OnPropertyChanged(nameof(DisplayLabel)); } }

    private string _fontName = "0";
    public string FontName { get => _fontName; set => SetProperty(ref _fontName, value); }

    private double _fontHeightMm = 5;
    public double FontHeightMm { get => _fontHeightMm; set => SetProperty(ref _fontHeightMm, value); }

    private double _fontWidthMm = 5;
    public double FontWidthMm { get => _fontWidthMm; set => SetProperty(ref _fontWidthMm, value); }

    private string _id = string.Empty;
    public string Id { get => _id; set { if (SetProperty(ref _id, value)) OnPropertyChanged(nameof(DisplayLabel)); } }

    private double _paddingMm;
    public double PaddingMm { get => _paddingMm; set => SetProperty(ref _paddingMm, value); }

    private double _borderMm;
    public double BorderMm { get => _borderMm; set => SetProperty(ref _borderMm, value); }

    private string? _regionId;
    public string? RegionId { get => _regionId; set => SetProperty(ref _regionId, value); }

    private string _regionHAlign = "Center";
    public string RegionHAlign { get => _regionHAlign; set => SetProperty(ref _regionHAlign, value); }

    private string _regionVAlign = "Center";
    public string RegionVAlign { get => _regionVAlign; set => SetProperty(ref _regionVAlign, value); }

    private string _textAlign = "Left";
    public string TextAlign { get => _textAlign; set => SetProperty(ref _textAlign, value); }

    public string[] TextAlignOptions { get; } = Enum.GetNames<LabelTextAlign>();

    private string _contentMode = "字段填充";
    public string ContentMode
    {
        get => _contentMode;
        set
        {
            if (SetProperty(ref _contentMode, value))
            {
                OnPropertyChanged(nameof(IsLiteral));
                OnPropertyChanged(nameof(IsField));
            }
        }
    }

    public bool IsLiteral => ContentMode == "固定值";

    public bool IsField => ContentMode != "固定值";

    public string[] ContentModeOptions { get; } = ["字段填充", "固定值"];

    private string _literal = string.Empty;
    public string Literal { get => _literal; set => SetProperty(ref _literal, value); }

    public string[] RegionAlignOptions { get; } = Enum.GetNames<LabelRegionAlign>();

    /// <summary>转换成 Core 版式元素。</summary>
    public LabelElement ToElement() => Type switch
    {
        EditorElementType.Text => new LabelTextElement
        {
            SourceKey = ContentMode == "固定值" ? string.Empty : SourceKey,
            Literal = ContentMode == "固定值" ? Literal : null,
            FontName = FontName,
            FontHeightMm = FontHeightMm,
            FontWidthMm = FontWidthMm,
            WidthMm = WidthMm,
            TextAlign = Enum.Parse<LabelTextAlign>(TextAlign),
            PaddingMm = PaddingMm,
            BorderMm = BorderMm,
            RegionId = RegionId,
            RegionHAlign = ParseRegionAlign(RegionHAlign),
            RegionVAlign = ParseRegionAlign(RegionVAlign),
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.Barcode => new LabelBarcodeElement
        {
            SourceKey = ContentMode == "固定值" ? string.Empty : SourceKey,
            Literal = ContentMode == "固定值" ? Literal : null,
            HeightMm = HeightMm,
            ModuleWidth = 2,
            BorderMm = BorderMm,
            RegionId = RegionId,
            RegionHAlign = ParseRegionAlign(RegionHAlign),
            RegionVAlign = ParseRegionAlign(RegionVAlign),
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.QrCode => new LabelQrCodeElement
        {
            SourceKey = ContentMode == "固定值" ? string.Empty : SourceKey,
            Literal = ContentMode == "固定值" ? Literal : null,
            SizeMm = WidthMm,
            BorderMm = BorderMm,
            RegionId = RegionId,
            RegionHAlign = ParseRegionAlign(RegionHAlign),
            RegionVAlign = ParseRegionAlign(RegionVAlign),
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.Image => new LabelImageElement
        {
            SourceKey = SourceKey,
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            BorderMm = BorderMm,
            RegionId = RegionId,
            RegionHAlign = ParseRegionAlign(RegionHAlign),
            RegionVAlign = ParseRegionAlign(RegionVAlign),
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.Line => new LabelLineElement
        {
            XMm = XMm,
            YMm = YMm,
            X2Mm = X2Mm,
            Y2Mm = Y2Mm,
            ThicknessMm = ThicknessMm,
        },
        EditorElementType.Region => new LabelRegionElement
        {
            Id = string.IsNullOrWhiteSpace(Id) ? $"region-{Guid.NewGuid():N}"[..14] : Id,
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            BorderMm = BorderMm,
            XMm = XMm,
            YMm = YMm,
        },
        _ => throw new InvalidOperationException($"未知元素类型：{Type}。"),
    };

    /// <summary>从 Core 版式元素加载。</summary>
    public static LayoutElementViewModel From(LabelElement element) => element switch
    {
        LabelTextElement text => new LayoutElementViewModel(EditorElementType.Text)
        {
            SourceKey = text.SourceKey,
            ContentMode = text.Literal is not null ? "固定值" : "字段填充",
            Literal = text.Literal ?? string.Empty,
            FontName = text.FontName,
            FontHeightMm = text.FontHeightMm,
            FontWidthMm = text.FontWidthMm,
            WidthMm = text.WidthMm,
            TextAlign = text.TextAlign.ToString(),
            PaddingMm = text.PaddingMm,
            BorderMm = text.BorderMm,
            RegionId = text.RegionId,
            RegionHAlign = (text.RegionHAlign ?? LabelRegionAlign.Center).ToString(),
            RegionVAlign = (text.RegionVAlign ?? LabelRegionAlign.Center).ToString(),
            XMm = text.XMm,
            YMm = text.YMm,
        },
        LabelBarcodeElement barcode => new LayoutElementViewModel(EditorElementType.Barcode)
        {
            SourceKey = barcode.SourceKey,
            ContentMode = barcode.Literal is not null ? "固定值" : "字段填充",
            Literal = barcode.Literal ?? string.Empty,
            HeightMm = barcode.HeightMm,
            BorderMm = barcode.BorderMm,
            RegionId = barcode.RegionId,
            RegionHAlign = (barcode.RegionHAlign ?? LabelRegionAlign.Center).ToString(),
            RegionVAlign = (barcode.RegionVAlign ?? LabelRegionAlign.Center).ToString(),
            XMm = barcode.XMm,
            YMm = barcode.YMm,
        },
        LabelQrCodeElement qr => new LayoutElementViewModel(EditorElementType.QrCode)
        {
            SourceKey = qr.SourceKey,
            ContentMode = qr.Literal is not null ? "固定值" : "字段填充",
            Literal = qr.Literal ?? string.Empty,
            WidthMm = qr.SizeMm,
            HeightMm = qr.SizeMm,
            BorderMm = qr.BorderMm,
            RegionId = qr.RegionId,
            RegionHAlign = (qr.RegionHAlign ?? LabelRegionAlign.Center).ToString(),
            RegionVAlign = (qr.RegionVAlign ?? LabelRegionAlign.Center).ToString(),
            XMm = qr.XMm,
            YMm = qr.YMm,
        },
        LabelImageElement image => new LayoutElementViewModel(EditorElementType.Image)
        {
            SourceKey = image.SourceKey,
            WidthMm = image.WidthMm,
            HeightMm = image.HeightMm,
            BorderMm = image.BorderMm,
            RegionId = image.RegionId,
            RegionHAlign = (image.RegionHAlign ?? LabelRegionAlign.Center).ToString(),
            RegionVAlign = (image.RegionVAlign ?? LabelRegionAlign.Center).ToString(),
            XMm = image.XMm,
            YMm = image.YMm,
        },
        LabelLineElement line => new LayoutElementViewModel(EditorElementType.Line)
        {
            XMm = line.XMm,
            YMm = line.YMm,
            X2Mm = line.X2Mm,
            Y2Mm = line.Y2Mm,
            ThicknessMm = line.ThicknessMm,
        },
        LabelRegionElement region => new LayoutElementViewModel(EditorElementType.Region)
        {
            Id = region.Id,
            WidthMm = region.WidthMm,
            HeightMm = region.HeightMm,
            BorderMm = region.BorderMm,
            XMm = region.XMm,
            YMm = region.YMm,
        },
        _ => throw new InvalidOperationException($"未知元素类型：{element.GetType().Name}。"),
    };

    private static LabelRegionAlign? ParseRegionAlign(string value)
        => Enum.TryParse<LabelRegionAlign>(value, out var align) ? align : null;
}

/// <summary>可编辑的契约字段（迭代 8D：由版式自动推导，元数据后台保留）。</summary>
public sealed class ContractFieldViewModel : ObservableObject
{
    public ContractFieldViewModel(string key, string displayName, bool isRequired, LabelFrame.Core.Contracts.LabelFieldType type)
    {
        Key = key;
        _displayName = displayName;
        IsRequired = isRequired;
        Type = type;
    }

    public string Key { get; set; }

    private string _displayName;
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }

    private bool _isRequired;
    public bool IsRequired { get => _isRequired; set => SetProperty(ref _isRequired, value); }

    public LabelFrame.Core.Contracts.LabelFieldType Type { get; set; }
}

/// <summary>数据表单条目（按契约字段生成）。</summary>
public sealed class FieldEntry : ObservableObject
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public bool IsRequired { get; init; }

    public required string Type { get; init; }

    private string _value = string.Empty;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}