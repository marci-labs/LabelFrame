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

/// <summary>模板设计器（独立窗口）：控件栏 / 画布 / 属性分组 / 填充 / 区域 / 实时预览 / 打印测试。</summary>
public partial class DesignerWindow : Window
{
    private readonly StudioClient _client;
    private readonly LayoutEditorViewModel _viewModel;
    private readonly LabelPreviewRenderer _renderer = new();
    private readonly DispatcherTimer _previewTimer;

    private LayoutElementViewModel? _dragElement;
    private Point _dragStart;
    private string? _fieldOldKey;
    private bool _isDrawingRegion;
    private Point _regionStart;
    private Rectangle? _regionPreview;

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
        _viewModel.Elements.CollectionChanged += OnCollectionChanged;
        _viewModel.Fields.CollectionChanged += OnCollectionChanged;
        _viewModel.TestFields.CollectionChanged += OnCollectionChanged;
        foreach (var field in _viewModel.TestFields)
        {
            field.PropertyChanged += OnFieldValueChanged;
        }

        RedrawCanvas();
        RenderPreview();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayoutEditorViewModel.CanvasWidthPx)
            or nameof(LayoutEditorViewModel.CanvasHeightPx)
            or nameof(LayoutEditorViewModel.PixelsPerMm)
            or nameof(LayoutEditorViewModel.SelectedElement)
            or nameof(LayoutEditorViewModel.CanvasBackground))
        {
            RedrawCanvas();
        }

        SchedulePreview();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawCanvas();
        SchedulePreview();
    }

    private void OnFieldValueChanged(object? sender, PropertyChangedEventArgs e)
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

    private void RedrawCanvas()
    {
        EditorCanvas.Children.Clear();
        var pps = _viewModel.PixelsPerMm;
        var selected = _viewModel.SelectedElement;
        var layout = _viewModel.BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var models = layout.Elements.ToList();

        for (var i = 0; i < _viewModel.Elements.Count; i++)
        {
            var element = _viewModel.Elements[i];
            var bounds = LabelLayoutResolver.ResolveBounds(models[i], regions);
            var ui = CreateElementUi(element, bounds, pps, ReferenceEquals(element, selected));
            if (ui is null)
            {
                continue;
            }

            ui.Tag = element;
            EditorCanvas.Children.Add(ui);
        }
    }

    private static FrameworkElement? CreateElementUi(
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
                var text = new TextBlock
                {
                    Text = element.IsLiteral && !string.IsNullOrEmpty(element.Literal) ? element.Literal : element.SourceKey,
                    FontFamily = new FontFamily("Microsoft YaHei"),
                    FontSize = Math.Max(8, element.FontHeightMm * pps),
                    Foreground = Brushes.Black,
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
                var content = element.IsLiteral && !string.IsNullOrEmpty(element.Literal) ? element.Literal : element.SourceKey;
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
                    Fill = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x80, 0xFF)),
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                return rect;
            }
            default:
                return null;
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (RegionTool.IsChecked == true)
        {
            _isDrawingRegion = true;
            _regionStart = e.GetPosition(EditorCanvas);
            _regionPreview = new Rectangle
            {
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x80, 0xFF)),
            };
            EditorCanvas.Children.Add(_regionPreview);
            e.Handled = true;
            return;
        }

        var hit = FindElement(e.OriginalSource as DependencyObject);
        if (hit is not null)
        {
            if (hit.RegionId is not null)
            {
                hit.RegionId = null;
                _viewModel.Log($"已解除区域锚定：{hit.DisplayLabel}");
            }

            _viewModel.SelectedElement = hit;
            _dragElement = hit;
            _dragStart = e.GetPosition(EditorCanvas);
            EditorCanvas.CaptureMouse();
            e.Handled = true;
        }
        else
        {
            _viewModel.SelectedElement = null;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(EditorCanvas);

        if (_isDrawingRegion && _regionPreview is not null)
        {
            var x = Math.Min(_regionStart.X, pos.X);
            var y = Math.Min(_regionStart.Y, pos.Y);
            var w = Math.Abs(pos.X - _regionStart.X);
            var h = Math.Abs(pos.Y - _regionStart.Y);
            _regionPreview.Width = Math.Max(4, w);
            _regionPreview.Height = Math.Max(4, h);
            Canvas.SetLeft(_regionPreview, x);
            Canvas.SetTop(_regionPreview, y);
            return;
        }

        if (_dragElement is null)
        {
            return;
        }

        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        _viewModel.MoveElement(_dragElement, dx, dy);
        _dragStart = pos;
        AutoAnchor(_dragElement);
        RedrawCanvas();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawingRegion && _regionPreview is not null)
        {
            _isDrawingRegion = false;
            var pos = e.GetPosition(EditorCanvas);
            var x = Math.Min(_regionStart.X, pos.X);
            var y = Math.Min(_regionStart.Y, pos.Y);
            var w = Math.Abs(pos.X - _regionStart.X);
            var h = Math.Abs(pos.Y - _regionStart.Y);
            EditorCanvas.Children.Remove(_regionPreview);
            _regionPreview = null;
            if (w > 4 && h > 4)
            {
                var pps = _viewModel.PixelsPerMm;
                _viewModel.AddRegionAt(x / pps, y / pps, w / pps, h / pps);
                _viewModel.Log("已创建区域（元素拖入格子可自动居中）。");
            }

            return;
        }

        _dragElement = null;
        EditorCanvas.ReleaseMouseCapture();
    }

    /// <summary>元素中心落入区域时自动锚定居中；移出区域解除锚定。</summary>
    private void AutoAnchor(LayoutElementViewModel element)
    {
        var layout = _viewModel.BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var bounds = LabelLayoutResolver.ResolveBounds(element.ToElement(), regions);
        var centerX = bounds.XMm + bounds.WidthMm / 2;
        var centerY = bounds.YMm + bounds.HeightMm / 2;
        var hit = regions.Values.FirstOrDefault(r =>
            centerX >= r.XMm && centerX <= r.XMm + r.WidthMm &&
            centerY >= r.YMm && centerY <= r.YMm + r.HeightMm);

        if (hit is not null && element.RegionId != hit.Id)
        {
            _viewModel.AnchorToRegion(element, hit.Id);
            _viewModel.Log($"元素已放入区域 {hit.Id}（居中）。");
        }
        else if (hit is null && element.RegionId is not null)
        {
            element.RegionId = null;
            _viewModel.Log("元素已移出区域，恢复绝对定位。");
        }
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

    private void ElementButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is Button button && e.LeftButton == MouseButtonState.Pressed && button.Tag is string type)
        {
            DragDrop.DoDragDrop(button, type, DragDropEffects.Copy);
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
        }

        _viewModel.Log($"已从控件栏添加 {type}。");
        e.Handled = true;
    }

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

    private void AddElement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string type } && Enum.TryParse<EditorElementType>(type, out var elementType))
        {
            _viewModel.AddElement(elementType);
        }
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
        => _viewModel.AddField();

    private void RemoveField_Click(object sender, RoutedEventArgs e)
    {
        if (FieldList.SelectedItem is ContractFieldViewModel field)
        {
            _viewModel.RemoveField(field);
        }
    }

    private void FieldList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _fieldOldKey = (FieldList.SelectedItem as ContractFieldViewModel)?.Key;

    private void FieldKey_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FieldList.SelectedItem is ContractFieldViewModel field && _fieldOldKey is not null)
        {
            _viewModel.RenameField(_fieldOldKey, field.Key);
            _fieldOldKey = field.Key;
        }
    }
}