using System.Windows;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Studio.Services;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio;

/// <summary>主窗口：作业工作台（模板列表 / 预览 / 数据表单 / 打印 / 状态日志）。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.Logs.CollectionChanged += (_, _) => ScrollLogsToEnd();
    }

    private void ScrollLogsToEnd()
    {
        if (LogList.Items.Count == 0)
        {
            return;
        }

        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
        => _viewModel.ClearLogs();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ConnectAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Log($"连接失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
        => _viewModel.Disconnect();

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
        => _viewModel.StopWinHost();

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
            var name = await _viewModel.ImportTemplateAsync(dialog.FileName);
            _viewModel.Log($"已导入模板包：{name}");
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
            _viewModel.Log($"已导出模板包：{dialog.FileName}");
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
            _viewModel.Log($"已删除模板：{_viewModel.SelectedTemplate.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
        => _viewModel.PreviewAsync();

    private async void PrintTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.PrintTestAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Log($"打印失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "打印测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Status_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshStatusAsync();
            _viewModel.Log(_viewModel.TransportText ?? "打印机状态未知。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "状态查询失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PrinterTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _viewModel.Client!.TestPrinterAsync();
            _viewModel.Log($"打印机测试页已发送（{result.Bytes} 字节）。");
        }
        catch (Exception ex)
        {
            _viewModel.Log($"测试页发送失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "测试页失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportData_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTemplate is null)
        {
            MessageBox.Show(this, "请先选择模板，再导入 Excel 数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Excel 数据文件",
            Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.ImportExcelAsync(dialog.FileName);
            var mappingWindow = new ExcelMappingWindow(_viewModel) { Owner = this };
            mappingWindow.ShowDialog();
            if (mappingWindow.Printed)
            {
                _viewModel.PreviewAsync();
            }
        }
        catch (Exception ex)
        {
            _viewModel.Log($"Excel 导入失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
        => Close();

    private void TemplateList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Edit_Click(sender, new RoutedEventArgs());

    private void NewTemplate_Click(object sender, RoutedEventArgs e)
        => OpenDesigner(newMode: true);

    private void Edit_Click(object sender, RoutedEventArgs e)
        => OpenDesigner(newMode: false);

    private async void OpenDesigner(bool newMode)
    {
        if (_viewModel.Client is null)
        {
            MessageBox.Show(this, "请先连接 WinHost。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TemplateSaveDto? template = null;
        if (newMode)
        {
            var dialog = new NewTemplateWindow { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            template = new TemplateSaveDto(
                dialog.TemplateName,
                dialog.Group,
                new LabelContract { Name = dialog.TemplateName, Version = "1.0", Fields = [] },
                new LabelLayout
                {
                    Name = $"{dialog.TemplateName}-layout",
                    ContractName = dialog.TemplateName,
                    ContractVersion = "1.0",
                    WidthMm = dialog.WidthMm,
                    HeightMm = dialog.HeightMm,
                    Elements = [],
                });
        }
        else
        {
            if (_viewModel.SelectedTemplate is null)
            {
                MessageBox.Show(this, "请先选择模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            template = await _viewModel.GetTemplateAsync(_viewModel.SelectedTemplate.Name);
            if (template?.Contract is null || template.Layout is null)
            {
                MessageBox.Show(this, "模板详情读取失败。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            var designer = new DesignerWindow(_viewModel.Client, template) { Owner = this };
            designer.ShowDialog();
            await _viewModel.RefreshTemplatesAsync();
            _viewModel.PreviewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开设计器失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}