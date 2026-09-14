using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>向导壳：分步导航（欢迎 → 部署形态 → 打印机品牌 → 管理界面 → 确认预览）；分页内容见各 Page。</summary>
internal sealed class WizardForm : Form
{
    private readonly WizardSession _session = new();
    private readonly LabelFrameBootstrapperBa _ba;

    private readonly Label _stepLabel = new();
    private readonly Panel _contentPanel = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _cancelButton = new();

    private readonly List<Func<WizardSession, IWizardPage>> _pageFactories;
    private readonly List<IWizardPage> _pages = [];
    private int _currentIndex = -1;

    public WizardForm(LabelFrameBootstrapperBa ba)
    {
        _ba = ba;

        Text = "LabelFrame 安装引导";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(760, 560);

        _pageFactories =
        [
            session => new WelcomePage(session, RequestNext),
            session => new TopologyPage(session),
            session => new BrandPage(session),
            session => new ManagementUiPage(session),
            session => new ConfirmPage(session, _ba),
        ];

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
        _backButton.Click += (_, _) => Navigate(-1);

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

        Navigate(0);
    }

    /// <summary>欢迎页加载清单成功后自动进入下一页（避免连点两次）。</summary>
    private void RequestNext()
    {
        var page = _pages[_currentIndex];
        if (!page.CanProceed(out var reason))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                MessageBox.Show(this, reason, "无法继续", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        if (_currentIndex == _pageFactories.Count - 1)
        {
            // 确认页（最后一步）：仅关闭窗口——引擎侧 dry-run 结束，无后续动作
            Close();
            return;
        }

        Navigate(1);
    }

    private void Navigate(int delta)
    {
        var target = _currentIndex + delta;
        if (target < 0 || target >= _pageFactories.Count)
        {
            return;
        }

        SuspendLayout();
        try
        {
            while (_pages.Count <= target)
            {
                _pages.Add(_pageFactories[_pages.Count](_session));
            }

            if (_currentIndex >= 0 && _contentPanel.Controls.Count > 0)
            {
                _contentPanel.Controls.RemoveAt(0);
            }

            var page = _pages[target];
            _contentPanel.Controls.Add((Control)page);
            _currentIndex = target;

            _stepLabel.Text = $"步骤 {_currentIndex + 1} / {_pageFactories.Count}";
            _backButton.Enabled = _currentIndex > 0;
            _nextButton.Text = _currentIndex == _pageFactories.Count - 1 ? "关闭" : "下一步(&N)";

            page.OnEnter();
        }
        finally
        {
            ResumeLayout();
        }
    }
}
