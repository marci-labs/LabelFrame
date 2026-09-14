using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>管理界面开关页（仅有的两项自由开关之二）：webui 组件落位服务端 plugins/web-ui；Docker 形态 = 启用镜像内置界面。</summary>
internal sealed class ManagementUiPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly CheckBox _switchCheckBox = new();
    private readonly Label _descriptionLabel = new();
    private bool _userToggled;

    public ManagementUiPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "是否安装服务端管理界面？",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _switchCheckBox.Text = "安装管理界面（服务端网页，供浏览器访问）";
        _switchCheckBox.AutoSize = true;
        _switchCheckBox.Location = new Point(16, 48);
        _switchCheckBox.CheckedChanged += (_, _) =>
        {
            _userToggled = true;
            _session.IncludeWebUi = _switchCheckBox.Checked;
        };

        _descriptionLabel.AutoSize = true;
        _descriptionLabel.Location = new Point(36, 80);
        _descriptionLabel.MaximumSize = new Size(620, 0);

        Controls.Add(title);
        Controls.Add(_switchCheckBox);
        Controls.Add(_descriptionLabel);
    }

    public void OnEnter()
    {
        var preset = _session.Preset;
        var applicable = preset is TopologyPreset.Standalone or TopologyPreset.ServerWin
            or TopologyPreset.ServerDocker or TopologyPreset.ServerLinux;

        // §6.3 开关适用性与建议：standalone 默认关（客户端本机界面已完整）；分离部署建议开；client 不适用
        var descriptions = new Dictionary<TopologyPreset, string>
        {
            [TopologyPreset.Standalone] = "单机一体默认不安装：客户端本机界面已完整（模板设计、数据与打印、日志都在客户端）。",
            [TopologyPreset.ServerWin] = "分离部署建议安装：服务端默认无头（仅健康检查与 API），管理界面装到服务端后可从局域网浏览器访问。",
            [TopologyPreset.ServerDocker] = "等价于启用 Docker 镜像内置的管理界面（不额外下载插件包）。",
            [TopologyPreset.ServerLinux] = "分离部署建议安装：管理界面插件放入服务端插件目录即生效，无需重启。",
            [TopologyPreset.Client] = "本形态不适用：管理界面安装在服务端，打印客户端无需此项。",
        };

        _descriptionLabel.Text = preset is { } value && descriptions.TryGetValue(value, out var text) ? text : string.Empty;
        _switchCheckBox.Enabled = applicable;

        // 不适用形态强制关（解析器也会按 topologies 过滤，此处让问卷状态与预期一致）；首次进入按预设默认值
        if (!applicable)
        {
            _switchCheckBox.Checked = false;
        }
        else if (!_userToggled)
        {
            _switchCheckBox.Checked = preset != TopologyPreset.Standalone;
        }
    }

    public bool CanProceed(out string? reason)
    {
        reason = null;
        return true;
    }
}
