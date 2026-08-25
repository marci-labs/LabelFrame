using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 打印 Worker：串行领取作业中的下一张标签并通过传输发送；
/// 发送失败记 Failed 并由队列决定挂起 / 结束。每台打印机一次只处理一张。
/// 按「批次作业」设置节流——每发满 N 张后、下一张发送前暂停间隔（claim-then-delay），
/// 本机作业与服务端作业统一生效；批次计数内存态、跨作业全局累计、不持久化（重启清零）。
/// </summary>
public sealed class JobPrintWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);

    private readonly LabelJobQueue _queue;
    private readonly ITransportManager _transportManager;
    private readonly ILogger<JobPrintWorker> _logger;
    private readonly PrintSettings _printSettings;
    private readonly TimeProvider _time;

    /// <summary>批次节流内存计数：发送成功张数，跨作业全局累计，不持久化（服务重启清零）。</summary>
    private int _sendsSinceBatch;

    /// <summary>创建打印 Worker。</summary>
    public JobPrintWorker(LabelJobQueue queue, ITransportManager transportManager, ILogger<JobPrintWorker> logger, PrintSettings printSettings)
        : this(queue, transportManager, logger, printSettings, TimeProvider.System)
    {
    }

    /// <summary>创建打印 Worker（注入 TimeProvider：节流测试用 FakeTimeProvider 确定性推进）。</summary>
    public JobPrintWorker(LabelJobQueue queue, ITransportManager transportManager, ILogger<JobPrintWorker> logger, PrintSettings printSettings, TimeProvider timeProvider)
    {
        _queue = queue;
        _transportManager = transportManager;
        _logger = logger;
        _printSettings = printSettings;
        _time = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _queue.MarkInterruptedJobsSuspendedAsync(stoppingToken);
            _logger.LogInformation("启动完成：中断作业已恢复为挂起状态。");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时恢复中断作业失败。");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 空转先轻量探测（EXISTS）：避免每 200ms 全量加载 Pending/Printing 作业（含 ZPL 文本）
                if (!await _queue.HasPendingItemsAsync(stoppingToken))
                {
                    await Task.Delay(IdleDelay, _time, stoppingToken);
                    continue;
                }

                var next = await _queue.ClaimNextItemAsync(stoppingToken);
                if (next is null)
                {
                    // 探测与领取之间被并发领走（挂起 / 取消等）——按空转处理
                    await Task.Delay(IdleDelay, _time, stoppingToken);
                    continue;
                }

                var (jobId, item) = next.Value;

                // 批次节流（发送前暂停）：领取到下一张后、发送前，若已发送数满批次倍数则先延迟
                var settings = _printSettings.Snapshot();
                var sent = Volatile.Read(ref _sendsSinceBatch);
                if (BatchPrintPolicy.ShouldPauseBeforeSend(settings, sent))
                {
                    _logger.LogInformation("批次节流：已发送 {SentCount} 张，暂停 {IntervalMs} 毫秒后继续。", sent, settings.BatchIntervalMs);
                    await Task.Delay(TimeSpan.FromMilliseconds(settings.BatchIntervalMs), _time, stoppingToken);
                }

                _logger.LogInformation("开始打印作业 {JobId} 第 {Index} 张。", jobId, item.Index);
                try
                {
                    // 每次发送前取当前连接（切换后下一张生效；正在发送的这一张不受影响）
                    await _transportManager.CurrentTransport.SendAsync(item.Zpl, stoppingToken);
                    await _queue.CompleteItemAsync(jobId, item.Id, stoppingToken);
                    Volatile.Write(ref _sendsSinceBatch, sent + 1);
                    _logger.LogInformation("作业 {JobId} 第 {Index} 张打印完成。", jobId, item.Index);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "作业 {JobId} 第 {Index} 张发送失败。", jobId, item.Index);
                    await _queue.FailItemAsync(
                        jobId,
                        item.Id,
                        JobErrorCodes.TransportSendFailed,
                        $"发送到打印机失败：{ex.Message}",
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印 Worker 异常，稍后重试。");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
