using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Text;
using Android.Views;
using Android.Widget;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 宿主配置页（唯一 UI）：主页展示运行状态与设置入口，「连接服务器 / 连接打印机 / 本机信息」
/// 三个子页各自编辑并保存（保存即重启宿主服务生效）；设备号由系统唯一码自动生成只读展示。
/// 文案面向不懂技术的仓库用户：只说「这里填什么 / 点按钮会发生什么 / 状态意味着什么与下一步」。
/// </summary>
[Activity(Label = "LabelFrame 标签打印", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // 插件包下载（服务器 → PDA）：包可达 64MB，WiFi 传输需要远超 8s 的预算
    private static readonly HttpClient HttpDownload = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 语义色：绿 = 正常，红 = 异常，橙 = 提醒，蓝 = 主操作；不做装饰用色
    private static readonly Color ColorBackground = Color.Argb(0xFF, 0xF2, 0xF4, 0xF7);
    private static readonly Color ColorText = Color.Argb(0xFF, 0x1F, 0x23, 0x29);
    private static readonly Color ColorTextSecondary = Color.Argb(0xFF, 0x6B, 0x70, 0x75);
    private static readonly Color ColorOk = Color.Argb(0xFF, 0x1B, 0x7E, 0x3A);
    private static readonly Color ColorOkBg = Color.Argb(0xFF, 0xE9, 0xF6, 0xEC);
    private static readonly Color ColorErr = Color.Argb(0xFF, 0xC5, 0x22, 0x1F);
    private static readonly Color ColorErrBg = Color.Argb(0xFF, 0xFD, 0xEC, 0xEA);
    private static readonly Color ColorWarn = Color.Argb(0xFF, 0xB2, 0x5E, 0x00);
    private static readonly Color ColorWarnBg = Color.Argb(0xFF, 0xFF, 0xF3, 0xE0);
    private static readonly Color ColorMutedBg = Color.Argb(0xFF, 0xF1, 0xF3, 0xF5);
    private static readonly Color ColorPrimary = Color.Argb(0xFF, 0x1D, 0x6F, 0xE0);

    private const float RadiusCardDp = 12;
    private const float RadiusButtonDp = 10;

    private LinearLayout _root = null!;
    private LabelHostConfig _config = null!;

    // 主页动态区
    private LinearLayout _statusCard = null!;
    private TextView _statusSummary = null!;
    private TextView _statusServer = null!;
    private TextView _statusPrinter = null!;
    private TextView _serverEntrySummary = null!;
    private TextView _printerEntrySummary = null!;
    private TextView _deviceEntrySummary = null!;
    private TextView _pluginsEntrySummary = null!;

    // 服务器子页
    private EditText _serverInput = null!;
    private TextView _serverTestText = null!;
    private TextView _serverSaveHint = null!;

    // 打印机子页（迭代 96 / 决策 #156：品牌选项由内置 Zebra + 已装外置插件动态扩展）
    private RadioGroup _brandGroup = null!;
    private readonly Dictionary<int, string> _brandIds = new(); // 单选项 Id → 品牌（zebra 或插件 id）
    private RadioGroup _connectionTypeGroup = null!;
    private LinearLayout _connectionTypeCard = null!;
    private LinearLayout _tcpFields = null!;
    private EditText _ipInput = null!;
    private EditText _portInput = null!;
    private LinearLayout _bluetoothFields = null!;
    private EditText _macInput = null!;
    private LinearLayout _usbFields = null!;
    private TextView _printTestText = null!;
    private TextView _printerSaveHint = null!;

    // 插件管理子页（迭代 96 / 决策 #156）
    private LinearLayout _installedList = null!;
    private TextView _installedSummary = null!;
    private LinearLayout _serverPackagesList = null!;
    private TextView _pluginStatusText = null!;
    private List<InstalledPluginDto>? _installedPluginsCache;

    // 本机信息子页
    private TextView _deviceIdText = null!;
    private EditText _deviceNameInput = null!;
    private TextView _deviceSaveHint = null!;

    private readonly Handler _refreshHandler = new(Looper.MainLooper!);
    private bool _autoRefresh;

    private enum Screen
    {
        Home,
        Server,
        Printer,
        Device,
        Plugins,
    }

    private Screen _current = Screen.Home;

    // 屏幕视图构建后缓存：往返导航不丢未保存的输入
    private View? _homeView;
    private View? _serverView;
    private View? _printerView;
    private View? _deviceView;
    private View? _pluginsView;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 1);
        }

        // 打开配置页即确保后台服务在运行（幂等；重复 Start 不会重建已运行的服务）
        EnsureServiceStarted();

        _config = LabelHostConfig.Load(this);

        _root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        _root.SetBackgroundColor(ColorBackground);
        SetContentView(_root);

        ShowScreen(Screen.Home);
    }

    /// <inheritdoc />
    protected override void OnResume()
    {
        base.OnResume();
        _autoRefresh = true;
        RefreshStatus();
        ScheduleAutoRefresh();
    }

    /// <inheritdoc />
    protected override void OnPause()
    {
        _autoRefresh = false;
        base.OnPause();
    }

    /// <summary>系统返回键：子页回主页，主页退出。</summary>
    // API 33 起 OnBackPressed 被标记过时（为预测性返回让路），但应用未启用 enableOnBackInvokedCallback
    // 时框架仍回调它；为避免引入 AndroidX 依赖，按既有行为保留并屏蔽平台版本分析。
#pragma warning disable CA1422
    public override void OnBackPressed()
    {
        if (_current != Screen.Home)
        {
            ShowScreen(Screen.Home);
            return;
        }

        base.OnBackPressed();
    }
#pragma warning restore CA1422

    private void EnsureServiceStarted()
    {
        var service = new Intent(this, typeof(PrintHostService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(service);
        }
        else
        {
            StartService(service);
        }
    }

    // ---------- 屏幕切换 ----------

    private void ShowScreen(Screen screen)
    {
        _current = screen;
        var view = screen switch
        {
            Screen.Server => _serverView ??= BuildServerScreen(),
            Screen.Printer => _printerView ??= BuildPrinterScreen(),
            Screen.Device => _deviceView ??= BuildDeviceScreen(),
            Screen.Plugins => _pluginsView ??= BuildPluginsScreen(),
            _ => _homeView ??= BuildHomeScreen(),
        };
        _root.RemoveAllViews();
        _root.AddView(view);
        if (screen == Screen.Home)
        {
            SyncInputsFromConfig();
            RefreshStatus();
        }
        else if (screen == Screen.Plugins)
        {
            // 每次进入刷新两份列表（已装 / 服务器可选）
            RunAsync(RefreshPluginsScreenAsync);
        }
    }

    // ---------- 主页 ----------

    private ScrollView BuildHomeScreen()
    {
        var content = ScrollColumn();

        content.AddView(PageTitle("LabelFrame 标签打印"));
        content.AddView(Subtle("这台 PDA 的打印设置"));
        content.AddView(Spacing(12));

        _statusCard = Card(ColorMutedBg);
        _statusCard.SetPadding(Dp(14), Dp(12), Dp(14), Dp(10));
        _statusSummary = TextView(string.Empty, 16, ColorText, bold: true);
        _statusServer = TextView(string.Empty, 13, ColorText);
        _statusPrinter = TextView(string.Empty, 13, ColorText);
        var autoNote = TextView("状态每 5 秒自动刷新", 11, ColorTextSecondary);
        autoNote.SetPadding(0, Dp(6), 0, 0);
        _statusCard.AddView(_statusSummary);
        _statusCard.AddView(Spacing(2));
        _statusCard.AddView(_statusServer);
        _statusCard.AddView(Spacing(2));
        _statusCard.AddView(_statusPrinter);
        _statusCard.AddView(autoNote);
        content.AddView(_statusCard);
        content.AddView(Spacing(14));

        content.AddView(EntryRow("① 连接服务器", ShowServer, out _serverEntrySummary));
        content.AddView(Spacing(10));
        content.AddView(EntryRow("② 连接打印机", ShowPrinter, out _printerEntrySummary));
        content.AddView(Spacing(10));
        content.AddView(EntryRow("③ 插件管理", ShowPlugins, out _pluginsEntrySummary));
        content.AddView(Spacing(10));
        content.AddView(EntryRow("④ 本机信息", ShowDevice, out _deviceEntrySummary));

        return WrapScroll(content);
    }

    private void ShowServer() => ShowScreen(Screen.Server);

    private void ShowPrinter() => ShowScreen(Screen.Printer);

    private void ShowPlugins() => ShowScreen(Screen.Plugins);

    private void ShowDevice() => ShowScreen(Screen.Device);

    /// <summary>设置入口行：左侧标题 + 当前值摘要，右侧「›」；整行可点、高度 ≥64dp。</summary>
    private LinearLayout EntryRow(string title, Action onClick, out TextView summary)
    {
        // 行内横向排列：文本列（宽度 0 + weight）+ 箭头。父容器必须是横向，
        // 竖向容器里宽度 0 的子视图会被压成 0 宽（文字逐字换行、卡片又高又空）。
        var row = Card(Color.White);
        row.Orientation = Orientation.Horizontal;
        row.SetPadding(Dp(16), Dp(12), Dp(12), Dp(12));
        row.SetMinimumHeight(Dp(64));

        var textColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        textColumn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        var titleView = TextView(title, 15, ColorText, bold: true);
        titleView.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        summary = TextView(string.Empty, 12.5f, ColorTextSecondary);
        summary.SetPadding(0, Dp(4), 0, 0);
        textColumn.AddView(titleView);
        textColumn.AddView(summary);
        row.AddView(textColumn);

        var chevron = TextView("›", 22, ColorTextSecondary);
        chevron.SetPadding(Dp(8), 0, 0, 0);
        chevron.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        row.AddView(chevron);

        row.Click += (_, _) => onClick();
        return row;
    }

    // ---------- 服务器子页 ----------

    private ScrollView BuildServerScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header("连接服务器"));
        content.AddView(Subtle("地址问管理员要；不用服务器就留空。"));
        content.AddView(Spacing(10));

        content.AddView(FieldLabel("服务器地址"));
        _serverInput = Input("例如 http://192.168.1.10:53961", _config.ServerUrl, InputTypes.ClassText | InputTypes.TextVariationUri);
        content.AddView(_serverInput);
        content.AddView(Spacing(10));

        var testRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        testRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var testButton = ActionButton("测试连接", () => RunAsync(TestServerAsync));
        _serverTestText = TextView(string.Empty, 13, ColorTextSecondary);
        _serverTestText.SetPadding(Dp(10), 0, 0, 0);
        _serverTestText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        testRow.AddView(testButton);
        testRow.AddView(_serverTestText);
        content.AddView(testRow);

        content.AddView(Spacing(20));
        _serverSaveHint = SaveHint();
        content.AddView(_serverSaveHint);
        content.AddView(SaveButton(() => RunAsync(SaveServerAsync)));

        return WrapScroll(content);
    }

    /// <summary>测试连接：探测服务端 healthz（用的是输入框里的地址，不用先保存）。</summary>
    private async Task TestServerAsync()
    {
        var url = (_serverInput.Text?.Trim() ?? string.Empty).TrimEnd('/');
        if (url.Length == 0)
        {
            SetResult(_serverTestText, "请先在上面填服务器地址", ColorTextSecondary);
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SetResult(_serverTestText, "地址不对——要以 http:// 开头，像 http://192.168.1.10:53961 这样", ColorErr);
            return;
        }

        SetResult(_serverTestText, "正在连接…", ColorTextSecondary);
        try
        {
            using var response = await Http.GetAsync($"{url}/healthz");
            if (response.IsSuccessStatusCode)
            {
                SetResult(_serverTestText, "✓ 能连上服务器", ColorOk);
            }
            else
            {
                SetResult(_serverTestText, $"✗ 连不上——请检查地址是否正确（服务器返回码 {(int)response.StatusCode}）", ColorErr);
            }
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"测试服务器连接失败（{url}）：{ex.Message}");
            SetResult(_serverTestText, "✗ 连不上服务器——请检查地址是否正确、PDA 是否连着 WiFi", ColorErr);
        }
    }

    private async Task SaveServerAsync()
    {
        var url = (_serverInput.Text?.Trim() ?? string.Empty).TrimEnd('/');
        if (url.Length > 0
            && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ShowSaveHint(_serverSaveHint, "还没保存——服务器地址要以 http:// 开头，像 http://192.168.1.10:53961；不用服务器就清空");
            return;
        }

        await ApplyAsync(serverUrl: url);
    }

    // ---------- 打印机子页 ----------

    private ScrollView BuildPrinterScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header("连接打印机"));
        content.AddView(Spacing(10));

        // 打印机品牌（迭代 96 / 决策 #156 / 决策 #95 预埋兑现）：Zebra 内置 + 已装外置插件动态扩展
        content.AddView(FieldLabel("打印机品牌"));
        _brandGroup = new RadioGroup(this);
        _brandGroup.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _brandIds.Clear();
        var zebraRadio = ConnectionRadio("Zebra（内置）", "网线 / 蓝牙 / USB 三种连接方式");
        zebraRadio.Id = 0x2001;
        _brandIds[0x2001] = LabelHostConfig.DefaultPrinterBrand;
        _brandGroup.AddView(zebraRadio);
        _brandGroup.Check(BrandRadioOf(_config.PrinterBrand));
        _brandGroup.CheckedChange += (_, _) => UpdateConnectionFields();
        content.AddView(_brandGroup);
        content.AddView(Spacing(6));
        content.AddView(Subtle("其他品牌的打印机先在「插件管理」装对应插件，装好重启后这里就能选。"));
        content.AddView(Spacing(10));
        RunAsync(RefreshBrandOptionsAsync);

        // 连接类型（网口默认且一级路径；蓝牙 / USB 为增量类型，迭代 56 决策 #111）——Zebra 专属，插件品牌隐藏整块
        _connectionTypeCard = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _connectionTypeCard.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _connectionTypeCard.AddView(FieldLabel("连接方式"));
        _connectionTypeGroup = new RadioGroup(this);
        _connectionTypeGroup.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var tcpRadio = ConnectionRadio("网线（推荐）", "插网线的打印机，填 IP 地址");
        tcpRadio.Id = 0x1001;
        var bluetoothRadio = ConnectionRadio("蓝牙", "在打印机设置里能找到蓝牙地址");
        bluetoothRadio.Id = 0x1002;
        var usbRadio = ConnectionRadio("USB 数据线", "打印机用数据线连着这台 PDA");
        usbRadio.Id = 0x1003;
        _connectionTypeGroup.AddView(tcpRadio);
        _connectionTypeGroup.AddView(bluetoothRadio);
        _connectionTypeGroup.AddView(usbRadio);
        _connectionTypeGroup.Check(SelectedConnectionRadio());
        _connectionTypeGroup.CheckedChange += (_, _) => UpdateConnectionFields();
        _connectionTypeCard.AddView(_connectionTypeGroup);
        content.AddView(_connectionTypeCard);
        content.AddView(Spacing(10));

        // 网口参数：IP + 端口
        _tcpFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var fieldRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        fieldRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var ipColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        ipColumn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2.5f)
        {
            MarginEnd = Dp(10),
        };
        ipColumn.AddView(FieldLabel("打印机 IP 地址"));
        _ipInput = Input("例如 192.168.1.50", _config.TcpHost, InputTypes.ClassText);
        ipColumn.AddView(_ipInput);
        var portColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        portColumn.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        portColumn.AddView(FieldLabel("端口"));
        _portInput = Input("9100", _config.TcpPort.ToString(CultureInfo.InvariantCulture), InputTypes.ClassNumber);
        portColumn.AddView(_portInput);
        fieldRow.AddView(ipColumn);
        fieldRow.AddView(portColumn);
        _tcpFields.AddView(fieldRow);
        _tcpFields.AddView(Spacing(6));
        _tcpFields.AddView(Subtle("不知道 IP 就问管理员；端口一般填 9100，不用改。"));
        _tcpFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        content.AddView(_tcpFields);

        // 蓝牙参数：MAC 地址手输（迭代 56 预授权决议 1：首版最简）
        _bluetoothFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _bluetoothFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _bluetoothFields.AddView(FieldLabel("打印机蓝牙地址"));
        _macInput = Input("例如 00:11:22:33:44:55", _config.BluetoothMac, InputTypes.ClassText | InputTypes.TextVariationVisiblePassword);
        _bluetoothFields.AddView(_macInput);
        _bluetoothFields.AddView(Spacing(6));
        _bluetoothFields.AddView(Subtle("打印机开机后在设置里找「蓝牙地址」或问管理员；先用蓝牙配对不需要，直接填地址就行。"));
        content.AddView(_bluetoothFields);

        // USB 参数：自动发现锁定第一台（无可选设备给可行动错误，迭代 56 预授权决议 2）
        _usbFields = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _usbFields.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var usbNote = TextView("保存后自动识别用数据线连着的第一台 Zebra 打印机；第一次用会弹「允许访问 USB 设备」，点允许。", 12.5f, ColorTextSecondary);
        _usbFields.AddView(usbNote);
        content.AddView(_usbFields);
        UpdateConnectionFields();
        content.AddView(Spacing(10));

        var testRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        testRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var testButton = ActionButton("打印测试标签", () => RunAsync(TestPrintAsync));
        _printTestText = TextView(string.Empty, 13, ColorTextSecondary);
        _printTestText.SetPadding(Dp(10), 0, 0, 0);
        _printTestText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        testRow.AddView(testButton);
        testRow.AddView(_printTestText);
        content.AddView(testRow);

        content.AddView(Spacing(20));
        _printerSaveHint = SaveHint();
        content.AddView(_printerSaveHint);
        content.AddView(SaveButton(() => RunAsync(SavePrinterAsync)));

        return WrapScroll(content);
    }

    /// <summary>当前选中的品牌（内置 zebra 或外置插件 id；空选回退 zebra）。</summary>
    private string SelectedBrand()
        => _brandGroup is not null && _brandIds.TryGetValue(_brandGroup.CheckedRadioButtonId, out var brand) ? brand : LabelHostConfig.DefaultPrinterBrand;

    /// <summary>品牌对应的单选项 Id（未安装的品牌 / 未知值回退 Zebra）。</summary>
    private int BrandRadioOf(string brand)
        => string.Equals(brand.Trim(), LabelHostConfig.DefaultPrinterBrand, StringComparison.OrdinalIgnoreCase) || brand.Trim().Length == 0
            ? 0x2001
            : _brandIds.FirstOrDefault(kv => string.Equals(kv.Value, brand.Trim(), StringComparison.OrdinalIgnoreCase)).Key is var id and not 0
                ? id
                : 0x2001;

    /// <summary>刷新品牌选项（已装外部插件动态扩展，决策 #95「品牌选项卡由已装插件扩展」兑现）。</summary>
    private async Task RefreshBrandOptionsAsync()
    {
        var installed = await FetchInstalledPluginsAsync();
        if (installed is null || _brandGroup is null)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            try
            {
                var selected = SelectedBrand();
                for (var i = _brandGroup.ChildCount - 1; i >= 1; i--)
                {
                    _brandGroup.RemoveViewAt(i);
                }

                foreach (var stale in _brandIds.Where(kv => kv.Key != 0x2001).Select(kv => kv.Key).ToList())
                {
                    _brandIds.Remove(stale);
                }
                var index = 0;
                foreach (var plugin in installed.Where(p => p.Loaded && p.Source == "package"))
                {
                    var id = 0x2100 + index++;
                    var radio = ConnectionRadio(plugin.Name, $"插件品牌（{plugin.PluginId}）—— 网线连接");
                    radio.Id = id;
                    _brandIds[id] = plugin.PluginId;
                    _brandGroup.AddView(radio);
                }

                _brandGroup.Check(BrandRadioOf(selected));
                if (!string.Equals(selected, LabelHostConfig.DefaultPrinterBrand, StringComparison.OrdinalIgnoreCase)
                    && BrandRadioOf(selected) == 0x2001)
                {
                    // 配置引用的插件品牌当前不可用（已卸载 / 加载失败）：状态栏摘要会显示回退，这里按 Zebra 呈现
                    HostLog.Warn(HostLog.Tags.Ui, $"配置的打印机品牌 {selected} 插件未装配，品牌选项回退 Zebra。");
                }

                UpdateConnectionFields();
            }
            catch (Exception ex)
            {
                HostLog.Warn(HostLog.Tags.Ui, $"刷新品牌选项失败：{ex.Message}");
            }
        });
    }

    /// <summary>连接方式单选项：圆角卡片行 + 标题说明，触达 ≥48dp。</summary>
    private RadioButton ConnectionRadio(string title, string hint)
    {
        var button = new RadioButton(this) { Text = title, TextSize = 15 };
        button.SetTextColor(ColorText);
        button.SetPadding(Dp(8), Dp(10), Dp(8), Dp(10));
        button.SetMinimumHeight(Dp(48));
        return button;
    }

    /// <summary>当前选中连接类型对应的存储值（插件品牌固定 tcp——决策 #95 配置结构零变更）。</summary>
    private string SelectedConnectionType()
        => IsPluginBrandSelected()
            ? LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp
            : _connectionTypeGroup.CheckedRadioButtonId switch
            {
                0x1002 => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth,
                0x1003 => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeUsb,
                _ => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp,
            };

    /// <summary>当前是否选中了外置插件品牌（非内置 Zebra）。</summary>
    private bool IsPluginBrandSelected()
        => !string.Equals(SelectedBrand(), LabelHostConfig.DefaultPrinterBrand, StringComparison.OrdinalIgnoreCase);

    /// <summary>当前配置连接类型对应的单选项 Id。</summary>
    private int SelectedConnectionRadio() => LabelFrame.AndroidHost.Transport.ZebraSdkTransport.NormalizeConnectionType(_config.ConnectionType) switch
    {
        LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth => 0x1002,
        LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeUsb => 0x1003,
        _ => 0x1001,
    };

    /// <summary>按选中的品牌 / 连接方式切换参数区（插件品牌只显示 IP + 端口）。</summary>
    private void UpdateConnectionFields()
    {
        var pluginBrand = _brandGroup is not null && IsPluginBrandSelected();
        if (_connectionTypeCard is not null)
        {
            _connectionTypeCard.Visibility = pluginBrand ? ViewStates.Gone : ViewStates.Visible;
        }

        var type = SelectedConnectionType();
        _tcpFields.Visibility = type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp ? ViewStates.Visible : ViewStates.Gone;
        _bluetoothFields.Visibility = !pluginBrand && type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth ? ViewStates.Visible : ViewStates.Gone;
        _usbFields.Visibility = !pluginBrand && type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeUsb ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>测试打印：经本地 HTTP 提交内置测试标签，轮询终态。用的是已保存的地址——输入未保存时先提示保存。</summary>
    private async Task TestPrintAsync()
    {
        if (PrinterInputDirty())
        {
            SetResult(_printTestText, "先保存再测试——上面填的地址还没保存，现在测试用的还是旧地址", ColorWarn);
            return;
        }

        SetResult(_printTestText, "正在发送到打印机…", ColorTextSecondary);
        try
        {
            using var response = await Http.PostAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/host/test-print", content: null);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                SetResult(_printTestText, $"✗ 没打出来——请再试一次（{Truncate(body)}）", ColorErr);
                return;
            }

            var job = JsonSerializer.Deserialize<JobStatusDto>(body, Json);
            if (job is null)
            {
                SetResult(_printTestText, "✗ 没打出来——请再试一次", ColorErr);
                return;
            }

            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                using var poll = await Http.GetAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/jobs/{job.JobId}");
                var pollBody = await poll.Content.ReadAsStringAsync();
                var current = JsonSerializer.Deserialize<JobStatusDto>(pollBody, Json);
                if (current is null)
                {
                    continue;
                }

                if (current.Status is "Completed")
                {
                    SetResult(_printTestText, $"✓ 打印成功——打印机应该已经出纸（{current.CompletedItems}/{current.TotalItems}）", ColorOk);
                    return;
                }

                if (current.Status is "Failed" or "Cancelled")
                {
                    var error = current.Items?.FirstOrDefault(i2 => i2.ErrorMessage is not null)?.ErrorMessage;
                    SetResult(_printTestText, PrintFailText(error ?? current.Status), ColorErr);
                    return;
                }

                SetResult(_printTestText, "正在打印…", ColorTextSecondary);
            }

            SetResult(_printTestText, "等了 1 分钟还没打完——请再点一次；反复失败就看看打印机是不是卡纸或缺纸", ColorWarn);
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"测试打印失败：{ex.Message}");
            SetResult(_printTestText, "✗ 打印服务没反应——请点「保存并重启服务」后再试", ColorErr);
        }
    }

    /// <summary>打印失败的可行动提示；原始原因作为第二行小字附后，供管理员远程排障。</summary>
    private static string PrintFailText(string reason) =>
        $"✗ 没打出来——请检查打印机是否开机、连接方式与地址是否正确、是否缺纸卡纸\n原因：{reason}";

    /// <summary>输入的打印机配置与已保存（正在使用）的是否不一致（品牌 + 按连接类型比较对应参数）。</summary>
    private bool PrinterInputDirty()
    {
        if (!string.Equals(SelectedBrand(), _config.PrinterBrand.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsPluginBrandSelected())
        {
            var pluginIp = _ipInput.Text?.Trim() ?? string.Empty;
            var pluginPort = int.TryParse(_portInput.Text?.Trim(), out var parsedPort) ? parsedPort : -1;
            return pluginIp != _config.TcpHost || pluginPort != _config.TcpPort;
        }

        if (SelectedConnectionType() != LabelFrame.AndroidHost.Transport.ZebraSdkTransport.NormalizeConnectionType(_config.ConnectionType))
        {
            return true;
        }

        if (SelectedConnectionType() == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth)
        {
            return (_macInput.Text?.Trim() ?? string.Empty) != _config.BluetoothMac;
        }

        var ip = _ipInput.Text?.Trim() ?? string.Empty;
        var port = int.TryParse(_portInput.Text?.Trim(), out var parsed) ? parsed : -1;
        return ip != _config.TcpHost || port != _config.TcpPort;
    }

    /// <summary>蓝牙地址格式校验：12 位十六进制，冒号分隔（AA:BB:CC:DD:EE:FF）或连写（AABBCCDDEEFF）均可。</summary>
    private static bool IsValidBluetoothMac(string value) =>
        value.Length is 12 or 17 && value.Replace(":", string.Empty).Length == 12
        && value.Replace(":", string.Empty).All(char.IsAsciiHexDigit);

    private async Task SavePrinterAsync()
    {
        var brand = SelectedBrand();
        var type = SelectedConnectionType();

        // IP / 端口校验（插件品牌固定网口；Zebra 网口同样必填）
        if (IsPluginBrandSelected() || type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeTcp)
        {
            var ip = _ipInput.Text?.Trim() ?? string.Empty;
            if (ip.Length == 0)
            {
                ShowSaveHint(_printerSaveHint, "还没保存——请先填写打印机 IP 地址");
                return;
            }

            if (!int.TryParse(_portInput.Text?.Trim(), out var port) || port is < 1 or > 65535)
            {
                ShowSaveHint(_printerSaveHint, "还没保存——端口要填 1 到 65535 之间的数字，一般填 9100");
                return;
            }

            await ApplyAsync(printerBrand: brand, connectionType: type, tcpHost: ip, tcpPort: port);
            return;
        }

        string? mac = null;
        if (type == LabelFrame.AndroidHost.Transport.ZebraSdkTransport.ConnectionTypeBluetooth)
        {
            mac = _macInput.Text?.Trim() ?? string.Empty;
            if (!IsValidBluetoothMac(mac))
            {
                ShowSaveHint(_printerSaveHint, "还没保存——蓝牙地址要像 00:11:22:33:44:55 这样（12 位数字和字母），问管理员要");
                return;
            }

            RequestBluetoothPermissionIfNeeded();
        }

        await ApplyAsync(printerBrand: brand, connectionType: type, bluetoothMac: mac);
    }

    /// <summary>Android 12+ 蓝牙连接需要「附近的设备」运行时权限：选蓝牙保存时顺带请求（拒绝不阻塞保存，打印 / 测试时会再提示）。</summary>
    private void RequestBluetoothPermissionIfNeeded()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) != Android.Content.PM.Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.BluetoothConnect], 2);
        }
    }

    // ---------- 插件管理子页（迭代 96 / 决策 #156 ②：已装列表 + 浏览服务端安装 + 卸载，重启生效） ----------

    private ScrollView BuildPluginsScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header("插件管理"));
        content.AddView(Spacing(4));
        content.AddView(Subtle("新品牌的打印机在这里装插件；装好重启后「连接打印机」里就能选这个品牌。"));
        content.AddView(Spacing(10));

        content.AddView(FieldLabel("已安装的插件"));
        _installedSummary = TextView("正在读取…", 13, ColorTextSecondary);
        content.AddView(_installedSummary);
        content.AddView(Spacing(6));
        _installedList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _installedList.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        content.AddView(_installedList);

        content.AddView(Spacing(16));
        content.AddView(FieldLabel("服务器上的插件"));
        _pluginStatusText = TextView(string.Empty, 13, ColorTextSecondary);
        content.AddView(_pluginStatusText);
        content.AddView(Spacing(6));
        _serverPackagesList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _serverPackagesList.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        content.AddView(_serverPackagesList);

        return WrapScroll(content);
    }

    /// <summary>刷新插件子页两份列表：本机已装（本地 HTTP）+ 服务器可选（plugin-packages）。</summary>
    private async Task RefreshPluginsScreenAsync()
    {
        var installed = await FetchInstalledPluginsAsync();
        if (installed is not null)
        {
            _installedPluginsCache = installed;
            RunOnUiThread(() => RenderInstalledPlugins(installed));
        }

        await RefreshServerPackagesAsync();
        RefreshStatus();
    }

    /// <summary>渲染已装列表卡片（package 来源可卸载；加载失败给原因，不阻断）。</summary>
    private void RenderInstalledPlugins(IReadOnlyList<InstalledPluginDto> installed)
    {
        if (_installedList is null)
        {
            return;
        }

        _installedList.RemoveAllViews();
        var packages = installed.Where(p => p.Source == "package").ToList();
        _installedSummary.Text = packages.Count == 0
            ? "还没装任何插件——Zebra 打印机不用插件（内置）。"
            : $"共 {packages.Count} 个插件。卸载或安装后要重启打印服务才生效。";

        foreach (var plugin in packages)
        {
            var pluginId = plugin.PluginId;
            var statusText = plugin.Loaded
                ? "✓ 已加载，可在「连接打印机」选择该品牌"
                : $"未加载：{plugin.LoadError ?? "重启打印服务后生效"}";
            var card = Card(plugin.Loaded ? Color.White : ColorWarnBg);
            card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(10));

            var title = TextView($"{plugin.Name}（{plugin.Version}）", 15, ColorText, bold: true);
            var idLine = TextView(pluginId, 12, ColorTextSecondary);
            var status = TextView(statusText, 12.5f, plugin.Loaded ? ColorOk : ColorWarn);
            status.SetPadding(0, Dp(4), 0, 0);
            card.AddView(title);
            card.AddView(idLine);
            card.AddView(status);

            var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            buttonRow.SetPadding(0, Dp(8), 0, 0);
            // 迭代 104（#225，决策 #161）：卸载 = 销毁类操作——红底白字 danger 视觉，点击先弹原生确认对话框
            var uninstallButton = DangerButton("卸载", () => ConfirmUninstallPlugin(pluginId, plugin.Name), Dp(88));
            buttonRow.AddView(uninstallButton);
            card.AddView(buttonRow);

            _installedList.AddView(card);
            _installedList.AddView(Spacing(8));
        }
    }

    /// <summary>拉取服务器插件包列表并渲染（服务器地址未配置给可行动提示）。</summary>
    private async Task RefreshServerPackagesAsync()
    {
        if (_serverPackagesList is null)
        {
            return;
        }

        var serverUrl = _config.ServerUrl.TrimEnd('/');
        if (serverUrl.Length == 0)
        {
            RunOnUiThread(() =>
            {
                _serverPackagesList.RemoveAllViews();
                _pluginStatusText.Text = "还没填服务器地址——先在「连接服务器」里填好再回来装插件。";
                _pluginStatusText.SetTextColor(ColorWarn);
            });
            return;
        }

        RunOnUiThread(() => _pluginStatusText.Text = "正在读取服务器插件列表…");
        try
        {
            using var response = await Http.GetAsync($"{serverUrl}/api/plugin-packages");
            if (!response.IsSuccessStatusCode)
            {
                RunOnUiThread(() =>
                {
                    _serverPackagesList.RemoveAllViews();
                    _pluginStatusText.Text = $"✗ 读不到插件列表（服务器返回码 {(int)response.StatusCode}）——请检查服务器地址";
                    _pluginStatusText.SetTextColor(ColorErr);
                });
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            var packages = JsonSerializer.Deserialize<List<ServerPluginPackageDto>>(body, Json) ?? [];
            RunOnUiThread(() => RenderServerPackages(serverUrl, packages));
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"读取服务器插件列表失败（{serverUrl}）：{ex.Message}");
            RunOnUiThread(() =>
            {
                _serverPackagesList.RemoveAllViews();
                _pluginStatusText.Text = "✗ 连不上服务器——请检查地址是否正确、PDA 是否连着 WiFi";
                _pluginStatusText.SetTextColor(ColorErr);
            });
        }
    }

    /// <summary>渲染服务器插件包卡片（invalid 条目跳过；平台标记透出——PDA 只能装 android 包）。</summary>
    private void RenderServerPackages(string serverUrl, IReadOnlyList<ServerPluginPackageDto> packages)
    {
        _serverPackagesList.RemoveAllViews();
        var valid = packages.Where(p => p.Valid).ToList();
        _pluginStatusText.Text = valid.Count == 0
            ? "服务器上还没有可装的插件——让管理员在服务端「插件分发」页上传。"
            : $"服务器共 {valid.Count} 个插件可装。";
        _pluginStatusText.SetTextColor(ColorTextSecondary);

        foreach (var package in valid)
        {
            var captured = package;
            var platforms = captured.Platforms is { Count: > 0 } list ? string.Join("、", list) : "windows";
            var isAndroid = captured.Platforms?.Contains("android", StringComparer.OrdinalIgnoreCase) == true;
            var card = Card(Color.White);
            card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(10));

            var title = TextView($"{captured.Name}（{captured.Version}）", 15, ColorText, bold: true);
            var idLine = TextView($"{captured.PluginId} · 平台：{platforms}", 12, ColorTextSecondary);
            card.AddView(title);
            card.AddView(idLine);
            if (!string.IsNullOrWhiteSpace(captured.Description))
            {
                card.AddView(TextView(captured.Description, 12.5f, ColorTextSecondary));
            }

            var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            buttonRow.SetPadding(0, Dp(8), 0, 0);
            var installButton = ActionButton("安装", () => RunAsync(() => InstallPluginAsync(serverUrl, captured)), Dp(88));
            if (!isAndroid)
            {
                installButton.Enabled = false;
                installButton.Text = "电脑专用";
            }

            buttonRow.AddView(installButton);
            card.AddView(buttonRow);

            _serverPackagesList.AddView(card);
            _serverPackagesList.AddView(Spacing(8));
        }
    }

    /// <summary>从服务器下载插件包并经本地 HTTP 安装（三层校验含平台门）→ 重启打印服务生效。</summary>
    private async Task InstallPluginAsync(string serverUrl, ServerPluginPackageDto package)
    {
        RunOnUiThread(() =>
        {
            _pluginStatusText.Text = $"正在下载「{package.Name}」…";
            _pluginStatusText.SetTextColor(ColorTextSecondary);
        });

        byte[] bytes;
        try
        {
            bytes = await HttpDownload.GetByteArrayAsync($"{serverUrl}{package.Url}");
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"下载插件包失败（{package.FileName}）：{ex.Message}");
            RunOnUiThread(() => SetResult(_pluginStatusText, "✗ 下载失败——请检查 PDA 与服务器的网络后重试", ColorErr));
            return;
        }

        RunOnUiThread(() => _pluginStatusText.Text = "正在安装…");
        try
        {
            using var response = await Http.PostAsync(
                $"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/plugins/install",
                new ByteArrayContent(bytes));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var error = TryReadErrorMessage(body) ?? $"安装失败（服务器返回码 {(int)response.StatusCode}）";
                RunOnUiThread(() => SetResult(_pluginStatusText, $"✗ {error}", ColorErr));
                return;
            }

            // 安装成功：重启打印服务生效（装配新插件 + 品牌选项扩展）
            await RestartServiceAsync();
            RunOnUiThread(() =>
            {
                Toast.MakeText(this, $"插件「{package.Name}」已安装，打印服务已重启生效", ToastLength.Long)?.Show();
                SetResult(_pluginStatusText, $"✓ 「{package.Name}」已安装并生效——去「连接打印机」选这个品牌", ColorOk);
            });
            await RefreshPluginsScreenAsync();
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"安装插件失败（{package.FileName}）：{ex.Message}");
            RunOnUiThread(() => SetResult(_pluginStatusText, "✗ 安装失败——打印服务没反应，请点「保存并重启服务」后重试", ColorErr));
        }
    }

    /// <summary>
    /// 卸载确认（迭代 104 / #225，决策 #161：销毁类操作必须先确认）——原生 AlertDialog，danger 文案与视觉：
    /// 标题 + 后果说明（含「不可恢复」与自动重启提示）+ 确认按钮红字；确认后才执行卸载（删插件目录并重启打印服务）。
    /// </summary>
    private void ConfirmUninstallPlugin(string pluginId, string pluginName)
    {
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("卸载插件");
        builder.SetMessage($"确定卸载「{pluginName}」吗？卸载后这个品牌的打印机暂时不能打印，该操作不可恢复；确认后会自动重启打印服务。");
        builder.SetNegativeButton("取消", (_, _) => { });
        builder.SetPositiveButton("卸载", (_, _) => RunAsync(() => UninstallPluginAsync(pluginId)));
        // 绑定注解将 Create() 标为可空（Java 契约实际不返回 null），按仓库 0 警口径显式断言非空（与 Typeface.Monospace! 同款）
        var dialog = builder.Create()!;
        dialog.Show();
        // danger 视觉：确认（卸载）按钮红字警示；取消为常规次要色
        dialog.GetButton((int)DialogButtonType.Positive)?.SetTextColor(ColorErr);
        dialog.GetButton((int)DialogButtonType.Negative)?.SetTextColor(ColorTextSecondary);
    }

    /// <summary>卸载已装插件（本地 HTTP）→ 重启打印服务生效；卸载后如配置还指向该品牌则回退 Zebra。</summary>
    private async Task UninstallPluginAsync(string pluginId)
    {
        try
        {
            using var response = await Http.PostAsync(
                $"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/plugins/uninstall",
                new StringContent(JsonSerializer.Serialize(new { pluginId }, Json), System.Text.Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var error = TryReadErrorMessage(body) ?? $"卸载失败（服务器返回码 {(int)response.StatusCode}）";
                RunOnUiThread(() => Toast.MakeText(this, $"✗ {error}", ToastLength.Long)?.Show());
                return;
            }

            // 配置还指向被卸载品牌时回退 Zebra（宿主路由同样回退，这里同步配置避免界面悬空）
            if (string.Equals(_config.PrinterBrand.Trim(), pluginId, StringComparison.OrdinalIgnoreCase))
            {
                _config.Persist(this, printerBrand: LabelHostConfig.DefaultPrinterBrand);
            }

            await RestartServiceAsync();
            RunOnUiThread(() => Toast.MakeText(this, $"插件「{pluginId}」已卸载，打印服务已重启生效", ToastLength.Long)?.Show());
            await RefreshPluginsScreenAsync();
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"卸载插件失败（{pluginId}）：{ex.Message}");
            RunOnUiThread(() => Toast.MakeText(this, "✗ 卸载失败，请再试一次", ToastLength.Long)?.Show());
        }
    }

    /// <summary>读取本机已装插件列表（本地 HTTP；服务未就绪返回 null）。</summary>
    private static async Task<List<InstalledPluginDto>?> FetchInstalledPluginsAsync()
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/plugins/installed");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<InstalledPluginDto>>(body, Json);
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"读取已装插件列表失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>从错误响应 JSON 里取中文消息（取不到返回 null）。</summary>
    private static string? TryReadErrorMessage(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorMessageDto>(body, Json)?.Message;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>本机已装插件视图（GET /api/plugins/installed 响应形状，PascalCase）。</summary>
    private sealed record InstalledPluginDto(
        string PluginId,
        string Name,
        string Version,
        string? Description,
        bool Loaded,
        string? LoadError,
        string? PackageDir,
        string Source);

    /// <summary>服务器插件包视图（GET /api/plugin-packages 列表项，camelCase）。</summary>
    private sealed record ServerPluginPackageDto(
        string FileName,
        string? PluginId,
        string? Name,
        string? Version,
        string? Description,
        string Url,
        bool Valid,
        IReadOnlyList<string>? Platforms);

    private sealed record ErrorMessageDto(string? Message);

    // ---------- 本机信息子页 ----------

    private ScrollView BuildDeviceScreen()
    {
        var content = ScrollColumn();

        content.AddView(Header("本机信息"));
        content.AddView(Spacing(10));

        content.AddView(FieldLabel("设备号"));
        var idRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        idRow.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _deviceIdText = TextView(_config.DeviceId, 14, ColorText);
        _deviceIdText.SetTypeface(Android.Graphics.Typeface.Monospace!, Android.Graphics.TypefaceStyle.Normal);
        _deviceIdText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        var copyButton = ActionButton("复制", CopyDeviceId, Dp(72));
        idRow.AddView(_deviceIdText);
        idRow.AddView(copyButton);
        content.AddView(idRow);
        content.AddView(Spacing(14));

        content.AddView(FieldLabel("设备名称"));
        _deviceNameInput = Input("例如：仓库门口 PDA", _config.DeviceName, InputTypes.ClassText);
        content.AddView(_deviceNameInput);
        content.AddView(Spacing(6));
        content.AddView(Subtle("服务器设备列表里显示的名字"));
        content.AddView(Spacing(20));

        _deviceSaveHint = SaveHint();
        content.AddView(_deviceSaveHint);
        content.AddView(SaveButton(() => RunAsync(SaveDeviceAsync)));
        content.AddView(Spacing(16));

        content.AddView(FieldLabel("程序版本"));
        content.AddView(TextView(HostInfo.GetVersion(this), 14, ColorText));
        content.AddView(Spacing(4));
        content.AddView(Subtle("有问题上报时把这个号告诉管理员"));

        return WrapScroll(content);
    }

    private void CopyDeviceId()
    {
        var clipboard = GetSystemService(ClipboardService)?.JavaCast<Android.Content.ClipboardManager>();
        clipboard?.PrimaryClip = ClipData.NewPlainText("LabelFrame 设备号", _config.DeviceId);
        Toast.MakeText(this, "已复制设备号", ToastLength.Short)?.Show();
    }

    private async Task SaveDeviceAsync() =>
        await ApplyAsync(deviceName: _deviceNameInput.Text?.Trim());

    // ---------- 保存并重启 ----------

    /// <summary>保存配置并重启宿主服务使其生效（传输 / 路由实例在服务启动时创建）。</summary>
    private async Task ApplyAsync(
        string? serverUrl = null,
        string? printerBrand = null,
        string? connectionType = null,
        string? tcpHost = null,
        int? tcpPort = null,
        string? bluetoothMac = null,
        string? deviceName = null)
    {
        Toast.MakeText(this, "已保存，正在重启打印服务…", ToastLength.Short)?.Show();
        _config.Persist(this, serverUrl, printerBrand: printerBrand, connectionType: connectionType, tcpHost: tcpHost, tcpPort: tcpPort, bluetoothMac: bluetoothMac, deviceName: deviceName);
        await RestartServiceAsync();
        Toast.MakeText(this, "设置已保存，打印服务已重启", ToastLength.Short)?.Show();
    }

    /// <summary>重启宿主服务并等待本地 HTTP 就绪（配置保存 / 插件安装 / 卸载后共用——重启生效）。</summary>
    private async Task RestartServiceAsync()
    {
        StopService(new Intent(this, typeof(PrintHostService)));
        await Task.Delay(800);
        EnsureServiceStarted();

        // 等本地 HTTP 就绪后再刷新界面
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var probe = await Http.GetAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/healthz");
                if (probe.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch
            {
                // 服务未就绪，继续等待
            }

            await Task.Delay(500);
        }

        _config = LabelHostConfig.Load(this);
        _installedPluginsCache = null;
        // 品牌选项随插件装配变化：打印机子页缓存失效，重进时重建（品牌选项卡由已装插件扩展）
        _printerView = null;
        SyncInputsFromConfig();
        RefreshStatus();
    }

    /// <summary>保存 / 重启后把输入框刷成正在使用的配置（子页惰性构建，未打开的跳过）。</summary>
    private void SyncInputsFromConfig()
    {
        if (_serverInput is not null)
        {
            _serverInput.Text = _config.ServerUrl;
        }

        if (_connectionTypeGroup is not null && _ipInput is not null && _portInput is not null && _macInput is not null)
        {
            if (_brandGroup is not null)
            {
                _brandGroup.Check(BrandRadioOf(_config.PrinterBrand));
            }

            _connectionTypeGroup.Check(SelectedConnectionRadio());
            _ipInput.Text = _config.TcpHost;
            _portInput.Text = _config.TcpPort.ToString(CultureInfo.InvariantCulture);
            _macInput.Text = _config.BluetoothMac;
            UpdateConnectionFields();
        }

        if (_deviceIdText is not null)
        {
            _deviceIdText.Text = _config.DeviceId;
        }

        if (_deviceNameInput is not null)
        {
            _deviceNameInput.Text = _config.DeviceName;
        }
    }

    // ---------- 状态刷新 ----------

    private void ScheduleAutoRefresh()
    {
        _refreshHandler.PostDelayed(() =>
        {
            if (!_autoRefresh)
            {
                return;
            }

            RefreshStatus();
            ScheduleAutoRefresh();
        }, 5000);
    }

    private void RefreshStatus()
    {
        if (_statusSummary is null || _serverEntrySummary is null)
        {
            return;
        }

        var view = ComputeStatus();

        _statusCard.Background = RoundDrawable(view.SummaryBg);
        _statusSummary.SetTextColor(view.SummaryColor);
        _statusSummary.Text = view.Summary;
        _statusServer.Text = view.ServerLine;
        _statusPrinter.Text = view.PrinterLine;
        _serverEntrySummary.Text = view.ServerEntry;
        _printerEntrySummary.Text = view.PrinterEntry;
        _pluginsEntrySummary.Text = _installedPluginsCache is null ? "新增打印机品牌在这里装" : $"已装 {_installedPluginsCache.Count} 个插件";
        _deviceEntrySummary.Text = _config.DeviceName;
    }

    private sealed record StatusView(
        Color SummaryColor, Color SummaryBg, string Summary,
        string ServerLine, string PrinterLine, string ServerEntry, string PrinterEntry);

    private static StatusView ComputeStatus()
    {
        var s = HostStatus.Current;
        static string Local(DateTime? utc) => utc is null ? string.Empty : utc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        var serverConfigured = s.ActiveServerUrl.Length > 0;
        var serverError = serverConfigured && s.LastServerError is not null;
        var serverOk = serverConfigured && !serverError && s.LastServerContactUtc is not null;

        string serverLine, serverEntry;
        if (!serverConfigured)
        {
            serverLine = "服务器：未设置——只用本机网页打印";
            serverEntry = "未设置";
        }
        else if (serverError)
        {
            serverLine = "服务器：连不上——请检查地址是否正确、PDA 是否连着 WiFi";
            serverEntry = $"连不上 · {UrlHost(s.ActiveServerUrl)}";
        }
        else if (serverOk)
        {
            serverLine = $"服务器：已连接 · {Local(s.LastServerContactUtc)} 通话正常";
            serverEntry = $"已连接 · {UrlHost(s.ActiveServerUrl)}";
        }
        else
        {
            serverLine = "服务器：正在连接…";
            serverEntry = $"正在连接 · {UrlHost(s.ActiveServerUrl)}";
        }

        // ActivePrinterEndpoint 已是按连接类型生成的用户可读摘要（网口 IP / 蓝牙地址 / USB 数据线）
        var printerDisplay = s.ActivePrinterEndpoint;
        var printerError = s.LastPrintError is not null;
        string printerLine, printerEntry;
        if (printerError)
        {
            printerLine = "打印机：连不上——请检查打印机是否开机、连接方式与地址是否正确";
            printerEntry = $"{printerDisplay} · 连不上";
        }
        else if (s.LastPrintUtc is not null)
        {
            printerLine = $"打印机：正常 · {Local(s.LastPrintUtc)} 出过纸（{printerDisplay}）";
            printerEntry = $"{printerDisplay} · 正常";
        }
        else
        {
            printerLine = $"打印机：还没打印过（{printerDisplay}）";
            printerEntry = $"{printerDisplay} · 还没打印过";
        }

        Color summaryColor, summaryBg;
        string summary;
        if (!s.ServiceRunning)
        {
            summaryColor = ColorTextSecondary;
            summaryBg = ColorMutedBg;
            summary = "打印服务没有在运行——重启 PDA 试试";
        }
        else if (serverError && printerError)
        {
            summaryColor = ColorErr;
            summaryBg = ColorErrBg;
            summary = "服务器和打印机都连不上";
        }
        else if (serverError)
        {
            summaryColor = ColorErr;
            summaryBg = ColorErrBg;
            summary = "服务器连不上——正在自动重试";
        }
        else if (printerError)
        {
            summaryColor = ColorErr;
            summaryBg = ColorErrBg;
            summary = "打印机连不上";
        }
        else
        {
            summaryColor = ColorOk;
            summaryBg = ColorOkBg;
            summary = "一切正常，随时可以打印";
        }

        return new StatusView(summaryColor, summaryBg, summary, serverLine, printerLine, serverEntry, printerEntry);
    }

    /// <summary>取 URL 里的主机部分（只给用户看，去掉协议与端口）。</summary>
    private static string UrlHost(string url)
    {
        var noScheme = url.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        var colon = noScheme.IndexOf(':');
        return colon > 0 ? noScheme[..colon] : noScheme;
    }

    // ---------- 通用控件（代码布局，无资源依赖） ----------

    private int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density);

    private LinearLayout ScrollColumn()
    {
        var column = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        column.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        return column;
    }

    private ScrollView WrapScroll(LinearLayout content)
    {
        var scroll = new ScrollView(this)
        {
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        scroll.AddView(content);
        return scroll;
    }

    private TextView PageTitle(string text)
    {
        var view = TextView(text, 20, ColorText, bold: true);
        view.SetPadding(0, 0, 0, Dp(2));
        return view;
    }

    /// <summary>子页页头：返回箭头 + 标题；返回箭头触达高度 ≥48dp。</summary>
    private LinearLayout Header(string text)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var back = TextView("‹", 26, ColorPrimary);
        back.SetPadding(Dp(4), 0, Dp(8), 0);
        back.SetMinimumHeight(Dp(48));
        back.Gravity = GravityFlags.Center;
        back.Click += (_, _) => ShowScreen(Screen.Home);
        var title = TextView(text, 17, ColorText, bold: true);
        title.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        row.AddView(back);
        row.AddView(title);
        return row;
    }

    private TextView Subtle(string text) => TextView(text, 12.5f, ColorTextSecondary);

    private TextView FieldLabel(string text)
    {
        var view = TextView(text, 13, ColorText, bold: true);
        view.SetPadding(0, 0, 0, Dp(6));
        return view;
    }

    private TextView TextView(string text, float sp, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = text, TextSize = sp };
        view.SetTextColor(color);
        if (bold)
        {
            view.SetTypeface(Android.Graphics.Typeface.DefaultBold!, Android.Graphics.TypefaceStyle.Bold);
        }

        return view;
    }

    private EditText Input(string hint, string value, InputTypes type) => new(this)
    {
        Hint = hint,
        Text = value,
        InputType = type,
    };

    /// <summary>卡片容器：圆角彩色底。</summary>
    private LinearLayout Card(Color background)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        card.Background = RoundDrawable(background);
        return card;
    }

    private static GradientDrawable RoundDrawable(Color color, float radiusDp = RadiusCardDp)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(radiusDp);
        drawable.SetColor(color);
        return drawable;
    }

    /// <summary>次级操作按钮（测试连接 / 复制）：透明底 + 蓝字蓝边，高度 ≥48dp。</summary>
    private Button ActionButton(string text, Action onClick, int? width = null)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.SetTextColor(ColorPrimary);
        button.Background = OutlineDrawable();
        button.SetMinimumHeight(Dp(48));
        button.LayoutParameters = new LinearLayout.LayoutParams(width ?? ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>销毁类操作按钮（卸载，迭代 104 / #225 决策 #161）：红底白字实心 danger 视觉，高度 ≥48dp。</summary>
    private Button DangerButton(string text, Action onClick, int? width = null)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.Background = RoundDrawable(ColorErr, RadiusButtonDp);
        button.SetMinimumHeight(Dp(48));
        button.LayoutParameters = new LinearLayout.LayoutParams(width ?? ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        button.Click += (_, _) => onClick();
        return button;
    }

    private GradientDrawable OutlineDrawable()
    {
        var drawable = RoundDrawable(Color.Argb(0x00, 0, 0, 0));
        drawable.SetStroke(Dp(1), ColorPrimary);
        drawable.SetCornerRadius(RadiusButtonDp);
        return drawable;
    }

    /// <summary>主操作按钮（保存并重启服务）：通栏、蓝底白字、高度 ≥52dp。</summary>
    private Button SaveButton(Action onClick)
    {
        var button = new Button(this) { Text = "保存并重启服务" };
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.SetTypeface(Android.Graphics.Typeface.DefaultBold!, Android.Graphics.TypefaceStyle.Bold);
        button.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        button.Background = RoundDrawable(ColorPrimary, RadiusButtonDp);
        button.SetMinimumHeight(Dp(52));
        button.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>保存校验提示：默认隐藏，出错时红字显示在保存按钮上方。</summary>
    private TextView SaveHint()
    {
        var view = TextView(string.Empty, 12.5f, ColorErr);
        view.Visibility = ViewStates.Gone;
        view.SetPadding(0, 0, 0, Dp(8));
        return view;
    }

    private static void ShowSaveHint(TextView hint, string message)
    {
        hint.Text = message;
        hint.Visibility = ViewStates.Visible;
    }

    private static void SetResult(TextView view, string message, Color color)
    {
        view.Text = message;
        view.SetTextColor(color);
    }

    private View Spacing(float dp) => new View(this)
    {
        LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(dp)),
    };

    // ---------- 执行辅助 ----------

    /// <summary>后台执行并回 UI 线程展示结果（异常兜底提示重试）。</summary>
    private async void RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            HostLog.Warn(HostLog.Tags.Ui, $"操作执行失败：{ex.Message}");
            RunOnUiThread(() => Toast.MakeText(this, "出错了，请再试一次", ToastLength.Short)?.Show());
        }
    }

    /// <summary>测试打印轮询用的作业视图（camelCase）。</summary>
    private sealed record JobStatusDto(string JobId, string Status, int TotalItems, int CompletedItems, IReadOnlyList<JobItemDto>? Items);

    private sealed record JobItemDto(int Index, string Status, string? ErrorCode, string? ErrorMessage);

    private static string Truncate(string text, int max = 120) => text.Length <= max ? text : text[..max] + "…";
}
