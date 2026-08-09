using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Studio.Services;

namespace LabelFrame.Studio.ViewModels;

/// <summary>多选对齐方式。</summary>
public enum EditorAlign
{
    Left,
    CenterH,
    Right,
    Top,
    CenterV,
    Bottom,
}

/// <summary>
/// 版式编辑器视图模型（迭代 8D）：元素集合、多选、字段自动推导、对齐 / 吸附、容器布局、保存。
/// </summary>
public sealed class LayoutEditorViewModel : ObservableObject
{
    private readonly StudioClient? _client;

    public LayoutEditorViewModel(StudioClient? client = null)
    {
        _client = client;
        Elements.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (LayoutElementViewModel element in e.OldItems)
                {
                    element.PropertyChanged -= OnElementPropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (LayoutElementViewModel element in e.NewItems)
                {
                    element.PropertyChanged += OnElementPropertyChanged;
                }
            }
        };
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
            var clamped = Math.Clamp(value, 0.25, 4.0);
            if (SetProperty(ref _zoom, clamped))
            {
                OnPropertyChanged(nameof(PixelsPerMm));
                OnPropertyChanged(nameof(CanvasWidthPx));
                OnPropertyChanged(nameof(CanvasHeightPx));
                OnPropertyChanged(nameof(CanvasBackground));
            }
        }
    }

    private bool _showGrid = true;
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetProperty(ref _showGrid, value))
            {
                OnPropertyChanged(nameof(CanvasBackground));
            }
        }
    }

    /// <summary>画布背景（毫米网格）。</summary>
    public Brush CanvasBackground => _showGrid ? CreateGridBrush() : Brushes.White;

    private Brush CreateGridBrush()
    {
        var cell = Math.Max(4, 5 * PixelsPerMm);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), 0.5);
        drawing.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(cell, 0), new Point(cell, cell))));
        drawing.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, cell), new Point(cell, cell))));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell, cell),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>每毫米像素数（100% = 4px/mm）。</summary>
    public double PixelsPerMm => Zoom * 4;

    public double CanvasWidthPx => WidthMm * PixelsPerMm;

    public double CanvasHeightPx => HeightMm * PixelsPerMm;

    public ObservableCollection<LayoutElementViewModel> Elements { get; } = [];

    /// <summary>契约字段（由版式元素填充 key 自动推导）。</summary>
    public ObservableCollection<ContractFieldViewModel> Fields { get; } = [];

    /// <summary>设计器测试数据（按推导字段生成，用于打印预览 / 打印测试）。</summary>
    public ObservableCollection<FieldEntry> TestFields { get; } = [];

    /// <summary>日志（底部日志栏）。</summary>
    public ObservableCollection<string> Logs { get; } = [];

    private LayoutElementViewModel? _selectedElement;
    /// <summary>主选中元素（属性面板显示单个元素属性）。</summary>
    public LayoutElementViewModel? SelectedElement
    {
        get => _selectedElement;
        private set => SetProperty(ref _selectedElement, value);
    }

    /// <summary>当前选中集合（支持多选）。</summary>
    public ObservableCollection<LayoutElementViewModel> SelectedElements { get; } = [];

    public bool HasSelection => SelectedElements.Count > 0;

    public bool IsSingleSelection => SelectedElements.Count == 1;

    public bool ShowMultiSelectionPanel => HasSelection && !IsSingleSelection;

    public string SelectionCountText => SelectedElements.Count switch
    {
        0 => string.Empty,
        1 => "已选 1 个元素",
        _ => $"已选 {SelectedElements.Count} 个元素",
    };

    public IReadOnlyList<string> FieldKeys => Fields.Select(f => f.Key).ToList();

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

    /// <summary>当前容器 Id 列表（内部用于区域解析）。</summary>
    public ObservableCollection<string> RegionIds { get; } = [];

    /// <summary>追加日志。</summary>
    public void Log(string message)
    {
        Logs.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        StatusText = message;
    }

    /// <summary>清空日志。</summary>
    public void ClearLogs() => Logs.Clear();

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
        RefreshFields();
        SelectedElement = Elements.FirstOrDefault();
        SelectedElements.Clear();
        if (SelectedElement is not null)
        {
            SelectedElements.Add(SelectedElement);
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(ShowMultiSelectionPanel));
        OnPropertyChanged(nameof(SelectionCountText));
    }

    /// <summary>添加元素：默认排在上一个元素下方（上下结构为主），SourceKey 留空待用户绑定。</summary>
    public void AddElement(EditorElementType type)
    {
        var element = new LayoutElementViewModel(type);
        if (type == EditorElementType.Region)
        {
            element.Id = NextRegionId();
        }

        PlaceBelow(element);
        Elements.Add(element);
        RefreshRegionIds();
        SetSelection(element, additive: false);
        Log(type == EditorElementType.Region ? "已添加容器（元素拖入容器可自动居中）。" : $"已添加{element.DisplayName}（在属性面板绑定字段或填固定值）。");
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

    /// <summary>删除单个元素（保留接口，测试与程序化使用）。</summary>
    public void RemoveElement(LayoutElementViewModel element)
    {
        Elements.Remove(element);
        SelectedElements.Remove(element);
        if (SelectedElement == element)
        {
            SelectedElement = SelectedElements.LastOrDefault();
        }

        RefreshRegionIds();
        RefreshFields();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(ShowMultiSelectionPanel));
        OnPropertyChanged(nameof(SelectionCountText));
    }

    /// <summary>删除全部选中元素（Delete 键 / 右键菜单）。</summary>
    public void DeleteSelected()
    {
        var count = SelectedElements.Count;
        if (count == 0)
        {
            return;
        }

        foreach (var element in SelectedElements.ToList())
        {
            Elements.Remove(element);
        }

        SelectedElements.Clear();
        SelectedElement = null;
        RefreshRegionIds();
        RefreshFields();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(ShowMultiSelectionPanel));
        OnPropertyChanged(nameof(SelectionCountText));
        Log($"已删除 {count} 个元素。");
    }

    /// <summary>设置选中集合；additive 为 true 时切换（Ctrl / Shift 点击）。</summary>
    public void SetSelection(LayoutElementViewModel element, bool additive)
    {
        if (additive)
        {
            if (SelectedElements.Contains(element))
            {
                SelectedElements.Remove(element);
                if (ReferenceEquals(SelectedElement, element))
                {
                    SelectedElement = SelectedElements.LastOrDefault();
                }
            }
            else
            {
                SelectedElements.Add(element);
                SelectedElement = element;
            }
        }
        else
        {
            SelectedElements.Clear();
            SelectedElements.Add(element);
            SelectedElement = element;
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(ShowMultiSelectionPanel));
        OnPropertyChanged(nameof(SelectionCountText));
    }

    /// <summary>框选：替换当前选中为命中元素集合（空集合等于清空）。</summary>
    public void SelectRange(IReadOnlyList<LayoutElementViewModel> elements)
    {
        SelectedElements.Clear();
        foreach (var element in elements)
        {
            SelectedElements.Add(element);
        }

        SelectedElement = elements.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(ShowMultiSelectionPanel));
        OnPropertyChanged(nameof(SelectionCountText));
    }

    /// <summary>清空选中。</summary>
    public void ClearSelection()
    {
        SelectedElements.Clear();
        SelectedElement = null;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(ShowMultiSelectionPanel));
        OnPropertyChanged(nameof(SelectionCountText));
    }

    /// <summary>契约字段自动推导：字段 = 版式中「字段填充」元素 SourceKey 去重（保留旧字段顺序与元数据）。</summary>
    public void RefreshFields()
    {
        var referenced = Elements
            .Where(e => e.Type != EditorElementType.Region && e.ContentMode == "字段填充" && !string.IsNullOrWhiteSpace(e.SourceKey))
            .Select(e => e.SourceKey!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var ordered = Fields.Select(f => f.Key).Where(referenced.Contains).ToList();
        ordered.AddRange(referenced.Except(ordered));

        var existing = Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        Fields.Clear();
        foreach (var key in ordered)
        {
            if (existing.TryGetValue(key, out var old))
            {
                Fields.Add(old);
            }
            else
            {
                Fields.Add(new ContractFieldViewModel(key, key, isRequired: false, LabelFieldType.Text));
            }
        }

        SyncTestFields();
        OnPropertyChanged(nameof(FieldKeys));
    }

    private void SyncTestFields()
    {
        var oldValues = TestFields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        TestFields.Clear();
        foreach (var field in Fields)
        {
            TestFields.Add(new FieldEntry
            {
                Key = field.Key,
                DisplayName = field.DisplayName,
                IsRequired = field.IsRequired,
                Type = field.Type.ToString(),
                Value = oldValues.TryGetValue(field.Key, out var value) ? value : string.Empty,
            });
        }
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayoutElementViewModel.SourceKey))
        {
            RefreshFields();
        }
    }

    /// <summary>在指定毫米位置创建容器（拖矩形入口保留，UI 由控件栏容器代替）。</summary>
    public LayoutElementViewModel AddRegionAt(double xMm, double yMm, double widthMm, double heightMm)
    {
        var element = new LayoutElementViewModel(EditorElementType.Region)
        {
            XMm = xMm,
            YMm = yMm,
            WidthMm = widthMm,
            HeightMm = heightMm,
            Id = $"region-{Guid.NewGuid():N}"[..14],
        };
        Elements.Add(element);
        RefreshRegionIds();
        SetSelection(element, additive: false);
        return element;
    }

    /// <summary>把元素锚定到容器（默认居中），供“元素拖入容器自动居中”使用。</summary>
    public void AnchorToRegion(LayoutElementViewModel element, string regionId)
    {
        element.RegionId = regionId;
        element.RegionHAlign = "Center";
        element.RegionVAlign = "Center";
    }

    /// <summary>解除容器锚定，并把元素位置更新为当前解析位置（拖拽 / 缩放前调用）。</summary>
    public void DetachFromRegion(LayoutElementViewModel element)
    {
        if (element.RegionId is null)
        {
            return;
        }

        var layout = BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var bounds = LabelLayoutResolver.ResolveBounds(element.ToElement(), regions);
        element.XMm = bounds.XMm;
        element.YMm = bounds.YMm;
        element.RegionId = null;
    }

    /// <summary>按像素位移移动元素（拖拽用）；已锚定元素先解除锚定到当前位置。</summary>
    public void MoveElements(IEnumerable<LayoutElementViewModel> elements, double dxPx, double dyPx)
    {
        var dx = dxPx / PixelsPerMm;
        var dy = dyPx / PixelsPerMm;
        foreach (var element in elements)
        {
            if (element.RegionId is not null)
            {
                DetachFromRegion(element);
            }

            element.XMm += dx;
            element.YMm += dy;
        }
    }

    /// <summary>元素中心落入容器时自动锚定居中；移出容器解除锚定。</summary>
    public void AutoAnchor(LayoutElementViewModel element)
    {
        var layout = BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var bounds = LabelLayoutResolver.ResolveBounds(element.ToElement(), regions);
        var centerX = bounds.XMm + bounds.WidthMm / 2;
        var centerY = bounds.YMm + bounds.HeightMm / 2;
        var hit = regions.Values.FirstOrDefault(r =>
            centerX >= r.XMm && centerX <= r.XMm + r.WidthMm &&
            centerY >= r.YMm && centerY <= r.YMm + r.HeightMm);

        if (hit is not null && element.RegionId != hit.Id)
        {
            AnchorToRegion(element, hit.Id);
        }
        else if (hit is null && element.RegionId is not null)
        {
            element.RegionId = null;
        }
    }

    /// <summary>对齐选中的多个元素（以选中集合包围框为基准，容器不参与）。</summary>
    public void AlignSelected(EditorAlign align)
    {
        var selected = SelectedElements.Where(e => e.Type != EditorElementType.Region).ToList();
        if (selected.Count < 2)
        {
            return;
        }

        var layout = BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var bounds = selected.Select(e => LabelLayoutResolver.ResolveBounds(e.ToElement(), regions)).ToList();
        var left = bounds.Min(b => b.XMm);
        var right = bounds.Max(b => b.XMm + b.WidthMm);
        var top = bounds.Min(b => b.YMm);
        var bottom = bounds.Max(b => b.YMm + b.HeightMm);

        for (var i = 0; i < selected.Count; i++)
        {
            var element = selected[i];
            var b = bounds[i];
            if (element.RegionId is not null)
            {
                element.RegionId = null;
            }

            switch (align)
            {
                case EditorAlign.Left:
                    element.XMm = left;
                    break;
                case EditorAlign.CenterH:
                    element.XMm = left + (right - left - b.WidthMm) / 2;
                    break;
                case EditorAlign.Right:
                    element.XMm = right - b.WidthMm;
                    break;
                case EditorAlign.Top:
                    element.YMm = top;
                    break;
                case EditorAlign.CenterV:
                    element.YMm = top + (bottom - top - b.HeightMm) / 2;
                    break;
                case EditorAlign.Bottom:
                    element.YMm = bottom - b.HeightMm;
                    break;
            }
        }

        Log($"已对齐 {selected.Count} 个元素（{align}）。");
    }

    /// <summary>
    /// 计算移动吸附：移动元素包围框的边缘 / 中心吸附到画布边界与其它元素（含容器）边缘 / 中心。
    /// 返回修正后的位移（毫米）。
    /// </summary>
    public (double DxMm, double DyMm) SnapDeltaMm(IReadOnlyList<LayoutElementViewModel> moving, double dxMm, double dyMm)
    {
        const double thresholdMm = 0.8;
        if (moving.Count == 0 || (dxMm == 0 && dyMm == 0))
        {
            return (dxMm, dyMm);
        }

        var layout = BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var bounds = moving.Select(e => LabelLayoutResolver.ResolveBounds(e.ToElement(), regions)).ToList();
        var beforeLeft = bounds.Min(b => b.XMm);
        var beforeTop = bounds.Min(b => b.YMm);
        var beforeRight = bounds.Max(b => b.XMm + b.WidthMm);
        var beforeBottom = bounds.Max(b => b.YMm + b.HeightMm);
        var afterLeft = beforeLeft + dxMm;
        var afterTop = beforeTop + dyMm;
        var afterRight = beforeRight + dxMm;
        var afterBottom = beforeBottom + dyMm;

        // 候选线：画布边界 / 中心 + 其它元素（含容器）边界 / 中心
        var candidatesX = new List<double> { 0, WidthMm / 2, WidthMm };
        var candidatesY = new List<double> { 0, HeightMm / 2, HeightMm };
        var movingSet = new HashSet<LayoutElementViewModel>(moving);
        foreach (var other in Elements)
        {
            if (movingSet.Contains(other))
            {
                continue;
            }

            var ob = LabelLayoutResolver.ResolveBounds(other.ToElement(), regions);
            candidatesX.Add(ob.XMm);
            candidatesX.Add(ob.XMm + ob.WidthMm / 2);
            candidatesX.Add(ob.XMm + ob.WidthMm);
            candidatesY.Add(ob.YMm);
            candidatesY.Add(ob.YMm + ob.HeightMm / 2);
            candidatesY.Add(ob.YMm + ob.HeightMm);
        }

        var (adjDx, _) = SnapAxis(
            candidatesX,
            afterLeft,
            (afterLeft + afterRight) / 2,
            afterRight,
            thresholdMm);
        var (adjDy, _) = SnapAxis(
            candidatesY,
            afterTop,
            (afterTop + afterBottom) / 2,
            afterBottom,
            thresholdMm);
        return (dxMm + adjDx, dyMm + adjDy);
    }

    private static (double Adjustment, bool Snapped) SnapAxis(
        IReadOnlyList<double> candidates,
        double afterStart,
        double afterCenter,
        double afterEnd,
        double threshold)
    {
        var best = (Adjustment: 0.0, Snapped: false);
        var bestDistance = threshold;
        foreach (var candidate in candidates)
        {
            foreach (var (target, delta) in new[]
            {
                (afterStart, candidate - afterStart),
                (afterCenter, candidate - afterCenter),
                (afterEnd, candidate - afterEnd),
            })
            {
                var distance = Math.Abs(delta);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = (delta, true);
                }
            }
        }

        return best;
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
        Log($"已保存：{Name} v{Version}");
    }

    /// <summary>打印测试：先保存，再用测试数据提交作业并轮询结果。</summary>
    public async Task PrintTestAsync()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("未连接 WinHost。");
        }

        await SaveAsync();
        var data = TestFields.ToDictionary(f => f.Key, f => f.Value ?? string.Empty);
        var job = await _client.SubmitJobAsync($"design-{Guid.NewGuid():N}", BuildSaveDto(), [data]);
        Log($"已提交打印测试 {job.JobId}（{job.TotalItems} 张）。");

        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(500);
            var current = await _client.GetJobAsync(job.JobId);
            if (current.Status is "Completed" or "Failed" or "Cancelled")
            {
                var error = current.Items.FirstOrDefault(x => x.ErrorMessage is not null)?.ErrorMessage;
                Log($"打印测试终态：{current.Status}（{current.CompletedItems}/{current.TotalItems}）{error ?? string.Empty}");
                return;
            }
        }

        Log("打印测试超时，请查看 WinHost 日志。");
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