using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LabelFrame.Core.Layout;
using LabelFrame.Studio.Services;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio;

/// <summary>版式编辑窗口（V2B）：画布拖拽 / 属性面板 / 字段编辑 / 区域布局 / 保存 / 预览。</summary>
public partial class EditorWindow : Window
{
    private readonly StudioClient _client;
    private readonly LayoutEditorViewModel _viewModel;
    private LayoutElementViewModel? _dragElement;
    private Point _dragStart;
    private string? _fieldOldKey;

    public EditorWindow(StudioClient client, TemplateSaveDto template)
    {
        InitializeComponent();
        _client = client;
        _viewModel = new LayoutEditorViewModel(client);
        DataContext = _viewModel;
        _viewModel.LoadFrom(template);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Elements.CollectionChanged += OnElementsChanged;
        RedrawCanvas();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayoutEditorViewModel.CanvasWidthPx)
            or nameof(LayoutEditorViewModel.CanvasHeightPx)
            or nameof(LayoutEditorViewModel.PixelsPerMm)
            or nameof(LayoutEditorViewModel.SelectedElement))
        {
            RedrawCanvas();
        }
    }

    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RedrawCanvas();

    private void RedrawCanvas()
    {
        EditorCanvas.Children.Clear();
        var pps = _viewModel.PixelsPerMm;
        var selected = _viewModel.SelectedElement;

        // 用解析器统一计算区域锚定后的实际位置（与 ZPL / 预览一致）
        var layout = _viewModel.BuildLayout();
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var elementModels = layout.Elements.ToList();

        for (var i = 0; i < _viewModel.Elements.Count; i++)
        {
            var element = _viewModel.Elements[i];
            var bounds = LabelLayoutResolver.ResolveBounds(elementModels[i], regions);
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
                    Text = element.SourceKey,
                    FontFamily = new FontFamily("Microsoft YaHei"),
                    FontSize = Math.Max(8, element.FontHeightMm * pps),
                    Foreground = Brushes.Black,
                    Background = Brushes.Transparent,
                    MinWidth = 24,
                    MinHeight = 16,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                Canvas.SetLeft(text, x);
                Canvas.SetTop(text, y);

                if (bounds.WidthMm > 0)
                {
                    // 块宽：画边框 + 内部对齐
                    var box = new Border
                    {
                        Width = Math.Max(1, bounds.WidthMm * pps),
                        Height = Math.Max(1, bounds.HeightMm * pps),
                        BorderBrush = element.BorderMm > 0 ? Brushes.Black : highlight,
                        BorderThickness = new Thickness(Math.Max(1, element.BorderMm * pps)),
                        Background = Brushes.Transparent,
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
                var box = new Border
                {
                    Width = width,
                    Height = height,
                    BorderBrush = element.BorderMm > 0 ? Brushes.Black : highlight,
                    BorderThickness = new Thickness(Math.Max(1, element.BorderMm * pps)),
                    Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    Child = new TextBlock
                    {
                        Text = $"{label}: {element.SourceKey}",
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
        var hit = FindElement(e.OriginalSource as DependencyObject);
        if (hit is not null)
        {
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

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragElement is null)
        {
            return;
        }

        var pos = e.GetPosition(EditorCanvas);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        _viewModel.MoveElement(_dragElement, dx, dy);
        _dragStart = pos;
        RedrawCanvas();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragElement = null;
        EditorCanvas.ReleaseMouseCapture();
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

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SaveAsync();
            var png = await _client.PreviewAsync(_viewModel.Name, new Dictionary<string, string>());
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            PreviewImage.Source = image;
            _viewModel.StatusText = "预览已刷新。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "预览失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddElement_Click(object sender, RoutedEventArgs e)
    {
        if (Enum.TryParse<EditorElementType>(ElementTypeBox.SelectedItem as string, out var type))
        {
            _viewModel.AddElement(type);
        }
    }

    private void AddRegion_Click(object sender, RoutedEventArgs e)
        => _viewModel.AddElement(EditorElementType.Region);

    private void RemoveElement_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedElement is { } element)
        {
            _viewModel.RemoveElement(element);
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