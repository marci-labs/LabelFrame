using System.Windows;

namespace LabelFrame.Studio;

/// <summary>新建模板输入对话框。</summary>
public partial class NewTemplateWindow : Window
{
    public NewTemplateWindow()
    {
        InitializeComponent();
        NameBox.Focus();
    }

    public string TemplateName => NameBox.Text.Trim();

    public string Group => string.IsNullOrWhiteSpace(GroupBox.Text) ? "默认" : GroupBox.Text.Trim();

    public double WidthMm => double.TryParse(WidthBox.Text, out var v) && v > 0 ? v : 100;

    public double HeightMm => double.TryParse(HeightBox.Text, out var v) && v > 0 ? v : 60;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            MessageBox.Show(this, "模板名不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}