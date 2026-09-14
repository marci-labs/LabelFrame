using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;
using WixToolset.BootstrapperApplicationApi;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>确认页（dry-run 预览）：组件名称 / 版本 / 体积 / 来源 URL / 目标安装位置；「生成安装计划」= Burn Plan 阶段只计划不执行，明示「仅预览」。</summary>
internal sealed class ConfirmPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly LabelFrameBootstrapperBa _ba;
    private readonly Label _banner = new();
    private readonly Label _summaryLabel = new();
    private readonly ListView _listView = new();
    private readonly Label _guidanceLabel = new();
    private readonly Button _planButton = new();

    public ConfirmPage(WizardSession session, LabelFrameBootstrapperBa ba)
    {
        _session = session;
        _ba = ba;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "确认：以上问卷结果对应的组件集合",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
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
        _listView.Size = new Size(680, 280);
        _listView.Columns.Add("组件", 210);
        _listView.Columns.Add("版本", 70);
        _listView.Columns.Add("体积", 70);
        _listView.Columns.Add("来源 URL", 180);
        _listView.Columns.Add("安装位置", 200);

        _planButton.Text = "生成安装计划（仅预览，不执行）";
        _planButton.AutoSize = true;
        _planButton.Location = new Point(8, 392);
        _planButton.Click += async (_, _) => await PreviewPlanAsync();

        _guidanceLabel.AutoSize = true;
        _guidanceLabel.Location = new Point(8, 428);
        _guidanceLabel.MaximumSize = new Size(680, 0);
        _guidanceLabel.ForeColor = SystemColors.GrayText;

        Controls.Add(title);
        Controls.Add(_banner);
        Controls.Add(_summaryLabel);
        Controls.Add(_listView);
        Controls.Add(_planButton);
        Controls.Add(_guidanceLabel);
    }

    public void OnEnter()
    {
        var plan = _session.BuildPlan();
        _ba.Log($"dry-run 预览：预设 {_session.Preset}，品牌 [{string.Join(",", _session.SelectedBrands)}]，管理界面 {_session.IncludeWebUi}，组件 [{string.Join(",", plan.Components.Select(item => item.Component.Id))}]");

        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var item in plan.Components)
        {
            var component = item.Component;
            var displayName = string.IsNullOrWhiteSpace(component.Notes) ? component.Id : $"{component.Id}（{component.Notes}）";
            var row = new ListViewItem(displayName)
            {
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

    /// <summary>dry-run：问卷答案 → Burn 变量 → Engine.Plan（只计划不执行，绝不 Apply）。</summary>
    private async Task PreviewPlanAsync()
    {
        _planButton.Enabled = false;
        try
        {
            var preview = await _ba.PreviewPlanAsync(_session).ConfigureAwait(true);

            var lines = preview.PlannedPackages
                .Select(package => $"  {package.PackageId}：{Describe(package.State)}")
                .ToArray();
            var message = "安装引擎计划结果（仅预览，尚未下载、尚未安装）：\n\n" + string.Join("\n", lines)
                + "\n\n实际下载与安装将在后续版本提供。";
            MessageBox.Show(this, message, "LabelFrame 安装引导（dry-run）", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "生成安装计划失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _planButton.Enabled = true;
        }
    }

    private static string Describe(RequestState state) => state switch
    {
        RequestState.Present => "将安装",
        RequestState.Repair => "将修复",
        RequestState.Cache => "仅缓存",
        RequestState.None => "跳过",
        RequestState.Absent => "将卸载",
        _ => state.ToString(),
    };
}
