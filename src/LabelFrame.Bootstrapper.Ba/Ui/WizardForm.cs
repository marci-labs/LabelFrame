using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>向导壳：分步导航（欢迎 → 部署形态 → 打印机品牌 → 管理界面 → 确认预览）；分页内容见各 Page。</summary>
/// <remarks>
/// 导航索引 / 惰性装配 / 越界防御由 <see cref="WizardNavigator{TPage}"/> 承担（迭代 60 返修，可单测）；
/// 本类只做 WinForms 呈现与按钮接线——<see cref="NavigateTo"/> 为<b>绝对</b>页索引（原增量 Navigate(delta)
/// 的 Navigate(0) 会被越界守卫静默吞掉，即验收回流缺陷，见 Issue #53）。
/// </remarks>
internal sealed class WizardForm : Form
{
    private readonly WizardSession _session = new();
    private readonly LabelFrameBootstrapperBa _ba;

    private readonly Label _stepLabel = new();
    private readonly Panel _contentPanel = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _cancelButton = new();

    private readonly WizardNavigator<IWizardPage> _navigator;

    public WizardForm(LabelFrameBootstrapperBa ba)
    {
        _ba = ba;

        Text = "LabelFrame 安装引导";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(760, 560);

        _navigator = new WizardNavigator<IWizardPage>(
        [
            () => new WelcomePage(_session, RequestNext),
            () => new TopologyPage(_session),
            () => new BrandPage(_session),
            () => new ManagementUiPage(_session),
            () => new ConfirmPage(_session, _ba),
        ]);

        // 顶部步骤指示
        _stepLabel.Dock = DockStyle.Top;
        _stepLabel.Height = 36;
        _stepLabel.TextAlign = ContentAlignment.MiddleLeft;
        _stepLabel.Padding = new Padding(16, 0, 0, 0);
        _stepLabel.ForeColor = Color.FromArgb(89, 89, 89);

        // 分页内容区
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Padding = new Padding(16);

        // 底部导航
        var navPanel = new Panel { Dock = DockStyle.Bottom, Height = 52 };
        _backButton.Text = "上一步(&B)";
        _backButton.AutoSize = true;
        _backButton.Location = new Point(16, 10);
        _backButton.Click += (_, _) => NavigateTo(_navigator.CurrentIndex - 1);

        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancelButton.Location = new Point(ClientSize.Width - 100, 10);
        _cancelButton.Click += (_, _) => Close();
        navPanel.Resize += (_, _) => _cancelButton.Location = new Point(navPanel.Width - _cancelButton.Width - 24, 10);

        _nextButton.Text = "下一步(&N)";
        _nextButton.AutoSize = true;
        _nextButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _nextButton.Location = new Point(ClientSize.Width - 210, 10);
        _nextButton.Click += (_, _) => RequestNext();
        navPanel.Resize += (_, _) => _nextButton.Location = new Point(_cancelButton.Left - _nextButton.Width - 12, 10);

        navPanel.Controls.Add(_backButton);
        navPanel.Controls.Add(_nextButton);
        navPanel.Controls.Add(_cancelButton);

        Controls.Add(_contentPanel);
        Controls.Add(_stepLabel);
        Controls.Add(navPanel);

        NavigateTo(0);

        // 装配自检：首页未就位即抛出（由 BA Run 兜底为显式失败 + 诊断），绝不带着 -1 索引进入消息循环
        if (!_navigator.IsStarted)
        {
            throw new InvalidOperationException($"向导首页装配未完成（当前索引 {_navigator.CurrentIndex}），禁止启动。");
        }
    }

    /// <summary>欢迎页加载清单成功后自动进入下一页（避免连点两次）。</summary>
    private void RequestNext()
    {
        if (!_navigator.IsStarted)
        {
            // 防御（正常流程不可达）：未装配状态禁止取当前页——显式抛出可诊断异常，而非索引越界
            throw new InvalidOperationException($"向导尚未装配页面（当前索引 {_navigator.CurrentIndex}），无法执行「下一步」。");
        }

        var page = _navigator.CurrentPage;
        if (!page.CanProceed(out var reason))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                MessageBox.Show(this, reason, "无法继续", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        if (_navigator.IsLastPage)
        {
            // 确认页（最后一步）：仅关闭窗口——引擎侧 dry-run 结束，无后续动作
            Close();
            return;
        }

        NavigateTo(_navigator.CurrentIndex + 1);
    }

    /// <summary>导航到<b>绝对</b>页索引（0 = 首页）；越界 / 非法索引静默拒绝（保持当前页，状态机负责防御）。</summary>
    private void NavigateTo(int pageIndex)
    {
        if (!_navigator.TryNavigateTo(pageIndex))
        {
            return;
        }

        SuspendLayout();
        try
        {
            if (_contentPanel.Controls.Count > 0)
            {
                _contentPanel.Controls.RemoveAt(0);
            }

            var page = _navigator.CurrentPage;
            _contentPanel.Controls.Add((Control)page);

            _stepLabel.Text = $"步骤 {_navigator.CurrentIndex + 1} / {_navigator.PageCount}";
            _backButton.Enabled = _navigator.CurrentIndex > 0;
            _nextButton.Text = _navigator.IsLastPage ? "关闭" : "下一步(&N)";

            page.OnEnter();
        }
        finally
        {
            ResumeLayout();
        }
    }
}
