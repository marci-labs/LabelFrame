using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LabelFrame.Studio.Services;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio;

/// <summary>版式编辑窗口（V2）：画布拖拽 / 属性面板 / 字段编辑 / 保存 / 预览。</summary>
public partial class EditorWindow : Window
{
    private readonly StudioClient _client;
    private readonly LayoutEditorViewModel _viewModel;
    private LayoutElementViewModel? _dragElement;
    private Point _dragStart;

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

        foreach (var element in _viewModel.Elements)
        {
            var ui = CreateElementUi(element, pps, ReferenceEquals(element, selected));
            if (ui is null)
            {
                continue;
            }

            ui.Tag = element;
            EditorCanvas.Children.Add(ui);
        }
    }

    private static FrameworkElement? CreateElementUi(LayoutElementViewModel element, double pps, bool isSelected)
    {
        var highlight = isSelected ? Brushes.DodgerBlue : Brushes.Silver;

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
                Canvas.SetLeft(text, element.XMm * pps);
                Canvas.SetTop(text, element.YMm * pps);
                if (isSelected)
                {
                    text.Foreground = Brushes.DodgerBlue;
                    text.FontWeight = FontWeights.Bold;
                }

                return text;
            }
            case EditorElementType.Barcode:
            {
                var width = Math.Max(60, element.HeightMm * 2.5 * pps);
                var height = Math.Max(20, element.HeightMm * pps);
                var box = new Border
                {
                    Width = width,
                    Height = height,
                    BorderBrush = highlight,
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    Child = new TextBlock
                    {
                        Text = $"条码: {element.SourceKey}",
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                };
                Canvas.SetLeft(box, element.XMm * pps);
                Canvas.SetTop(box, element.YMm * pps);
                return box;
            }
            case EditorElementType.QrCode:
            case EditorElementType.Image:
            {
                var width = Math.Max(20, element.WidthMm * pps);
                var height = Math.Max(20, element.HeightMm * pps);
                var label = element.Type == EditorElementType.QrCode ? "二维码" : "图片";
                var box = new Border
                {
                    Width = width,
                    Height = height,
                    BorderBrush = highlight,
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    Child = new TextBlock
                    {
                        Text = $"{label}: {element.SourceKey}",
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                    },
                };
                Canvas.SetLeft(box, element.XMm * pps);
                Canvas.SetTop(box, element.YMm * pps);
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
}