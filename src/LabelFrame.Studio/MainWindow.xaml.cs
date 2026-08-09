using System.Windows;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Studio.Services;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio;

/// <summary>主窗口。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.WinHostExe = string.Empty;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ConnectAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Disconnect();
    }

    private void StartWinHost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.StartWinHost();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopWinHost_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopWinHost();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshTemplatesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "刷新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择模板包",
            Filter = "LabelFrame 模板包 (*.lfpkg)|*.lfpkg|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.ImportAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTemplate is null)
        {
            MessageBox.Show(this, "请先选择模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出模板包",
            FileName = $"{_viewModel.SelectedTemplate.Name}.lfpkg",
            Filter = "LabelFrame 模板包 (*.lfpkg)|*.lfpkg",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var bytes = await _viewModel.ExportAsync(_viewModel.SelectedTemplate.Name);
            await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
            MessageBox.Show(this, "导出成功。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTemplate is null)
        {
            MessageBox.Show(this, "请先选择模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"确定删除模板「{_viewModel.SelectedTemplate.Name}」？",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _viewModel.DeleteAsync(_viewModel.SelectedTemplate.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Client is null)
        {
            MessageBox.Show(this, "请先连接 WinHost。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new NewTemplateWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var dto = new TemplateSaveDto(
            dialog.TemplateName,
            dialog.Group,
            new LabelContract
            {
                Name = dialog.TemplateName,
                Version = "1.0",
                Fields = [],
            },
            new LabelLayout
            {
                Name = $"{dialog.TemplateName}-layout",
                ContractName = dialog.TemplateName,
                ContractVersion = "1.0",
                WidthMm = dialog.WidthMm,
                HeightMm = dialog.HeightMm,
                Elements = [],
            });

        try
        {
            var editor = new EditorWindow(_viewModel.Client, dto) { Owner = this };
            editor.ShowDialog();
            _ = _viewModel.RefreshTemplatesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开编辑器失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTemplate is null)
        {
            MessageBox.Show(this, "请先选择模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var detail = await _viewModel.GetTemplateAsync(_viewModel.SelectedTemplate.Name);
            if (detail?.Contract is null || detail.Layout is null)
            {
                MessageBox.Show(this, "模板详情读取失败。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editor = new EditorWindow(_viewModel.Client!, detail) { Owner = this };
            editor.ShowDialog();
            await _viewModel.RefreshTemplatesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开编辑器失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.PreviewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "预览失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, ex.Message, "打印测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Status_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "状态查询失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}