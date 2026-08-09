using System.Windows;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio;

/// <summary>Excel 导入映射窗口：列 → 字段 Key 映射确认 + 批量打印。</summary>
public partial class ExcelMappingWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>是否已确认批量打印。</summary>
    public bool Printed { get; private set; }

    public ExcelMappingWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var total = await _viewModel.PrintExcelAsync();
            Printed = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "批量打印失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}