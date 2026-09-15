using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Upgrade;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>确认页：组件名称 / 版本 / <b>本机版本（§6.11 可升级清单：现版本 → 新版本 / 已是最新）</b> / 体积 / 来源 URL / 目标安装位置；升级或已最新横幅；「下一步 = 安装」进入进度页触发引擎 Plan + Apply（执行边界契约，决策 #124）。</summary>
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
        _listView.Columns.Add("组件", 200);
        _listView.Columns.Add("版本", 64);
        _listView.Columns.Add("本机版本", 110);
        _listView.Columns.Add("体积", 62);
        _listView.Columns.Add("来源 URL", 150);
        _listView.Columns.Add("安装位置", 190);

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
        var assessment = _session.Assessment!;
        var plannedEntries = assessment.Entries
            .Where(entry => plan.Components.Any(item => item.Component.Id == entry.ComponentId))
            .ToList();
        _ba.Log($"确认安装计划：预设 {_session.Preset}，品牌 [{string.Join(",", _session.SelectedBrands)}]，管理界面 {_session.IncludeWebUi}，组件 [{string.Join(",", plan.Components.Select(item => item.Component.Id))}]，{DescribeRuntimeProbes(plan)}");
        _ba.Log($"升级评估（§6.11）：{UpgradePresentation.Summarize(assessment.Entries)}");

        // 横幅：升级 / 已最新优先于既有「确认前只读」提示（决策 #126：用户最关心的状态放最上层）
        var upgradeBanner = UpgradePresentation.ConfirmBanner(plannedEntries);
        _banner.Text = upgradeBanner ?? ExecuteBoundaryNotice.Banner;
        _banner.BackColor = upgradeBanner is null
            ? Color.FromArgb(255, 244, 230)
            : Color.FromArgb(232, 240, 254);
        _banner.ForeColor = upgradeBanner is null
            ? Color.FromArgb(154, 84, 0)
            : Color.FromArgb(22, 84, 160);

        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var item in plan.Components)
        {
            var component = item.Component;
            var displayName = string.IsNullOrWhiteSpace(component.Notes) ? component.Id : $"{component.Id}（{component.Notes}）";
            if (component.Type == "runtime")
            {
                // 运行时组件标注探测结论（AC-02：缺失才装、已装跳过——Burn DetectCondition 消费）
                var runtimeInstalled = component.Id switch
                {
                    "runtime-desktop" => _ba.RuntimeStatus.DesktopRuntimeInstalled,
                    "runtime-aspnetcore" => _ba.RuntimeStatus.AspNetCoreRuntimeInstalled,
                    "runtime-webview2" => _ba.RuntimeStatus.WebView2Installed,
                    _ => false,
                };
                displayName += runtimeInstalled ? "【已装则跳过】" : "【缺失将安装】";
            }

            var row = new ListViewItem(displayName)
            {
                ToolTipText = item.InstallTarget,
            };
            row.SubItems.Add(component.Version);
            row.SubItems.Add(DescribeLocalVersion(plannedEntries, component.Id));
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

    /// <summary>运行时探测腿文案（观察项①修正，决策 #129 ③）：按当轮计划集合过滤——计划不含的运行时不输出「未装（将安装）」，
    /// 避免与引擎 InstallCondition 过滤后的实际执行计划（execute: None）矛盾。</summary>
    private string DescribeRuntimeProbes(TopologyPlan plan)
    {
        var notes = new List<string>();
        if (plan.Components.Any(item => item.Component.Id == "runtime-desktop"))
        {
            notes.Add($".NET Desktop Runtime {(_ba.RuntimeStatus.DesktopRuntimeInstalled ? $"已装 {_ba.RuntimeStatus.DesktopRuntimeVersion}（跳过）" : "未装（将安装）")}");
        }

        if (plan.Components.Any(item => item.Component.Id == "runtime-aspnetcore"))
        {
            notes.Add($"ASP.NET Core Runtime {(_ba.RuntimeStatus.AspNetCoreRuntimeInstalled ? $"已装 {_ba.RuntimeStatus.AspNetCoreRuntimeVersion}（跳过）" : "未装（将安装）")}");
        }

        if (plan.Components.Any(item => item.Component.Id == "runtime-webview2"))
        {
            notes.Add($"WebView2 {(_ba.RuntimeStatus.WebView2Installed ? "已装（跳过）" : "未装（将安装）")}");
        }

        return string.Join("，", notes);
    }

    /// <summary>本机版本列（§6.11 可升级清单）：升级 = 现版本 → 新版本；新装 = —（新装）；已最新 = 版本号（已是最新）。</summary>
    private static string DescribeLocalVersion(IReadOnlyList<ComponentUpgradeEntry> entries, string componentId)
    {
        var entry = entries.FirstOrDefault(candidate => candidate.ComponentId == componentId);
        if (entry is null)
        {
            return "—";
        }

        return entry.Action switch
        {
            ComponentUpgradeAction.Upgrade => $"{entry.InstalledVersion} → {entry.TargetVersion}",
            ComponentUpgradeAction.Install => "—（新装）",
            ComponentUpgradeAction.UpToDate => $"{entry.InstalledVersion ?? "已装"}（已是最新）",
            ComponentUpgradeAction.LocalNewer => $"{entry.InstalledVersion}（本机更新）",
            _ => "—（覆盖更新）", // webui 覆盖重写 / evergreen 已装等不比较形态
        };
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
