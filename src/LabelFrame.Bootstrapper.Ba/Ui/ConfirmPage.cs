using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>确认页：组件名称 / 版本 / 体积 / 来源 URL / 目标安装位置（运行时组件标注「已装则跳过」）；「下一步 = 安装」进入进度页触发引擎 Plan + Apply（执行边界契约，决策 #124）。</summary>
internal sealed class ConfirmPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly LabelFrameBootstrapperBa _ba;
    private readonly Label _banner = new();
    private readonly Label _summaryLabel = new();
    private readonly ListView _listView = new();
    private readonly Label _guidanceLabel = new();

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

        _banner.Text = ExecuteBoundaryNotice.Banner;
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
        _listView.Columns.Add("组件", 220);
        _listView.Columns.Add("版本", 70);
        _listView.Columns.Add("体积", 70);
        _listView.Columns.Add("来源 URL", 180);
        _listView.Columns.Add("安装位置", 200);

        var installHint = new Label
        {
            Text = "点击「下一步」开始安装：将按上表下载组件并安装（需要管理员权限，系统会弹出 UAC 确认）。",
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Location = new Point(8, 432),
            ForeColor = SystemColors.GrayText,
        };

        _guidanceLabel.AutoSize = true;
        _guidanceLabel.Location = new Point(8, 456);
        _guidanceLabel.MaximumSize = new Size(680, 0);
        _guidanceLabel.ForeColor = SystemColors.GrayText;

        Controls.Add(title);
        Controls.Add(_banner);
        Controls.Add(_summaryLabel);
        Controls.Add(_listView);
        Controls.Add(installHint);
        Controls.Add(_guidanceLabel);
    }

    public void OnEnter()
    {
        var plan = _session.BuildPlan();
        _ba.Log($"确认安装计划：预设 {_session.Preset}，品牌 [{string.Join(",", _session.SelectedBrands)}]，管理界面 {_session.IncludeWebUi}，组件 [{string.Join(",", plan.Components.Select(item => item.Component.Id))}]，.NET Desktop Runtime {(_ba.RuntimeStatus.DesktopRuntimeInstalled ? $"已装 {_ba.RuntimeStatus.DesktopRuntimeVersion}（跳过）" : "未装（将安装）")}，WebView2 {(_ba.RuntimeStatus.WebView2Installed ? "已装（跳过）" : "未装（将安装）")}");

        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var item in plan.Components)
        {
            var component = item.Component;
            var displayName = string.IsNullOrWhiteSpace(component.Notes) ? component.Id : $"{component.Id}（{component.Notes}）";
            if (component.Type == "runtime")
            {
                // 运行时组件标注探测结论（AC-02：缺失才装、已装跳过——Burn DetectCondition 消费）
                displayName += _ba.RuntimeStatus.DesktopRuntimeInstalled && component.Id == "runtime-desktop"
                    || _ba.RuntimeStatus.WebView2Installed && component.Id == "runtime-webview2"
                    ? "【已装则跳过】"
                    : "【缺失将安装】";
            }

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
        if (_session.BuildPlan().Components.Count == 0)
        {
            reason = "本形态无下载组件（如 Docker / Linux 部署指引形态），无需在本机执行安装。你可以直接关闭向导，按页面指引部署。";
            return false;
        }

        reason = null;
        return true;
    }
}
