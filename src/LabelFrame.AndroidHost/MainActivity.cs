using Android.App;
using Android.Content;
using Android.Content.PM;
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
/// 宿主配置页（唯一 UI）：后台服务定位下只配置「服务端地址 + 打印机通讯方式」，
/// 附带运行状态、测试连接 / 测试打印；设备号由系统唯一码自动生成只读展示。
/// </summary>
[Activity(Label = "LabelFrame 打印宿主", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
public sealed class MainActivity : Activity
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TextView _statusText = null!;
    private EditText _serverInput = null!;
    private TextView _serverTestText = null!;
    private EditText _ipInput = null!;
    private EditText _portInput = null!;
    private TextView _printTestText = null!;
    private TextView _deviceIdText = null!;
    private EditText _deviceNameInput = null!;
    private LabelHostConfig _config = null!;

    private readonly Handler _refreshHandler = new(Looper.MainLooper!);
    private bool _autoRefresh;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 1);
        }

        // 打开配置页即确保后台服务在运行（幂等；重复 Start 不会重建已运行的服务）
        var service = new Intent(this, typeof(PrintHostService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(service);
        }
        else
        {
            StartService(service);
        }

        _config = LabelHostConfig.Load(this);
        BuildUi();
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

    // ---------- 界面构建（代码布局，无资源依赖） ----------

    private void BuildUi()
    {
        var density = Resources!.DisplayMetrics!.Density;
        var pad = (int)(16 * density);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        root.SetPadding(pad, pad, pad, pad);

        var scroll = new ScrollView(this) { LayoutParameters = root.LayoutParameters };
        var content = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };

        content.AddView(Heading("LabelFrame 打印宿主", 20));
        content.AddView(Body("后台打印服务 · 配置与测试"));
        content.AddView(Spacing(12));

        // ---- 运行状态 ----
        content.AddView(SectionHeader("运行状态"));
        _statusText = Body(string.Empty);
        _statusText.SetPadding((int)(12 * density), (int)(10 * density), (int)(12 * density), (int)(10 * density));
        _statusText.SetBackgroundColor(Android.Graphics.Color.Argb(20, 0x33, 0x55, 0x88));
        content.AddView(_statusText);
        content.AddView(Spacing(6));
        content.AddView(SmallButton("刷新状态", () => RefreshStatus()));
        content.AddView(Spacing(16));

        // ---- 服务端 ----
        content.AddView(SectionHeader("服务端"));
        _serverInput = Input("http://192.168.x.x:53961（留空 = 不连接服务端）", _config.ServerUrl, InputTypes.ClassText | InputTypes.TextVariationUri);
        content.AddView(_serverInput);
        content.AddView(Spacing(6));
        var serverRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        var serverTestButton = SmallButton("测试连接", () => RunAsync(TestServerAsync), width: (int)(96 * density));
        serverRow.AddView(serverTestButton);
        _serverTestText = Body(string.Empty);
        _serverTestText.SetPadding((int)(10 * density), 0, 0, 0);
        _serverTestText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        serverRow.AddView(_serverTestText);
        content.AddView(serverRow);
        content.AddView(Spacing(16));

        // ---- 打印机 ----
        content.AddView(SectionHeader("打印机"));
        var brandRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        var brandChip = Body(" Zebra ");
        brandChip.SetPadding((int)(14 * density), (int)(6 * density), (int)(14 * density), (int)(6 * density));
        brandChip.SetTextColor(Android.Graphics.Color.White);
        brandChip.SetBackgroundColor(Android.Graphics.Color.Argb(255, 0x1D, 0x6F, 0xE0));
        brandRow.AddView(brandChip);
        var brandHint = Body("当前唯一品牌；其他品牌将经传输插件扩展");
        brandHint.SetPadding((int)(10 * density), 0, 0, 0);
        brandHint.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        brandRow.AddView(brandHint);
        content.AddView(brandRow);
        content.AddView(Spacing(10));

        var connectionGroup = new RadioGroup(this);
        var tcpRadio = new RadioButton(this)
        {
            Text = "网口 TCP",
            Checked = true,
            Enabled = false,
        };
        connectionGroup.AddView(tcpRadio);
        content.AddView(connectionGroup);
        content.AddView(Body("连接类型：网口 TCP（蓝牙等类型待插件支持后提供）"));
        content.AddView(Spacing(10));

        _ipInput = Input("打印机 IP 地址", _config.TcpHost, InputTypes.ClassText);
        content.AddView(_ipInput);
        content.AddView(Spacing(6));
        _portInput = Input("端口（默认 9100）", _config.TcpPort.ToString(CultureInfo.InvariantCulture), InputTypes.ClassNumber);
        content.AddView(_portInput);
        content.AddView(Spacing(6));
        var printRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        var printTestButton = SmallButton("测试打印", () => RunAsync(TestPrintAsync), width: (int)(96 * density));
        printRow.AddView(printTestButton);
        _printTestText = Body(string.Empty);
        _printTestText.SetPadding((int)(10 * density), 0, 0, 0);
        _printTestText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        printRow.AddView(_printTestText);
        content.AddView(printRow);
        content.AddView(Body("测试打印走完整链路（渲染 → 发送打印机 → 出纸），用于验证打印功能无误。"));
        content.AddView(Spacing(16));

        // ---- 设备信息 ----
        content.AddView(SectionHeader("设备信息"));
        var idRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        _deviceIdText = Body(_config.DeviceId);
        _deviceIdText.SetPadding(0, 0, 0, 0);
        _deviceIdText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            Gravity = GravityFlags.CenterVertical,
        };
        idRow.AddView(_deviceIdText);
        idRow.AddView(SmallButton("复制", CopyDeviceId, width: (int)(72 * density)));
        content.AddView(idRow);
        content.AddView(Body("设备号取系统唯一码自动生成，用于服务端定向投递（不可配置）。"));
        content.AddView(Spacing(6));
        _deviceNameInput = Input("设备名称（服务端目录展示，默认自动生成）", _config.DeviceName, InputTypes.ClassText);
        content.AddView(_deviceNameInput);
        content.AddView(Spacing(20));

        // ---- 保存 ----
        var saveButton = new Button(this) { Text = "保存并应用（重启宿主服务）" };
        saveButton.Click += (_, _) => RunAsync(SaveAndApplyAsync);
        content.AddView(saveButton);

        scroll.AddView(content);
        root.AddView(scroll);
        SetContentView(root);
    }

    private TextView Heading(string text, float sp)
    {
        var view = new TextView(this) { Text = text, TextSize = sp };
        view.SetTypeface(Android.Graphics.Typeface.DefaultBold!, Android.Graphics.TypefaceStyle.Bold);
        return view;
    }

    private TextView SectionHeader(string text)
    {
        var view = Heading(text, 15);
        view.SetPadding(0, 0, 0, (int)(6 * Resources!.DisplayMetrics!.Density));
        return view;
    }

    private TextView Body(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 13 };
        return view;
    }

    private EditText Input(string hint, string value, InputTypes type)
    {
        var view = new EditText(this)
        {
            Hint = hint,
            Text = value,
            InputType = type,
        };
        return view;
    }

    private Button SmallButton(string text, Action onClick, int? width = null)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.Click += (_, _) => onClick();
        if (width is not null)
        {
            button.LayoutParameters = new LinearLayout.LayoutParams(width.Value, ViewGroup.LayoutParams.WrapContent);
        }

        return button;
    }

    private View Spacing(float dp) => new View(this)
    {
        LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(dp * Resources!.DisplayMetrics!.Density)),
    };

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

    private void RefreshStatus() => _statusText.Text = FormatStatus();

    private static string FormatStatus()
    {
        var s = HostStatus.Current;
        static string Local(DateTime? utc) => utc is null ? "—" : utc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        var server = s.ActiveServerUrl.Length == 0
            ? "未配置"
            : s.LastServerError is not null
                ? $"连接失败（{s.ActiveServerUrl}）：{s.LastServerError}"
                : s.LastServerContactUtc is not null
                    ? $"已连接（{s.ActiveServerUrl}，最近通讯 {Local(s.LastServerContactUtc)}）"
                    : $"连接中（{s.ActiveServerUrl}）";

        var printer = s.LastPrintError is not null
            ? $"{s.ActivePrinterEndpoint}，最近发送失败：{s.LastPrintError}"
            : s.LastPrintUtc is not null
                ? $"{s.ActivePrinterEndpoint}，最近出纸 {Local(s.LastPrintUtc)}"
                : $"{s.ActivePrinterEndpoint}，暂无发送记录";

        return $"服务：{(s.ServiceRunning ? "运行中" : "已停止")}\n服务端：{server}\n打印机：{printer}";
    }

    // ---------- 测试动作 ----------

    /// <summary>测试连接：探测服务端 healthz。</summary>
    private async Task TestServerAsync()
    {
        var url = (_serverInput.Text?.Trim() ?? string.Empty).TrimEnd('/');
        if (url.Length == 0)
        {
            _serverTestText.Text = "未填写服务端地址。";
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _serverTestText.Text = "地址需以 http:// 开头。";
            return;
        }

        _serverTestText.Text = "测试中…";
        try
        {
            using var response = await Http.GetAsync($"{url}/healthz");
            _serverTestText.Text = response.IsSuccessStatusCode
                ? $"✓ 可达（HTTP {(int)response.StatusCode}）"
                : $"✗ 不可达（HTTP {(int)response.StatusCode}）";
        }
        catch (Exception ex)
        {
            _serverTestText.Text = $"✗ 连接失败：{ex.Message}";
        }
    }

    /// <summary>测试打印：经本地 HTTP 提交内置测试标签，轮询终态（服务端 / 打印机无需先保存配置）。</summary>
    private async Task TestPrintAsync()
    {
        _printTestText.Text = "提交测试作业…";
        try
        {
            using var response = await Http.PostAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/api/host/test-print", content: null);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _printTestText.Text = $"✗ 提交失败：{Truncate(body)}";
                return;
            }

            var job = JsonSerializer.Deserialize<JobStatusDto>(body, Json);
            if (job is null)
            {
                _printTestText.Text = "✗ 响应解析失败。";
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
                    _printTestText.Text = $"✓ 打印完成（{current.CompletedItems}/{current.TotalItems}）";
                    return;
                }

                if (current.Status is "Failed" or "Cancelled")
                {
                    var error = current.Items?.FirstOrDefault(i2 => i2.ErrorMessage is not null)?.ErrorMessage;
                    _printTestText.Text = $"✗ 打印失败：{error ?? current.Status}";
                    return;
                }

                _printTestText.Text = $"打印中…（{current.CompletedItems}/{current.TotalItems}）";
            }

            _printTestText.Text = "超时：60 秒内未到终态（可再点一次测试打印）。";
        }
        catch (Exception ex)
        {
            _printTestText.Text = $"✗ 本地服务未就绪或请求失败：{ex.Message}";
        }
    }

    private void CopyDeviceId()
    {
        var clipboard = GetSystemService(ClipboardService)?.JavaCast<Android.Content.ClipboardManager>();
        clipboard?.PrimaryClip = ClipData.NewPlainText("LabelFrame 设备号", _config.DeviceId);
        Toast.MakeText(this, "设备号已复制", ToastLength.Short)?.Show();
    }

    // ---------- 保存并应用 ----------

    private async Task SaveAndApplyAsync()
    {
        var url = (_serverInput.Text?.Trim() ?? string.Empty).TrimEnd('/');
        if (url.Length > 0 && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _statusText.Text = "保存失败：服务端地址需以 http:// 开头（或留空）。";
            return;
        }

        var ip = _ipInput.Text?.Trim() ?? string.Empty;
        if (ip.Length == 0)
        {
            _statusText.Text = "保存失败：打印机 IP 不能为空。";
            return;
        }

        if (!int.TryParse(_portInput.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            _statusText.Text = "保存失败：端口须为 1-65535 的数字。";
            return;
        }

        _config.Persist(this, url, tcpHost: ip, tcpPort: port, deviceName: _deviceNameInput.Text?.Trim());
        _statusText.Text = "已保存，正在重启宿主服务…";

        // 重启服务应用新配置（传输 / 路由实例在服务启动时创建）
        StopService(new Intent(this, typeof(PrintHostService)));
        await Task.Delay(800);
        var intent = new Intent(this, typeof(PrintHostService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(intent);
        }
        else
        {
            StartService(intent);
        }

        // 等本地 HTTP 就绪后刷新状态
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
        _deviceIdText.Text = _config.DeviceId;
        _deviceNameInput.Text = _config.DeviceName;
        RefreshStatus();
        Toast.MakeText(this, "配置已应用", ToastLength.Short)?.Show();
    }

    /// <summary>后台执行并回 UI 线程展示结果（异常兜底进状态文本）。</summary>
    private async void RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => _statusText.Text = $"操作异常：{ex.Message}");
        }
    }

    /// <summary>测试打印轮询用的作业视图（camelCase）。</summary>
    private sealed record JobStatusDto(string JobId, string Status, int TotalItems, int CompletedItems, IReadOnlyList<JobItemDto>? Items);

    private sealed record JobItemDto(int Index, string Status, string? ErrorCode, string? ErrorMessage);

    private static string Truncate(string text, int max = 120) => text.Length <= max ? text : text[..max] + "…";
}
