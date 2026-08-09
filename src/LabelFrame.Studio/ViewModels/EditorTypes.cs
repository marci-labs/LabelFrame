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
}

/// <summary>可编辑的版式元素。</summary>
public sealed class LayoutElementViewModel : ObservableObject
{
    public LayoutElementViewModel(EditorElementType type)
    {
        Type = type;
        SourceKey = "text";
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
        }
    }

    public EditorElementType Type { get; }

    public string DisplayName => $"{Type}";
    public string DisplayLabel => $"{Type} ({SourceKey})";

    public bool IsText => Type == EditorElementType.Text;
    public bool IsRect => Type is EditorElementType.Barcode or EditorElementType.QrCode or EditorElementType.Image;
    public bool IsLine => Type == EditorElementType.Line;

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

    /// <summary>转换成 Core 版式元素。</summary>
    public LabelElement ToElement() => Type switch
    {
        EditorElementType.Text => new LabelTextElement
        {
            SourceKey = SourceKey,
            FontName = FontName,
            FontHeightMm = FontHeightMm,
            FontWidthMm = FontWidthMm,
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.Barcode => new LabelBarcodeElement
        {
            SourceKey = SourceKey,
            HeightMm = HeightMm,
            ModuleWidth = 2,
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.QrCode => new LabelQrCodeElement
        {
            SourceKey = SourceKey,
            SizeMm = Math.Max(WidthMm, HeightMm),
            XMm = XMm,
            YMm = YMm,
        },
        EditorElementType.Image => new LabelImageElement
        {
            SourceKey = SourceKey,
            WidthMm = WidthMm,
            HeightMm = HeightMm,
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
        _ => throw new InvalidOperationException($"未知元素类型：{Type}。"),
    };

    /// <summary>从 Core 版式元素加载。</summary>
    public static LayoutElementViewModel From(LabelElement element) => element switch
    {
        LabelTextElement text => new LayoutElementViewModel(EditorElementType.Text)
        {
            SourceKey = text.SourceKey,
            FontName = text.FontName,
            FontHeightMm = text.FontHeightMm,
            FontWidthMm = text.FontWidthMm,
            XMm = text.XMm,
            YMm = text.YMm,
        },
        LabelBarcodeElement barcode => new LayoutElementViewModel(EditorElementType.Barcode)
        {
            SourceKey = barcode.SourceKey,
            HeightMm = barcode.HeightMm,
            XMm = barcode.XMm,
            YMm = barcode.YMm,
        },
        LabelQrCodeElement qr => new LayoutElementViewModel(EditorElementType.QrCode)
        {
            SourceKey = qr.SourceKey,
            WidthMm = qr.SizeMm,
            HeightMm = qr.SizeMm,
            XMm = qr.XMm,
            YMm = qr.YMm,
        },
        LabelImageElement image => new LayoutElementViewModel(EditorElementType.Image)
        {
            SourceKey = image.SourceKey,
            WidthMm = image.WidthMm,
            HeightMm = image.HeightMm,
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
        _ => throw new InvalidOperationException($"未知元素类型：{element.GetType().Name}。"),
    };
}

/// <summary>可编辑的契约字段。</summary>
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

    public string[] AvailableTypes { get; } = Enum.GetNames<LabelFrame.Core.Contracts.LabelFieldType>();
}