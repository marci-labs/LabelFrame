using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using LabelFrame.AndroidHost.Api;
using LabelFrame.AndroidHost.Rendering;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 前台打印宿主服务：本地 HTTP + 打印 Worker + Server 路由轮询。
/// 前台服务常驻，开机由 BootReceiver 拉起；运行状态经 <see cref="HostStatus"/> 快照供配置页 / 状态页读取。
/// </summary>
[Service(Exported = true, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class PrintHostService : Service
{
    private const string ChannelId = "labelframe_host";
    private const int NotificationId = 1001;

    private LabelJobQueue? _queue;
    private Tcp9100PrintTransport? _transport;
    private EmbeddedHttpServer? _http;
    private ServerPoller? _poller;
    private CancellationTokenSource? _cts;
    private Task? _printLoop;
    private Task? _routingLoop;
    private Task? _notifyLoop;

    /// <inheritdoc />
    public override void OnCreate()
    {
        base.OnCreate();
        StartForegroundCompat();

        // SQLitePCLRaw 的 e_sqlite3 原生库需先经 System.loadLibrary 装载进 Android 链接器命名空间，
        // 否则 raw.SetProvider 的 DllImport("e_sqlite3") 抛 DllNotFoundException（APK 内已含 libe_sqlite3.so 也不行）。
        Java.Lang.JavaSystem.LoadLibrary("e_sqlite3");

        var config = LabelHostConfig.Load(this);
        HostStatus.NoteServiceStarted(config.ServerUrl, $"{config.TcpHost}:{config.TcpPort}");
        var store = new SqliteLabelJobStore(config.DatabasePath);
        store.InitializeAsync().GetAwaiter().GetResult();
        _queue = new LabelJobQueue(store);
        _transport = new Tcp9100PrintTransport(config.TcpHost, config.TcpPort);
        var submission = new SubmissionService(_queue, LabelHostConfig.Dpi);

        _cts = new CancellationTokenSource();
        _http = new EmbeddedHttpServer(LabelHostConfig.LocalPort, submission, _queue, store, _transport, this);
        _http.Start();

        _printLoop = Task.Run(() => PrintLoopAsync(_cts.Token));
        _notifyLoop = Task.Run(() => NotificationLoopAsync(_cts.Token));
        if (!string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            _poller = new ServerPoller(config.ServerUrl, config.DeviceId, config.DeviceName);
            _routingLoop = Task.Run(() => RoutingLoopAsync(_poller, submission, _queue, _cts.Token));
        }
    }

    /// <inheritdoc />
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        => StartCommandResult.Sticky;

    /// <inheritdoc />
    public override void OnDestroy()
    {
        _cts?.Cancel();
        _http?.Dispose();
        _poller?.Dispose();
        _cts?.Dispose();
        HostStatus.NoteServiceStopped();
        base.OnDestroy();
    }

    /// <inheritdoc />
    public override IBinder? OnBind(Intent? intent) => null;

    private void StartForegroundCompat()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "LabelFrame 打印服务", NotificationImportance.Low);
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
            var notification = BuildNotification(BuildStatusText());
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(NotificationId, notification, Android.Content.PM.ForegroundService.TypeDataSync);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        else
        {
            StartForeground(NotificationId, new Notification.Builder(this).Build());
        }
    }

    /// <summary>常驻通知：点击打开配置页（MainActivity），文案随运行状态刷新。</summary>
    private Notification BuildNotification(string text)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        var pending = PendingIntent.GetActivity(
            this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
        return builder
            .SetContentTitle("LabelFrame 标签打印")
            .SetContentText(text)
            .SetSmallIcon(Resource.Drawable.ic_stat_labelframe)
            .SetOngoing(true)
            .SetContentIntent(pending)
            .Build();
    }

    /// <summary>通知文案（面向仓库用户的人话）：服务器状态 + 打印机地址，一句话说完。</summary>
    private static string BuildStatusText()
    {
        var s = HostStatus.Current;
        string server;
        if (s.ActiveServerUrl.Length == 0)
        {
            server = "服务器：未设置";
        }
        else if (s.LastServerError is not null)
        {
            server = "服务器：连不上（自动重试中）";
        }
        else if (s.LastServerContactUtc is not null)
        {
            server = "服务器：已连接";
        }
        else
        {
            server = "服务器：正在连接…";
        }

        var printerIp = s.ActivePrinterEndpoint.Contains(':', StringComparison.Ordinal)
            ? s.ActivePrinterEndpoint[..s.ActivePrinterEndpoint.IndexOf(':')]
            : s.ActivePrinterEndpoint;
        var printer = s.LastPrintError is not null
            ? $"打印机 {printerIp} · 连不上"
            : $"打印机 {printerIp}";

        return $"{server} · {printer}";
    }

    /// <summary>通知文案刷新循环：状态文本变化时才重发通知（5s 一帧）。</summary>
    private async Task NotificationLoopAsync(CancellationToken cancellationToken)
    {
        var lastText = BuildStatusText();
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                var text = BuildStatusText();
                if (text != lastText)
                {
                    lastText = text;
                    manager?.Notify(NotificationId, BuildNotification(text));
                }
            }
            catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 通知刷新失败不影响服务
            }
        }
    }

    private async Task PrintLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _queue!.MarkInterruptedJobsSuspendedAsync(cancellationToken);
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // 忽略启动恢复异常
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var next = await _queue!.ClaimNextItemAsync(cancellationToken);
                if (next is null)
                {
                    await Task.Delay(200, cancellationToken);
                    continue;
                }

                try
                {
                    await _transport!.SendAsync(next.Value.Item.Zpl, cancellationToken);
                    await _queue.CompleteItemAsync(next.Value.JobId, next.Value.Item.Id, cancellationToken);
                    HostStatus.NotePrintSuccess();
                }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    HostStatus.NotePrintError(ex.Message);
                    await _queue.FailItemAsync(
                        next.Value.JobId,
                        next.Value.Item.Id,
                        JobErrorCodes.TransportSendFailed,
                        $"发送失败：{ex.Message}",
                        cancellationToken);
                }
            }
            catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private static async Task RoutingLoopAsync(
        ServerPoller poller,
        SubmissionService submission,
        LabelJobQueue queue,
        CancellationToken cancellationToken)
    {
        // 与 WinHost ServerRoutingWorker 同构：主循环负责注册 / 长轮询 / 领取 / 投入队列，
        // 回报循环每 1s 检查本地作业——终态立即回报、进行中节流上报进度（决策 #101，计数有变化才发）。
        var localToServer = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var lastReported = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Completed, int Failed)>();
        var interval = TimeSpan.FromSeconds(Math.Max(1, LabelHostConfig.PollIntervalSeconds));
        var notifyTimeout = TimeSpan.FromSeconds(20);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reportLoop = ReportFinishedLoopAsync(poller, localToServer, lastReported, queue, linked.Token);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await poller.RegisterAsync(cancellationToken);
                    HostStatus.NoteServerContact();
                    // 长轮询等待通知：作业到达立即返回，随后立刻领取（等效推送）；超时也照常领取一次兜底
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var signaled = await poller.WaitForJobAsync(notifyTimeout, cancellationToken);
                        var jobs = await poller.FetchPendingAsync(cancellationToken);
                        HostStatus.NoteServerContact();
                        foreach (var job in jobs)
                        {
                            var result = await submission.SubmitAsync(
                                new SubmitJobRequest(job.RequestId, job.Template, job.Labels),
                                cancellationToken);
                            if (result.Job is not null)
                            {
                                // 幂等重放：同一 requestId 可能返回既有本地作业
                                localToServer[result.Job.Id] = job.JobId;
                            }
                            else
                            {
                                await poller.ReportResultAsync(
                                    job.JobId,
                                    new JobResult("Failed", 0, job.TotalItems, result.ErrorMessage),
                                    cancellationToken);
                            }
                        }

                        if (!signaled)
                        {
                            // 超时：继续下一轮等待（连续挂起，设备在线由 notify 端点心跳维持）
                            continue;
                        }
                    }
                }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    HostStatus.NoteServerError(ex.Message);
                    // 网络异常下一轮重试
                    try
                    {
                        await Task.Delay(interval, cancellationToken);
                    }
                    catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            linked.Cancel();
            try
            {
                await reportLoop;
            }
            catch (System.OperationCanceledException)
            {
                // 取消路径：回报循环已结束
            }
        }
    }

    /// <summary>回报 / 进度循环：周期检查本地作业——终态回报结果，进行中节流上报进度（计数有变化才发）。</summary>
    private static async Task ReportFinishedLoopAsync(
        ServerPoller poller,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> localToServer,
        System.Collections.Concurrent.ConcurrentDictionary<string, (int Completed, int Failed)> lastReported,
        LabelJobQueue queue,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (var (localJobId, serverJobId) in localToServer.ToList())
                {
                    var local = await queue.GetAsync(localJobId, cancellationToken);
                    if (local is null)
                    {
                        continue;
                    }

                    var completed = local.Items.Count(i => i.Status == LabelJobItemStatus.Completed);
                    var failed = local.Items.Count(i => i.Status is LabelJobItemStatus.Failed or LabelJobItemStatus.Cancelled);

                    if (local.Status is LabelJobStatus.Completed or LabelJobStatus.Failed or LabelJobStatus.Cancelled)
                    {
                        await poller.ReportResultAsync(
                            serverJobId,
                            new JobResult(
                                local.Status == LabelJobStatus.Completed ? "Completed" : "Failed",
                                completed,
                                failed,
                                local.Items.FirstOrDefault(i => i.ErrorMessage is not null)?.ErrorMessage),
                            cancellationToken);
                        localToServer.TryRemove(localJobId, out _);
                        lastReported.TryRemove(localJobId, out _);
                        continue;
                    }

                    // 打印中：计数有变化才上报（回报循环 1s 周期即节流间隔）；
                    // 失败静默降级——Server 不可达不阻塞打印主链路，下一轮携带最新计数自然重试
                    if (lastReported.GetOrAdd(localJobId, (0, 0)) == (completed, failed))
                    {
                        continue;
                    }

                    try
                    {
                        await poller.ReportProgressAsync(serverJobId, new JobProgress(completed, failed), cancellationToken);
                        lastReported[localJobId] = (completed, failed);
                    }
                    catch
                    {
                        // 进度上报失败：不重试风暴、不翻转通知状态，下一轮重试
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 回报异常下一轮重试
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
