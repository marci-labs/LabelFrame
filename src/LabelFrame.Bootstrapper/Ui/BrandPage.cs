using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ui;

/// <summary>打印机品牌多选页（Issue #53 决议 2）：选项来源仅为清单已有 plugin-&lt;brand&gt; 条目；ZDesigner 驱动名预选 Zebra。</summary>
internal sealed class BrandPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly Label _hintLabel = new();
    private readonly FlowLayoutPanel _brandPanel = new();
    private readonly Dictionary<string, CheckBox> _checkBoxes = [];

    public BrandPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "需要哪些打印机品牌的支持？",
            Font = new Font(Control.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _hintLabel.AutoSize = true;
        _hintLabel.Location = new Point(8, 40);
        _hintLabel.MaximumSize = new Size(650, 0);

        _brandPanel.Location = new Point(16, 88);
        _brandPanel.Size = new Size(650, 220);
        _brandPanel.FlowDirection = FlowDirection.TopDown;
        _brandPanel.WrapContents = false;

        Controls.Add(title);
        Controls.Add(_hintLabel);
        Controls.Add(_brandPanel);
    }

    public void OnEnter()
    {
        // 清单可能被重新加载（品牌集合变化）：每次进入重建
        _brandPanel.SuspendLayout();
        foreach (var checkBox in _checkBoxes.Values)
        {
            checkBox.Dispose();
        }

        _checkBoxes.Clear();
        _brandPanel.Controls.Clear();

        var brands = _session.AvailableBrands;
        var applicable = _session.Preset is TopologyPreset.Standalone or TopologyPreset.Client;

        if (brands.Count == 0)
        {
            _hintLabel.Text = "当前安装清单没有独立的品牌插件条目（Zebra 打印支持已内置于打印客户端，无需选择）。";
        }
        else if (applicable)
        {
            _hintLabel.Text = "已检测到本机安装的打印机驱动时会预选对应品牌；未选的品牌将不安装其插件。";
        }
        else
        {
            _hintLabel.Text = "品牌插件仅适用于含打印客户端的部署（单机一体 / 追加打印客户端），当前形态可跳过此页。";
        }

        foreach (var brand in brands)
        {
            var checkBox = new CheckBox
            {
                Text = BrandDisplayName(brand),
                AutoSize = true,
                Enabled = applicable,
                Checked = applicable && _session.SelectedBrands.Contains(brand),
                Tag = brand,
            };
            checkBox.CheckedChanged += (_, _) => _session.SetBrandSelected(brand, checkBox.Checked);

            _checkBoxes[brand] = checkBox;
            _brandPanel.Controls.Add(checkBox);
        }

        _brandPanel.ResumeLayout();
    }

    public bool CanProceed(out string? reason)
    {
        // 品牌多选允许为空（不装任何品牌插件）；非适用预设同样直接放行
        reason = null;
        return true;
    }

    private static string BrandDisplayName(string brand) => brand.ToLowerInvariant() switch
    {
        "zebra" => "Zebra（斑马）",
        _ => brand,
    };
}
