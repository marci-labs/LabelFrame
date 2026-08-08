using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;

namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 打印 Worker：串行领取作业中的下一张标签并通过传输发送；
/// 发送失败记 Failed 并由队列决定挂起 / 结束。每台打印机一次只处理一张。
/// </summary>
public sealed class JobPrintWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);

    private readonly LabelJobQueue _queue;
    private readonly IPrintTransport _transport;
    private readonly ILogger<JobPrintWorker> _logger;

    /// <summary>创建打印 Worker。</summary>
    public JobPrintWorker(LabelJobQueue queue, IPrintTransport transport, ILogger<JobPrintWorker> logger)
    {
        _queue = queue;
        _transport = transport;
        _logger = logger;
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
                var next = await _queue.ClaimNextItemAsync(stoppingToken);
                if (next is null)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                var (jobId, item) = next.Value;
                _logger.LogInformation("开始打印作业 {JobId} 第 {Index} 张。", jobId, item.Index);
                try
                {
                    await _transport.SendAsync(item.Zpl, stoppingToken);
                    await _queue.CompleteItemAsync(jobId, item.Id, stoppingToken);
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
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}