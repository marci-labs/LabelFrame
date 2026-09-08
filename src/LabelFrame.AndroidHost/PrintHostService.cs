using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using LabelFrame.AndroidHost.Api;
using LabelFrame.AndroidHost.Pc;
using LabelFrame.AndroidHost.Rendering;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 前台打印宿主服务：本地 HTTP + 打印 Worker + Server 路由轮询。
/// 前台服务常驻，开机由 BootReceiver 拉起。
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

    /// <inheritdoc />
    public override void OnCreate()
    {
        base.OnCreate();
        StartForegroundCompat();

        // SQLitePCLRaw 的 e_sqlite3 原生库需先经 System.loadLibrary 装载进 Android 链接器命名空间，
        // 否则 raw.SetProvider 的 DllImport("e_sqlite3") 抛 DllNotFoundException（APK 内已含 libe_sqlite3.so 也不行）。
        Java.Lang.JavaSystem.LoadLibrary("e_sqlite3");

        var config = LabelHostConfig.Load(this);
        var store = new SqliteLabelJobStore(config.DatabasePath);
        store.InitializeAsync().GetAwaiter().GetResult();
        _queue = new LabelJobQueue(store);
        _transport = new Tcp9100PrintTransport(config.TcpHost, LabelHostConfig.TcpPort);
        var submission = new SubmissionService(_queue, LabelHostConfig.Dpi);

        _cts = new CancellationTokenSource();
        var pcClient = string.IsNullOrWhiteSpace(config.PcHostUrl)
            ? null
            : new PcTemplateClient(config.PcHostUrl, config.DeviceId);
        _http = new EmbeddedHttpServer(LabelHostConfig.LocalPort, submission, _queue, store, _transport, this, pcClient);
        _http.Start();

        _printLoop = Task.Run(() => PrintLoopAsync(_cts.Token));
        if (!string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            _poller = new ServerPoller(config.ServerUrl, config.DeviceId);
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
        base.OnDestroy();
    }

    /// <inheritdoc />
    public override IBinder? OnBind(Intent? intent) => null;

    private void StartForegroundCompat()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "LabelFrame 打印宿主", NotificationImportance.Low);
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
            var notification = new Notification.Builder(this, ChannelId)
                .SetContentTitle("LabelFrame 打印宿主")
                .SetContentText("本地打印服务运行中")
                .SetSmallIcon(Android.Resource.Drawable.SymDefAppIcon)
                .SetOngoing(true)
                .Build();
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
                }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
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
        // 回报循环每 1s 检查本地作业终态并立即回报，避免被长轮询等待（最长 20s）阻塞。
        var localToServer = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var interval = TimeSpan.FromSeconds(Math.Max(1, LabelHostConfig.PollIntervalSeconds));
        var notifyTimeout = TimeSpan.FromSeconds(20);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reportLoop = ReportFinishedLoopAsync(poller, localToServer, queue, linked.Token);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await poller.RegisterAsync(cancellationToken);
                    // 长轮询等待通知：作业到达立即返回，随后立刻领取（等效推送）；超时也照常领取一次兜底
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var signaled = await poller.WaitForJobAsync(notifyTimeout, cancellationToken);
                        var jobs = await poller.FetchPendingAsync(cancellationToken);
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
                catch
                {
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

    /// <summary>回报循环：周期检查本地作业终态并回报 Server（不依赖长轮询）。</summary>
    private static async Task ReportFinishedLoopAsync(
        ServerPoller poller,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> localToServer,
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
                    if (local is null || local.Status is not (LabelJobStatus.Completed or LabelJobStatus.Failed or LabelJobStatus.Cancelled))
                    {
                        continue;
                    }

                    await poller.ReportResultAsync(
                        serverJobId,
                        new JobResult(
                            local.Status == LabelJobStatus.Completed ? "Completed" : "Failed",
                            local.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
                            local.Items.Count(i => i.Status is LabelJobItemStatus.Failed or LabelJobItemStatus.Cancelled),
                            local.Items.FirstOrDefault(i => i.ErrorMessage is not null)?.ErrorMessage),
                        cancellationToken);
                    localToServer.TryRemove(localJobId, out _);
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