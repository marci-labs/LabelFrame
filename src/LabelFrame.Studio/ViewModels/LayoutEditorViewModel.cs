using System.Collections.ObjectModel;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Studio.Services;

namespace LabelFrame.Studio.ViewModels;

/// <summary>版式编辑器视图模型（V2/V2B）：元素集合、缩放换算、字段编辑、区域布局、保存。</summary>
public sealed class LayoutEditorViewModel : ObservableObject
{
    private readonly StudioClient? _client;

    public LayoutEditorViewModel(StudioClient? client = null)
    {
        _client = client;
    }

    private string _name = string.Empty;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _version = "1.0";
    public string Version { get => _version; set => SetProperty(ref _version, value); }

    private string _group = "默认";
    public string Group { get => _group; set => SetProperty(ref _group, value); }

    private string _layoutName = string.Empty;
    public string LayoutName { get => _layoutName; set => SetProperty(ref _layoutName, value); }

    private double _widthMm = 100;
    public double WidthMm
    {
        get => _widthMm;
        set
        {
            if (SetProperty(ref _widthMm, value))
            {
                OnPropertyChanged(nameof(CanvasWidthPx));
            }
        }
    }

    private double _heightMm = 60;
    public double HeightMm
    {
        get => _heightMm;
        set
        {
            if (SetProperty(ref _heightMm, value))
            {
                OnPropertyChanged(nameof(CanvasHeightPx));
            }
        }
    }

    private double _zoom = 1.0;
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, value))
            {
                OnPropertyChanged(nameof(PixelsPerMm));
                OnPropertyChanged(nameof(CanvasWidthPx));
                OnPropertyChanged(nameof(CanvasHeightPx));
            }
        }
    }

    /// <summary>每毫米像素数（100% = 4px/mm）。</summary>
    public double PixelsPerMm => Zoom * 4;

    public double CanvasWidthPx => WidthMm * PixelsPerMm;

    public double CanvasHeightPx => HeightMm * PixelsPerMm;

    public ObservableCollection<LayoutElementViewModel> Elements { get; } = [];

    public ObservableCollection<ContractFieldViewModel> Fields { get; } = [];

    private LayoutElementViewModel? _selectedElement;
    public LayoutElementViewModel? SelectedElement
    {
        get => _selectedElement;
        set => SetProperty(ref _selectedElement, value);
    }

    private ContractFieldViewModel? _selectedField;
    public ContractFieldViewModel? SelectedField
    {
        get => _selectedField;
        set => SetProperty(ref _selectedField, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string[] ElementTypes { get; } = Enum.GetNames<EditorElementType>();

    /// <summary>当前区域 Id 列表（属性面板 RegionId 下拉）。</summary>
    public ObservableCollection<string> RegionIds { get; } = [];

    public void LoadFrom(TemplateSaveDto template)
    {
        if (template.Contract is null || template.Layout is null)
        {
            throw new InvalidOperationException("模板详情不完整。");
        }

        Name = template.Name ?? string.Empty;
        Group = template.Group ?? "默认";
        Version = template.Contract.Version;
        LayoutName = template.Layout.Name;
        WidthMm = template.Layout.WidthMm;
        HeightMm = template.Layout.HeightMm;

        Fields.Clear();
        foreach (var field in template.Contract.Fields)
        {
            Fields.Add(new ContractFieldViewModel(field.Key, field.DisplayName, field.IsRequired, field.Type));
        }

        Elements.Clear();
        foreach (var element in template.Layout.Elements)
        {
            Elements.Add(LayoutElementViewModel.From(element));
        }

        RefreshRegionIds();
        SelectedElement = Elements.FirstOrDefault();
    }

    /// <summary>添加元素：默认排在上一个元素下方（上下结构为主）。</summary>
    public void AddElement(EditorElementType type)
    {
        var element = new LayoutElementViewModel(type);
        if (Fields.Count > 0)
        {
            element.SourceKey = Fields[0].Key;
        }

        PlaceBelow(element);
        if (type == EditorElementType.Region)
        {
            element.Id = NextRegionId();
        }

        Elements.Add(element);
        RefreshRegionIds();
        SelectedElement = element;
        StatusText = $"已添加 {type} 元素。";
    }

    /// <summary>把元素放到上一个元素下方（间距 3mm），超界则回到画布顶部。</summary>
    private void PlaceBelow(LayoutElementViewModel element)
    {
        var last = Elements.LastOrDefault();
        if (last is null)
        {
            return;
        }

        var lastHeight = last.Type switch
        {
            EditorElementType.Text => last.FontHeightMm,
            EditorElementType.Line => last.Y2Mm - last.YMm,
            _ => last.HeightMm,
        };
        var nextY = last.YMm + lastHeight + 3;
        if (nextY + element.HeightMm > HeightMm && HeightMm > 0)
        {
            nextY = 5;
        }

        element.YMm = nextY;
    }

    public void RemoveElement(LayoutElementViewModel element)
    {
        Elements.Remove(element);
        if (SelectedElement == element)
        {
            SelectedElement = Elements.FirstOrDefault();
        }

        RefreshRegionIds();
    }

    public void AddField()
    {
        var index = Fields.Count + 1;
        Fields.Add(new ContractFieldViewModel($"field{index}", $"字段{index}", isRequired: false, LabelFieldType.Text));
        StatusText = "已添加字段，可在下方编辑键与显示名。";
    }

    public void RemoveField(ContractFieldViewModel field)
    {
        Fields.Remove(field);
    }

    /// <summary>重命名字段：同步更新引用该字段的元素 SourceKey。</summary>
    public void RenameField(string oldKey, string newKey)
    {
        if (string.IsNullOrWhiteSpace(newKey) || oldKey == newKey)
        {
            return;
        }

        foreach (var element in Elements)
        {
            if (element.SourceKey == oldKey)
            {
                element.SourceKey = newKey;
            }
        }

        StatusText = $"字段 {oldKey} 已重命名为 {newKey}，元素引用已同步。";
    }

    /// <summary>按像素位移移动元素（拖拽用）。</summary>
    public void MoveElement(LayoutElementViewModel element, double dxPx, double dyPx)
    {
        element.XMm += dxPx / PixelsPerMm;
        element.YMm += dyPx / PixelsPerMm;
    }

    public LabelContract BuildContract() => new()
    {
        Name = Name,
        Version = Version,
        Fields = Fields.Select(f => new LabelField
        {
            Key = f.Key,
            DisplayName = f.DisplayName,
            IsRequired = f.IsRequired,
            Type = f.Type,
        }).ToList(),
    };

    public LabelLayout BuildLayout() => new()
    {
        Name = string.IsNullOrWhiteSpace(LayoutName) ? $"{Name}-layout" : LayoutName,
        ContractName = Name,
        ContractVersion = Version,
        WidthMm = WidthMm,
        HeightMm = HeightMm,
        Elements = Elements.Select(e => e.ToElement()).ToList(),
    };

    public TemplateSaveDto BuildSaveDto() => new(Name, Group, BuildContract(), BuildLayout());

    public async Task SaveAsync()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("未连接 WinHost。");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("模板名不能为空。");
        }

        await _client.SaveTemplateAsync(BuildSaveDto());
        StatusText = $"已保存：{Name} v{Version}";
    }

    private void RefreshRegionIds()
    {
        var ids = Elements.Where(e => e.Type == EditorElementType.Region && !string.IsNullOrWhiteSpace(e.Id))
            .Select(e => e.Id!)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        RegionIds.Clear();
        foreach (var id in ids)
        {
            RegionIds.Add(id);
        }
    }

    private static string NextRegionId()
    {
        var random = Guid.NewGuid().ToString("N")[..8];
        return $"region-{random}";
    }
}