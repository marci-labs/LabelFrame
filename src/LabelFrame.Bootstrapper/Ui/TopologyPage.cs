using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ui;

/// <summary>拓扑预设选择页（DESIGN §6.3 五个 PC 预设，单选）。</summary>
internal sealed class TopologyPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly Dictionary<TopologyPreset, RadioButton> _radioButtons = [];

    public TopologyPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "这台电脑的部署形态是什么？",
            Font = new Font(Control.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        var presets = new[]
        {
            TopologyPreset.Standalone,
            TopologyPreset.ServerWin,
            TopologyPreset.ServerDocker,
            TopologyPreset.ServerLinux,
            TopologyPreset.Client,
        };

        var y = 48;
        foreach (var preset in presets)
        {
            var radio = new RadioButton
            {
                Text = preset.DisplayName(),
                AutoSize = true,
                Location = new Point(16, y),
                Tag = preset,
            };
            var description = new Label
            {
                Text = preset.Description(),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(36, y + 24),
                MaximumSize = new Size(620, 0),
            };
            radio.CheckedChanged += (_, _) =>
            {
                if (radio.Checked)
                {
                    _session.Preset = preset;
                }
            };

            _radioButtons[preset] = radio;
            Controls.Add(radio);
            Controls.Add(description);
            y += 56;
        }

        Controls.Add(title);
    }

    public void OnEnter()
    {
        // 回显已选预设（导航往返）
        if (_session.Preset is { } preset && _radioButtons.TryGetValue(preset, out var radio))
        {
            radio.Checked = true;
        }
    }

    public bool CanProceed(out string? reason)
    {
        if (_session.Preset is null)
        {
            reason = "请选择一个部署形态。";
            return false;
        }

        reason = null;
        return true;
    }
}
