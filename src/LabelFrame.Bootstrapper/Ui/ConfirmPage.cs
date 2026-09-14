using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ui;

/// <summary>确认页（dry-run 预览）：组件名称 / 版本 / 体积 / 来源 URL / 目标安装位置；server-docker 展示 compose 指引；明示「仅预览，尚未下载 / 安装」。</summary>
internal sealed class ConfirmPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly Label _banner = new();
    private readonly Label _summaryLabel = new();
    private readonly ListView _listView = new();
    private readonly Label _guidanceLabel = new();

    public ConfirmPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "确认：以下内容将被下载并安装",
            Font = new Font(Control.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _banner.Text = DryRunNotice.Banner;
        _banner.AutoSize = false;
        _banner.Height = 32;
        _banner.Width = 680;
        _banner.Location = new Point(8, 36);
        _banner.BackColor = Color.FromArgb(255, 244, 230);
        _banner.ForeColor = Color.FromArgb(154, 84, 0);
        _banner.TextAlign = ContentAlignment.MiddleLeft;
        _banner.Padding = new Padding(8, 0, 0, 0);

        _summaryLabel.AutoSize = true;
        _summaryLabel.Location = new Point(8, 74);

        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.HideSelection = true;
        _listView.Location = new Point(8, 100);
        _listView.Size = new Size(680, 320);
        _listView.Columns.Add("组件", 210);
        _listView.Columns.Add("版本", 70);
        _listView.Columns.Add("体积", 70);
        _listView.Columns.Add("来源 URL", 180);
        _listView.Columns.Add("安装位置", 200);

        _guidanceLabel.AutoSize = true;
        _guidanceLabel.Location = new Point(8, 428);
        _guidanceLabel.MaximumSize = new Size(680, 0);
        _guidanceLabel.ForeColor = SystemColors.GrayText;

        Controls.Add(title);
        Controls.Add(_banner);
        Controls.Add(_summaryLabel);
        Controls.Add(_listView);
        Controls.Add(_guidanceLabel);
    }

    public void OnEnter()
    {
        var plan = _session.BuildPlan();
        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var item in plan.Components)
        {
            var component = item.Component;
            var displayName = string.IsNullOrWhiteSpace(component.Notes) ? component.Id : $"{component.Id}（{component.Notes}）";
            var row = new ListViewItem(displayName)
            {
                UseItemStyleForSubItems = false,
                ToolTipText = item.InstallTarget,
            };
            row.SubItems.Add(component.Version);
            row.SubItems.Add(SizeFormat.Format(component.SizeBytes));
            row.SubItems.Add(component.Urls[0]);
            row.SubItems.Add(item.InstallTarget);
            _listView.Items.Add(row);
        }

        _listView.EndUpdate();

        _summaryLabel.Text = plan.Components.Count == 0
            ? $"清单版本 {_session.Manifest!.LabelframeVersion}：本形态无下载组件。"
            : $"清单版本 {_session.Manifest!.LabelframeVersion}，共 {plan.Components.Count} 个组件，合计约 {SizeFormat.Format(plan.TotalSizeBytes)}。";

        _guidanceLabel.Visible = plan.DockerComposeGuidance is not null;
        _guidanceLabel.Text = plan.DockerComposeGuidance ?? string.Empty;
    }

    public bool CanProceed(out string? reason)
    {
        reason = null;
        return true;
    }
}
