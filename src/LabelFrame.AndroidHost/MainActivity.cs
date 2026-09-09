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

    // 服务器子页
    private EditText _serverInput = null!;
    private TextView _serverTestText = null!;
    private TextView _serverSaveHint = null!;

    // 打印机子页
    private EditText _ipInput = null!;
    private EditText _portInput = null!;
    private TextView _printTestText = null!;
    private TextView _printerSaveHint = null!;

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
    }

    private Screen _current = Screen.Home;

    // 屏幕视图构建后缓存：往返导航不丢未保存的输入
    private View? _homeView;
    private View? _serverView;
    private View? _printerView;
    private View? _deviceView;

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
            _ => _homeView ??= BuildHomeScreen(),
        };
        _root.RemoveAllViews();
        _root.AddView(view);
        if (screen == Screen.Home)
        {
            SyncInputsFromConfig();
            RefreshStatus();
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
        content.AddView(EntryRow("③ 本机信息", ShowDevice, out _deviceEntrySummary));

        return WrapScroll(content);
    }

    private void ShowServer() => ShowScreen(Screen.Server);

    private void ShowPrinter() => ShowScreen(Screen.Printer);

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
        catch (Exception)
        {
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
        content.AddView(fieldRow);
        content.AddView(Spacing(6));
        content.AddView(Subtle("不知道 IP 就问管理员；端口一般填 9100，不用改。"));
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
        catch (Exception)
        {
            SetResult(_printTestText, "✗ 打印服务没反应——请点「保存并重启服务」后再试", ColorErr);
        }
    }

    /// <summary>打印失败的可行动提示；原始原因作为第二行小字附后，供管理员远程排障。</summary>
    private static string PrintFailText(string reason) =>
        $"✗ 没打出来——请检查打印机是否开机、IP 是否正确、是否缺纸卡纸\n原因：{reason}";

    /// <summary>输入的打印机地址与已保存（正在使用）的是否不一致。</summary>
    private bool PrinterInputDirty()
    {
        var ip = _ipInput.Text?.Trim() ?? string.Empty;
        var port = int.TryParse(_portInput.Text?.Trim(), out var parsed) ? parsed : -1;
        return ip != _config.TcpHost || port != _config.TcpPort;
    }

    private async Task SavePrinterAsync()
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

        await ApplyAsync(tcpHost: ip, tcpPort: port);
    }

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
    private async Task ApplyAsync(string? serverUrl = null, string? tcpHost = null, int? tcpPort = null, string? deviceName = null)
    {
        Toast.MakeText(this, "已保存，正在重启打印服务…", ToastLength.Short)?.Show();
        _config.Persist(this, serverUrl, tcpHost: tcpHost, tcpPort: tcpPort, deviceName: deviceName);

        StopService(new Intent(this, typeof(PrintHostService)));
        await Task.Delay(800);
        EnsureServiceStarted();

        // 等本地 HTTP 就绪后再刷新界面
        for (var i = 0; i < 10; i++)
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
        SyncInputsFromConfig();
        RefreshStatus();
        Toast.MakeText(this, "设置已保存，打印服务已重启", ToastLength.Short)?.Show();
    }

    /// <summary>保存 / 重启后把输入框刷成正在使用的配置（子页惰性构建，未打开的跳过）。</summary>
    private void SyncInputsFromConfig()
    {
        if (_serverInput is not null)
        {
            _serverInput.Text = _config.ServerUrl;
        }

        if (_ipInput is not null && _portInput is not null)
        {
            _ipInput.Text = _config.TcpHost;
            _portInput.Text = _config.TcpPort.ToString(CultureInfo.InvariantCulture);
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

        var printerIp = EndpointHost(s.ActivePrinterEndpoint);
        var printerError = s.LastPrintError is not null;
        string printerLine, printerEntry;
        if (printerError)
        {
            printerLine = "打印机：连不上——请检查打印机是否开机、IP 是否正确";
            printerEntry = $"{printerIp} · 连不上";
        }
        else if (s.LastPrintUtc is not null)
        {
            printerLine = $"打印机：正常 · {Local(s.LastPrintUtc)} 出过纸（{printerIp}）";
            printerEntry = $"{printerIp} · 正常";
        }
        else
        {
            printerLine = $"打印机：还没打印过（{printerIp}）";
            printerEntry = $"{printerIp} · 还没打印过";
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

    private static string EndpointHost(string endpoint)
    {
        var colon = endpoint.IndexOf(':');
        return colon > 0 ? endpoint[..colon] : endpoint;
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
        catch (Exception)
        {
            RunOnUiThread(() => Toast.MakeText(this, "出错了，请再试一次", ToastLength.Short)?.Show());
        }
    }

    /// <summary>测试打印轮询用的作业视图（camelCase）。</summary>
    private sealed record JobStatusDto(string JobId, string Status, int TotalItems, int CompletedItems, IReadOnlyList<JobItemDto>? Items);

    private sealed record JobItemDto(int Index, string Status, string? ErrorCode, string? ErrorMessage);

    private static string Truncate(string text, int max = 120) => text.Length <= max ? text : text[..max] + "…";
}
