using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using LabelFrame.Studio.Services;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio;

/// <summary>缩放手柄类型。</summary>
internal enum ResizeHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>模板设计器（迭代 8D）：设计 / 测试分离、控件栏拖拽、标尺画布、框选多选、手柄缩放、中键平移、Ctrl+滚轮缩放、属性选中显示、底部横跨状态日志。</summary>
public partial class DesignerWindow : Window
{
    private readonly StudioClient _client;
    private readonly LayoutEditorViewModel _viewModel;
    private readonly LabelPreviewRenderer _renderer = new();
    private readonly DispatcherTimer _previewTimer;

    private List<LayoutElementViewModel> _dragElements = [];
    private Point _dragStart;
    private Point _paletteStart;
    private bool _paletteDragging;
    private bool _panActive;
    private Point _panStart;
    private (double X, double Y) _panScrollStart;

    private Rectangle? _marquee;
    private Point _marqueeStart;

    private HandleTag? _resize;
    private Point _resizeStartPos;
    private ElementBounds _resizeStartBounds;

    public DesignerWindow(StudioClient client, TemplateSaveDto template)
    {
        InitializeComponent();
        _client = client;
        _viewModel = new LayoutEditorViewModel(client);
        DataContext = _viewModel;
        _viewModel.LoadFrom(template);
        _viewModel.Log($"已打开模板：{template.Name}");

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RenderPreview(); };

        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.Elements.CollectionChanged += OnElementsChanged;
        _viewModel.TestFields.CollectionChanged += (_, _) => SchedulePreview();
        _viewModel.Logs.CollectionChanged += (_, _) => ScrollLogsToEnd();
        foreach (var element in _viewModel.Elements)
        {
            element.PropertyChanged += OnElementPropertyChanged;
        }

        foreach (var field in _viewModel.TestFields)
        {
            field.PropertyChanged += OnTestFieldValueChanged;
        }

        RedrawCanvas();
        RedrawRulers();
        RenderPreview();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayoutEditorViewModel.CanvasWidthPx)
            or nameof(LayoutEditorViewModel.CanvasHeightPx)
            or nameof(LayoutEditorViewModel.PixelsPerMm)
            or nameof(LayoutEditorViewModel.SelectedElement)
            or nameof(LayoutEditorViewModel.CanvasBackground)
            or nameof(LayoutEditorViewModel.SelectedElements)
            or nameof(LayoutEditorViewModel.HasSelection)
            or nameof(LayoutEditorViewModel.IsSingleSelection))
        {
            RedrawCanvas();
            RedrawRulers();
        }

        SchedulePreview();
    }

    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        RedrawCanvas();
        RedrawRulers();
        SchedulePreview();
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RedrawCanvas();
        SchedulePreview();
    }

    private void OnTestFieldValueChanged(object? sender, PropertyChangedEventArgs e)
        => SchedulePreview();

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RenderPreview()
    {
        var document = new LabelFrame.Core.Documents.LabelDocument
        {
            Layout = _viewModel.BuildLayout(),
            Data = _viewModel.TestFields.ToDictionary(f => f.Key, f => f.Value ?? string.Empty),
        };
        var png = _renderer.RenderPng(document, dpi: 203);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        PreviewImage.Source = image;
    }

    // ---------- 画布绘制 ----------

    private void RedrawCanvas()
    {
        EditorCanvas.Children.Clear();
        var pps = _viewModel.PixelsPerMm;
        var layout = _viewModel.BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var models = layout.Elements.ToList();
        var selectedSet = new HashSet<LayoutElementViewModel>(_viewModel.SelectedElements);
        var boundsByElement = new Dictionary<LayoutElementViewModel, ElementBounds>();

        for (var i = 0; i < _viewModel.Elements.Count; i++)
        {
            var element = _viewModel.Elements[i];
            var bounds = LabelLayoutResolver.ResolveBounds(models[i], regions);
            boundsByElement[element] = bounds;
            var ui = CreateElementUi(element, bounds, pps, selectedSet.Contains(element));
            if (ui is null)
            {
                continue;
            }

            ui.Tag = element;
            EditorCanvas.Children.Add(ui);
            if (element.Type == EditorElementType.Region)
            {
                EditorCanvas.Children.Add(CreateRegionLabel(element, bounds, pps));
            }
        }

        if (_viewModel.IsSingleSelection && _viewModel.SelectedElement is { } single && single.Type != EditorElementType.Line
            && boundsByElement.TryGetValue(single, out var singleBounds))
        {
            DrawSelectionHandles(single, singleBounds, pps);
        }
        else if (_viewModel.SelectedElements.Count > 1)
        {
            DrawMultiSelectionBox(selectedSet, boundsByElement, pps);
        }
    }

    private static Border CreateRegionLabel(LayoutElementViewModel element, ElementBounds bounds, double pps)
    {
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xF4, 0xE0)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"容器 {element.Id}",
                FontSize = 10,
                Foreground = Brushes.DimGray,
            },
        };
        Canvas.SetLeft(label, bounds.XMm * pps + 2);
        Canvas.SetTop(label, bounds.YMm * pps + 2);
        return label;
    }

    private FrameworkElement? CreateElementUi(
        LayoutElementViewModel element,
        ElementBounds bounds,
        double pps,
        bool isSelected)
    {
        var highlight = isSelected ? Brushes.DodgerBlue : Brushes.Silver;
        var x = bounds.XMm * pps;
        var y = bounds.YMm * pps;

        switch (element.Type)
        {
            case EditorElementType.Text:
            {
                var content = TextContent(element);
                var text = new TextBlock
                {
                    Text = content,
                    FontFamily = new FontFamily("Microsoft YaHei"),
                    FontSize = Math.Max(8, element.FontHeightMm * pps),
                    Foreground = string.IsNullOrWhiteSpace(element.SourceKey) && !element.IsLiteral ? Brushes.Gray : Brushes.Black,
                    Background = Brushes.Transparent,
                    MinWidth = 24,
                    MinHeight = 16,
                };
                if (bounds.WidthMm > 0)
                {
                    var box = new Border
                    {
                        Width = Math.Max(1, bounds.WidthMm * pps),
                        Height = Math.Max(1, bounds.HeightMm * pps),
                        BorderBrush = element.BorderMm > 0 ? Brushes.Black : highlight,
                        BorderThickness = new Thickness(Math.Max(1, element.BorderMm * pps)),
                        Padding = new Thickness(element.PaddingMm * pps),
                    };
                    text.HorizontalAlignment = element.TextAlign switch
                    {
                        "Center" => HorizontalAlignment.Center,
                        "Right" => HorizontalAlignment.Right,
                        _ => HorizontalAlignment.Left,
                    };
                    box.Child = text;
                    Canvas.SetLeft(box, x);
                    Canvas.SetTop(box, y);
                    return box;
                }

                if (isSelected)
                {
                    text.Foreground = Brushes.DodgerBlue;
                    text.FontWeight = FontWeights.Bold;
                }

                Canvas.SetLeft(text, x);
                Canvas.SetTop(text, y);
                return text;
            }
            case EditorElementType.Barcode:
            case EditorElementType.QrCode:
            case EditorElementType.Image:
            {
                var width = Math.Max(20, bounds.WidthMm * pps);
                var height = Math.Max(20, bounds.HeightMm * pps);
                var label = element.Type switch
                {
                    EditorElementType.Barcode => "条码",
                    EditorElementType.QrCode => "二维码",
                    _ => "图片",
                };
                var content = TextContent(element);
                var box = new Border
                {
                    Width = width,
                    Height = height,
                    BorderBrush = element.BorderMm > 0 ? Brushes.Black : highlight,
                    BorderThickness = new Thickness(Math.Max(1, element.BorderMm * pps)),
                    Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    Child = new TextBlock
                    {
                        Text = $"{label}: {content}",
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = string.IsNullOrEmpty(content) || content.StartsWith("（") ? Brushes.Gray : Brushes.Black,
                    },
                };
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);
                return box;
            }
            case EditorElementType.Line:
            {
                var line = new Line
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = (element.X2Mm - element.XMm) * pps,
                    Y2 = (element.Y2Mm - element.YMm) * pps,
                    Stroke = Brushes.Black,
                    StrokeThickness = Math.Max(1, element.ThicknessMm * pps),
                };
                Canvas.SetLeft(line, element.XMm * pps);
                Canvas.SetTop(line, element.YMm * pps);
                return line;
            }
            case EditorElementType.Region:
            {
                var rect = new Rectangle
                {
                    Width = Math.Max(20, bounds.WidthMm * pps),
                    Height = Math.Max(20, bounds.HeightMm * pps),
                    Stroke = element.BorderMm > 0 ? Brushes.Black : Brushes.Gray,
                    StrokeThickness = Math.Max(1, element.BorderMm * pps),
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x80, 0xFF)),
                    Tag = element,
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                return rect;
            }
            default:
                return null;
        }
    }

    private void DrawSelectionHandles(LayoutElementViewModel element, ElementBounds bounds, double pps)
    {
        var x = bounds.XMm * pps;
        var y = bounds.YMm * pps;
        var w = Math.Max(16, bounds.WidthMm * pps);
        var h = Math.Max(16, bounds.HeightMm * pps);

        var frame = new Rectangle
        {
            Width = w,
            Height = h,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent,
            Tag = element,
        };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        EditorCanvas.Children.Add(frame);

        const double handleSize = 7;
        var handles = new[]
        {
            (0.0, 0.0, ResizeHandle.TopLeft, Cursors.SizeNWSE),
            (0.5, 0.0, ResizeHandle.Top, Cursors.SizeNS),
            (1.0, 0.0, ResizeHandle.TopRight, Cursors.SizeNESW),
            (0.0, 0.5, ResizeHandle.Left, Cursors.SizeWE),
            (1.0, 0.5, ResizeHandle.Right, Cursors.SizeWE),
            (0.0, 1.0, ResizeHandle.BottomLeft, Cursors.SizeNESW),
            (0.5, 1.0, ResizeHandle.Bottom, Cursors.SizeNS),
            (1.0, 1.0, ResizeHandle.BottomRight, Cursors.SizeNWSE),
        };
        foreach (var (fx, fy, handle, cursor) in handles)
        {
            var rect = new Rectangle
            {
                Width = handleSize,
                Height = handleSize,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1.5,
                Cursor = cursor,
                Tag = new HandleTag(element, handle),
            };
            Canvas.SetLeft(rect, x + w * fx - handleSize / 2);
            Canvas.SetTop(rect, y + h * fy - handleSize / 2);
            EditorCanvas.Children.Add(rect);
        }
    }

    private void DrawMultiSelectionBox(
        HashSet<LayoutElementViewModel> selected,
        IReadOnlyDictionary<LayoutElementViewModel, ElementBounds> boundsByElement,
        double pps)
    {
        var boxes = selected
            .Where(boundsByElement.ContainsKey)
            .Select(e => boundsByElement[e])
            .ToList();
        if (boxes.Count == 0)
        {
            return;
        }

        var left = boxes.Min(b => b.XMm) * pps;
        var top = boxes.Min(b => b.YMm) * pps;
        var right = boxes.Max(b => (b.XMm + b.WidthMm) * pps);
        var bottom = boxes.Max(b => (b.YMm + b.HeightMm) * pps);
        var frame = new Rectangle
        {
            Width = Math.Max(0, right - left),
            Height = Math.Max(0, bottom - top),
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(0x10, 0x1E, 0x90, 0xFF)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(frame, left);
        Canvas.SetTop(frame, top);
        EditorCanvas.Children.Add(frame);
    }

    /// <summary>元素在画布上显示的内容：固定值 / 测试数据 / 字段键占位 / 未绑定提示。</summary>
    private string TextContent(LayoutElementViewModel element)
    {
        if (element.IsLiteral)
        {
            return string.IsNullOrEmpty(element.Literal) ? "（固定值）" : element.Literal;
        }

        if (string.IsNullOrWhiteSpace(element.SourceKey))
        {
            return "（未绑定字段）";
        }

        var entry = _viewModel.TestFields.FirstOrDefault(f => f.Key == element.SourceKey);
        if (entry is not null && !string.IsNullOrEmpty(entry.Value))
        {
            return entry.Value;
        }

        return element.SourceKey;
    }

    private void RedrawRulers()
    {
        HRuler.Children.Clear();
        VRuler.Children.Clear();
        var pps = _viewModel.PixelsPerMm;

        for (var mm = 0; mm <= _viewModel.WidthMm + 0.001; mm++)
        {
            var x = mm * pps;
            var major5 = mm % 5 == 0;
            var major10 = mm % 10 == 0;
            var h = major10 ? 14 : major5 ? 10 : 5;
            HRuler.Children.Add(new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = h,
                Stroke = Brushes.Gray,
                StrokeThickness = 0.8,
            });
            if (major10)
            {
                var text = new TextBlock
                {
                    Text = mm.ToString(),
                    FontSize = 8,
                    Foreground = Brushes.Gray,
                };
                Canvas.SetLeft(text, x + 2);
                Canvas.SetTop(text, 0);
                HRuler.Children.Add(text);
            }
        }

        for (var mm = 0; mm <= _viewModel.HeightMm + 0.001; mm++)
        {
            var y = mm * pps;
            var major5 = mm % 5 == 0;
            var major10 = mm % 10 == 0;
            var w = major10 ? 14 : major5 ? 10 : 5;
            VRuler.Children.Add(new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = w,
                Y2 = y,
                Stroke = Brushes.Gray,
                StrokeThickness = 0.8,
            });
            if (major10)
            {
                var text = new TextBlock
                {
                    Text = mm.ToString(),
                    FontSize = 8,
                    Foreground = Brushes.Gray,
                };
                Canvas.SetLeft(text, 1);
                Canvas.SetTop(text, y + 2);
                VRuler.Children.Add(text);
            }
        }
    }

    // ---------- 鼠标交互 ----------

    private void Canvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panActive = true;
            _panStart = e.GetPosition(this);
            _panScrollStart = (CanvasScroll.HorizontalOffset, CanvasScroll.VerticalOffset);
            EditorCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _panActive)
        {
            _panActive = false;
            EditorCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(EditorCanvas);

        if (FindHandle(e.OriginalSource as DependencyObject) is { } handleTag)
        {
            _resize = handleTag;
            _resizeStartPos = pos;
            _resizeStartBounds = GetBoundsMm(handleTag.Element);
            _viewModel.DetachFromRegion(handleTag.Element);
            EditorCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        var hit = FindElement(e.OriginalSource as DependencyObject);
        if (hit is not null)
        {
            var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (additive || !_viewModel.SelectedElements.Contains(hit))
            {
                _viewModel.SetSelection(hit, additive);
            }

            _dragElements = _viewModel.SelectedElements.ToList();
            _dragStart = pos;
            EditorCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 空白：框选
        _viewModel.ClearSelection();
        _marqueeStart = pos;
        _marquee = new Rectangle
        {
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(0x28, 0x1E, 0x90, 0xFF)),
            IsHitTestVisible = false,
        };
        EditorCanvas.Children.Add(_marquee);
        EditorCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panActive)
        {
            var pos = e.GetPosition(this);
            CanvasScroll.ScrollToHorizontalOffset(_panScrollStart.X - (pos.X - _panStart.X));
            CanvasScroll.ScrollToVerticalOffset(_panScrollStart.Y - (pos.Y - _panStart.Y));
            e.Handled = true;
            return;
        }

        var canvasPos = e.GetPosition(EditorCanvas);

        if (_resize is not null)
        {
            ResizeMove(canvasPos);
            return;
        }

        if (_marquee is not null)
        {
            var x = Math.Min(_marqueeStart.X, canvasPos.X);
            var y = Math.Min(_marqueeStart.Y, canvasPos.Y);
            _marquee.Width = Math.Abs(canvasPos.X - _marqueeStart.X);
            _marquee.Height = Math.Abs(canvasPos.Y - _marqueeStart.Y);
            Canvas.SetLeft(_marquee, x);
            Canvas.SetTop(_marquee, y);
            return;
        }

        if (_dragElements.Count == 0)
        {
            return;
        }

        var dxPx = canvasPos.X - _dragStart.X;
        var dyPx = canvasPos.Y - _dragStart.Y;
        if (dxPx == 0 && dyPx == 0)
        {
            return;
        }

        var pps = _viewModel.PixelsPerMm;
        var (dxMm, dyMm) = _viewModel.SnapDeltaMm(_dragElements, dxPx / pps, dyPx / pps);
        _viewModel.MoveElements(_dragElements, dxMm * pps, dyMm * pps);
        _dragStart = canvasPos;
        if (_dragElements.Count == 1)
        {
            _viewModel.AutoAnchor(_dragElements[0]);
        }

        RedrawCanvas();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_marquee is not null)
        {
            var rect = new Rect(
                Canvas.GetLeft(_marquee),
                Canvas.GetTop(_marquee),
                _marquee.Width,
                _marquee.Height);
            EditorCanvas.Children.Remove(_marquee);
            _marquee = null;
            var pps = _viewModel.PixelsPerMm;
            var hit = new List<LayoutElementViewModel>();
            foreach (var element in _viewModel.Elements)
            {
                var b = GetBoundsMm(element);
                var elementRect = new Rect(
                    b.XMm * pps,
                    b.YMm * pps,
                    Math.Max(4, b.WidthMm * pps),
                    Math.Max(4, b.HeightMm * pps));
                if (rect.IntersectsWith(elementRect))
                {
                    hit.Add(element);
                }
            }

            _viewModel.SelectRange(hit);
            _viewModel.Log(hit.Count > 0 ? $"已框选 {hit.Count} 个元素。" : "框选未命中元素。");
            EditorCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        _resize = null;
        _dragElements = [];
        if (EditorCanvas.IsMouseCaptured)
        {
            EditorCanvas.ReleaseMouseCapture();
        }
    }

    private void Canvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var pos = e.GetPosition(EditorCanvas);
        var oldZoom = _viewModel.Zoom;
        var newZoom = Math.Clamp(oldZoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 4.0);
        if (Math.Abs(newZoom - oldZoom) < 0.001)
        {
            e.Handled = true;
            return;
        }

        _viewModel.Zoom = newZoom;
        var newPx = new Point(pos.X / oldZoom * newZoom, pos.Y / oldZoom * newZoom);
        CanvasScroll.ScrollToHorizontalOffset(CanvasScroll.HorizontalOffset + (newPx.X - pos.X));
        CanvasScroll.ScrollToVerticalOffset(CanvasScroll.VerticalOffset + (newPx.Y - pos.Y));
        e.Handled = true;
    }

    private void ResizeMove(Point canvasPos)
    {
        var element = _resize!.Element;
        var handle = _resize!.Handle;
        var pps = _viewModel.PixelsPerMm;
        var dxMm = (canvasPos.X - _resizeStartPos.X) / pps;
        var dyMm = (canvasPos.Y - _resizeStartPos.Y) / pps;
        var b = _resizeStartBounds;

        const double minW = 2;
        const double minH = 2;
        var left = b.XMm;
        var top = b.YMm;
        var right = b.XMm + b.WidthMm;
        var bottom = b.YMm + b.HeightMm;

        switch (handle)
        {
            case ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft:
                left = Math.Min(b.XMm + dxMm, right - minW);
                break;
            case ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight:
                right = Math.Max(b.XMm + b.WidthMm + dxMm, b.XMm + minW);
                break;
        }

        switch (handle)
        {
            case ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight:
                top = Math.Min(b.YMm + dyMm, bottom - minH);
                break;
            case ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight:
                bottom = Math.Max(b.YMm + b.HeightMm + dyMm, b.YMm + minH);
                break;
        }

        ApplyResize(element, left, top, right, bottom);
        RedrawCanvas();
    }

    private void ApplyResize(LayoutElementViewModel element, double left, double top, double right, double bottom)
    {
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        switch (element.Type)
        {
            case EditorElementType.Text:
                element.XMm = left;
                element.YMm = top;
                element.WidthMm = width;
                element.FontHeightMm = Math.Max(1.5, height);
                break;
            case EditorElementType.Barcode:
                element.XMm = left;
                element.YMm = top;
                element.HeightMm = Math.Max(3, height);
                break;
            case EditorElementType.QrCode:
            {
                var size = Math.Max(3, Math.Max(width, height));
                element.XMm = HandleIncludesLeft(_resize!.Handle) ? right - size : left;
                element.YMm = HandleIncludesTop(_resize!.Handle) ? bottom - size : top;
                element.WidthMm = size;
                element.HeightMm = size;
                break;
            }
            case EditorElementType.Image:
            case EditorElementType.Region:
                element.XMm = left;
                element.YMm = top;
                element.WidthMm = Math.Max(2, width);
                element.HeightMm = Math.Max(2, height);
                break;
        }
    }

    private static bool HandleIncludesLeft(ResizeHandle handle)
        => handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft;

    private static bool HandleIncludesTop(ResizeHandle handle)
        => handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight;

    // ---------- 命中检测 ----------

    private static HandleTag? FindHandle(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: HandleTag tag })
            {
                return tag;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static LayoutElementViewModel? FindElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: LayoutElementViewModel vm })
            {
                return vm;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private ElementBounds GetBoundsMm(LayoutElementViewModel element)
    {
        var layout = _viewModel.BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        return LabelLayoutResolver.ResolveBounds(element.ToElement(), regions);
    }

    // ---------- 控件栏 ----------

    private void Palette_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _paletteStart = e.GetPosition((IInputElement)sender);
        _paletteDragging = false;
    }

    private void Palette_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Border { Tag: string type })
        {
            return;
        }

        var pos = e.GetPosition((IInputElement)sender);
        if (Math.Abs(pos.X - _paletteStart.X) < 5 && Math.Abs(pos.Y - _paletteStart.Y) < 5)
        {
            return;
        }

        _paletteDragging = true;
        DragDrop.DoDragDrop((DependencyObject)sender, type, DragDropEffects.Copy);
    }

    private void Palette_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_paletteDragging)
        {
            _paletteDragging = false;
            return;
        }

        if (sender is Border { Tag: string type } && Enum.TryParse<EditorElementType>(type, out var elementType))
        {
            _viewModel.AddElement(elementType);
        }
    }

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.Text) || !Enum.TryParse<EditorElementType>(e.Data.GetData(DataFormats.Text) as string, out var type))
        {
            return;
        }

        _viewModel.AddElement(type);
        var pos = e.GetPosition(EditorCanvas);
        if (_viewModel.SelectedElement is { } element)
        {
            element.XMm = pos.X / _viewModel.PixelsPerMm;
            element.YMm = pos.Y / _viewModel.PixelsPerMm;
            if (type != EditorElementType.Region)
            {
                _viewModel.AutoAnchor(element);
            }
        }

        _viewModel.Log($"已从控件栏添加 {type}。");
        e.Handled = true;
    }

    // ---------- 命令 ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (e.Key is Key.Delete or Key.Back && _viewModel.SelectedElements.Count > 0)
        {
            _viewModel.DeleteSelected();
            e.Handled = true;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
        => _viewModel.DeleteSelected();

    private void Align_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<EditorAlign>(tag, out var align))
        {
            _viewModel.AlignSelected(align);
        }
    }

    private void AlignMenu_Click(object sender, RoutedEventArgs e)
        => Align_Click(sender, e);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SaveAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PrintTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.PrintTestAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Log($"打印测试失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "打印测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
        => _viewModel.ClearLogs();

    private void ScrollLogsToEnd()
    {
        if (LogList.Items.Count == 0)
        {
            return;
        }

        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }
}

/// <summary>手柄命中标签。</summary>
internal sealed record HandleTag(LayoutElementViewModel Element, ResizeHandle Handle);
