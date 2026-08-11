using System.Diagnostics;

namespace LabelFrame.WinHost;

/// <summary>
/// 安装完成提示（迭代 20 优化）：MSI 原生完成弹窗会被 Windows 焦点策略挡到后台 / 只在任务栏闪烁，
/// 改为由 WinHost 以 `--install-finished` 模式显示 TopMost 弹窗（默认置前）。
/// 选择「立即打开」则以普通模式重启宿主（Kestrel + 托盘 + 打开浏览器界面），否则直接退出。
/// </summary>
internal static class InstallFinishedPrompt
{
    /// <summary>弹窗专用参数：由 Client MSI 的 LaunchFinishPrompt 自定义动作传入。</summary>
    public const string Flag = "--install-finished";

    public static int RunAndMaybeLaunch()
    {
        using var form = new FinishForm();
        // ShowDialog（模态）：按钮 DialogResult / Close 均能可靠结束对话框并返回；
        // Application.Run 在此场景下曾出现窗体已关闭但消息循环不退出、进程挂起的问题（0.16.0 实测）。
        var thread = new Thread(() => form.ShowDialog());
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (form.ShouldLaunch)
        {
            // 以普通模式重启宿主（无 --install-finished），启动打印服务并打开浏览器界面
            try
            {
                // UseShellExecute=false（CreateProcess）：不依赖 ShellExecuteEx，启动宿主更确定
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位宿主程序。"),
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                });
            }
            catch
            {
                // 启动失败不影响安装结果（用户可稍后从开始菜单启动）
            }
        }

        return 0;
    }

    /// <summary>TopMost 完成弹窗。</summary>
    private sealed class FinishForm : Form
    {
        private readonly CheckBox _openNow;

        /// <summary>用户是否选择「立即打开」。默认勾选，与旧安装完成弹窗一致。</summary>
        public bool ShouldLaunch { get; private set; }

        public FinishForm()
        {
            Text = "LabelFrame 客户端安装完成";
            ClientSize = new Size(420, 170);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;

            var title = new Label
            {
                Text = "LabelFrame Client 安装完成。",
                AutoSize = true,
                Location = new Point(18, 16),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            };

            var hint = new Label
            {
                Text = "是否立即打开客户端界面？",
                AutoSize = true,
                Location = new Point(18, 52),
                Font = new Font("Microsoft YaHei UI", 9F),
            };

            _openNow = new CheckBox
            {
                Text = "立即打开",
                Checked = true,
                AutoSize = true,
                Location = new Point(18, 88),
                Font = new Font("Microsoft YaHei UI", 9F),
            };

            var confirm = new Button
            {
                Text = "确认",
                Size = new Size(90, 30),
                Location = new Point(312, 120),
                DialogResult = DialogResult.OK,
                Font = new Font("Microsoft YaHei UI", 9F),
            };
            confirm.Click += (_, _) =>
            {
                ShouldLaunch = _openNow.Checked;
                // Application.Run（非模态）下设置 DialogResult 不会自动关闭窗体，
                // 必须显式 Close()，否则点击「确认」后弹窗不消失、进程挂起（0.16.0 实测复现）。
                Close();
            };
            AcceptButton = confirm;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_openNow);
            Controls.Add(confirm);

            // TopMost 之外再主动激活，确保默认显示在屏幕上
            Shown += (_, _) =>
            {
                Activate();
                BringToFront();
            };
        }
    }
}