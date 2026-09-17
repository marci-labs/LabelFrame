using LabelFrame.Bootstrapper.Upgrade;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>欢迎页：简介 + dry-run 预期 + 离线布局目录入口指引（迭代 70 / 决策 #132：--layout 生成 + 拷目录离线首装）+ 清单来源（本地路径或 URL；布局目录隐式检测时默认本地）+ 本机升级摘要（§6.11：可升级清单 / 已是最新 / 清单新鲜度）。</summary>
internal sealed class WelcomePage : UserControl, IWizardPage
{
    private const string ReleasesPageUrl = "https://github.com/marci-labs/LabelFrame/releases";

    private readonly WizardSession _session;
    private readonly Action _proceed;
    private readonly TextBox _sourceTextBox = new();
    private readonly Button _browseButton = new();
    private readonly Button _loadButton = new();
    private readonly Label _statusLabel = new();
    private readonly Label _upgradeLabel = new();
    private readonly Label _freshnessLabel = new();
    private readonly Label _layoutHintLabel = new();

    public WelcomePage(WizardSession session, Action proceed)
    {
        _session = session;
        _proceed = proceed;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "欢迎使用 LabelFrame 安装引导",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        var intro = new Label
        {
            Text = "本向导通过少量问题确定你的部署形态，并展示将要下载的组件与安装位置。",
            AutoSize = true,
            Location = new Point(8, 40),
        };

        var dryRun = new Label
        {
            Text = ExecuteBoundaryNotice.WelcomeHint,
            AutoSize = true,
            ForeColor = Color.FromArgb(154, 84, 0),
            Location = new Point(8, 64),
        };

        // 本机升级摘要（§6.11，决策 #126）：清单加载后呈现——可升级清单（组件、现版本 → 新版本）/ 已是最新 / 全新安装
        _upgradeLabel.AutoSize = false;
        _upgradeLabel.Width = 680;
        _upgradeLabel.Height = 34;
        _upgradeLabel.Location = new Point(8, 88);
        _upgradeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _upgradeLabel.Padding = new Padding(6, 0, 0, 0);
        _upgradeLabel.BackColor = Color.FromArgb(232, 240, 254);
        _upgradeLabel.ForeColor = Color.FromArgb(22, 84, 160);

        // 清单新鲜度提示（latest.json 消费，§6.11）：清单版本落后于通道最新时提示（null 时隐藏）
        _freshnessLabel.AutoSize = false;
        _freshnessLabel.Width = 680;
        _freshnessLabel.Height = 26;
        _freshnessLabel.Location = new Point(8, 124);
        _freshnessLabel.TextAlign = ContentAlignment.MiddleLeft;
        _freshnessLabel.Padding = new Padding(6, 0, 0, 0);
        _freshnessLabel.BackColor = Color.FromArgb(255, 244, 230);
        _freshnessLabel.ForeColor = Color.FromArgb(154, 84, 0);
        _freshnessLabel.Visible = false;

        // 布局目录提示（迭代 70 / 决策 #132）：默认清单来源为本地布局清单时展示（EXE 同目录隐式检测命中）
        _layoutHintLabel.AutoSize = false;
        _layoutHintLabel.Width = 656;
        _layoutHintLabel.Height = 32;
        _layoutHintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _layoutHintLabel.Padding = new Padding(6, 0, 0, 0);
        _layoutHintLabel.BackColor = Color.FromArgb(232, 248, 236);
        _layoutHintLabel.ForeColor = Color.FromArgb(38, 106, 66);
        _layoutHintLabel.Visible = false;

        // 离线布局目录入口指引（迭代 70 / #89，决策 #132——#53 决议 1 方案 A 指引的落地形态：布局目录已产品化）
        var offlineBox = new GroupBox
        {
            Text = "无法联网？（离线安装）",
            AutoSize = true,
            Location = new Point(8, 158),
            Width = 680,
        };
        var offlineText = new Label
        {
            Text = "目标机器不能访问互联网时，先在有网络的电脑上生成「离线布局目录」（运行安装引导并加参数"
                + " --layout <目录>，或使用仓库脚本 scripts/make-offline-layout.ps1），把整个目录拷贝到目标机器，"
                + "直接运行目录中的安装引导即可离线安装——清单与全部组件取自本地目录，全程无需联网。",
            AutoSize = true,
            MaximumSize = new Size(650, 0),
            Location = new Point(12, 22),
        };
        var offlineLink = new LinkLabel
        {
            Text = ReleasesPageUrl,
            AutoSize = true,
            Location = new Point(12, 78),
        };
        offlineLink.LinkClicked += (_, _) => OpenReleasesPage();
        offlineBox.Controls.Add(offlineText);
        offlineBox.Controls.Add(offlineLink);

        // 清单来源
        var sourceBox = new GroupBox
        {
            Text = "安装清单来源",
            Location = new Point(8, 262),
            Size = new Size(680, 150),
        };
        var sourceLabel = new Label
        {
            Text = "本地文件路径或 URL（默认官方稳定通道，需联网）：",
            AutoSize = true,
            Location = new Point(12, 26),
        };
        _sourceTextBox.Location = new Point(12, 50);
        _sourceTextBox.Width = 540;
        // 会话来源（迭代 70 / 决策 #132）：BA 启动检测到引导 EXE 同目录布局清单时已改写为本地路径——离线首装零网络起步
        _sourceTextBox.Text = _session.ManifestSource;
        if (!string.Equals(_sourceTextBox.Text, WizardSession.StableChannelManifestUrl, StringComparison.Ordinal))
        {
            _layoutHintLabel.Text = "已检测到本目录为离线布局目录：清单默认取自本地，组件将优先从本目录获取（无需联网）。";
            _layoutHintLabel.Visible = true;
        }

        _browseButton.Text = "浏览…";
        _browseButton.AutoSize = true;
        _browseButton.Location = new Point(560, 48);
        _browseButton.Click += (_, _) => BrowseLocalManifest();

        _loadButton.Text = "加载清单";
        _loadButton.AutoSize = true;
        _loadButton.Location = new Point(12, 84);
        _loadButton.Click += async (_, _) => await LoadManifestAsync();

        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(110, 88);
        _statusLabel.MaximumSize = new Size(540, 0);

        _layoutHintLabel.Location = new Point(12, 112);
        sourceBox.Controls.Add(sourceLabel);
        sourceBox.Controls.Add(_sourceTextBox);
        sourceBox.Controls.Add(_browseButton);
        sourceBox.Controls.Add(_loadButton);
        sourceBox.Controls.Add(_statusLabel);
        sourceBox.Controls.Add(_layoutHintLabel);

        Controls.Add(title);
        Controls.Add(intro);
        Controls.Add(dryRun);
        Controls.Add(_upgradeLabel);
        Controls.Add(_freshnessLabel);
        Controls.Add(offlineBox);
        Controls.Add(sourceBox);
    }

    public void OnEnter()
    {
        // 无需刷新（状态保留在控件与会话中）
    }

    public bool CanProceed(out string? reason)
    {
        if (_session.Manifest is null)
        {
            reason = "请先加载安装清单（本地文件或 URL）。";
            return false;
        }

        reason = null;
        return true;
    }

    private void BrowseLocalManifest()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择安装清单（install-manifest.json）",
            Filter = "安装清单 (install-manifest.json)|install-manifest.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sourceTextBox.Text = dialog.FileName;
            _ = LoadManifestAsync();
        }
    }

    private async Task LoadManifestAsync()
    {
        _session.ManifestSource = _sourceTextBox.Text.Trim();
        _loadButton.Enabled = false;
        _browseButton.Enabled = false;
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = "正在加载清单…";
        try
        {
            await _session.LoadManifestAsync();
            _statusLabel.Text = $"已加载：LabelFrame {_session.Manifest!.LabelframeVersion}（{_session.Manifest.Components.Count} 个组件）。";

            // 升级摘要 + 清单新鲜度（§6.11）：只读探测结果呈现，双通道留痕（UI + Burn 日志）
            var assessment = _session.Assessment!;
            _upgradeLabel.Text = UpgradePresentation.Summarize(assessment.Entries);
            var freshness = UpgradePresentation.DescribeFreshness(_session.Manifest, _session.Latest);
            _freshnessLabel.Visible = freshness is not null;
            _freshnessLabel.Text = freshness ?? string.Empty;

            // 加载成功即进入下一步（清单是后续所有问卷步骤的前提）
            _proceed();
        }
        catch (Exception ex)
        {
            // UI 边界兜底（文件不存在 / URL 不可达 / 清单非法等）：中文可行动提示，不打断向导
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            _loadButton.Enabled = true;
            _browseButton.Enabled = true;
        }
    }

    private void OpenReleasesPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReleasesPageUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (SystemException)
        {
            // 打开浏览器失败（无默认浏览器等）：不打断向导，链接文本本身可供手动复制
            MessageBox.Show(this, $"请手动打开发布页：{ReleasesPageUrl}", "无法打开链接", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
