using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;

namespace LabelFrame.WinHost.Routing;

/// <summary>
/// Server 路由 Worker：周期注册（心跳）→ 领取定向作业 → 投入本地作业队列打印 →
/// 本地作业终态后回报 Server。未配置 ServerUrl 时不启用。
/// </summary>
public sealed class ServerRoutingWorker : BackgroundService
{
    private readonly IServerJobPoller _poller;
    private readonly JobSubmissionService _submission;
    private readonly LabelJobQueue _queue;
    private readonly TimeSpan _interval;
    private readonly ILogger<ServerRoutingWorker> _logger;
    private readonly Dictionary<string, string> _localToServer = [];

    /// <summary>创建路由 Worker。</summary>
    public ServerRoutingWorker(
        IServerJobPoller poller,
        JobSubmissionService submission,
        LabelJobQueue queue,
        TimeSpan interval,
        ILogger<ServerRoutingWorker> logger)
    {
        _poller = poller;
        _submission = submission;
        _queue = queue;
        _interval = interval;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _poller.RegisterAsync(stoppingToken);
                var jobs = await _poller.FetchPendingAsync(stoppingToken);
                foreach (var job in jobs)
                {
                    await HandleClaimedJobAsync(job, stoppingToken);
                }

                await ReportFinishedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Server 轮询异常，稍后重试。");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleClaimedJobAsync(ServerJobPayload job, CancellationToken cancellationToken)
    {
        var request = new SubmitJobRequest(
            job.RequestId,
            new TemplateDto(job.Template.Contract, job.Template.Layout),
            job.Labels);

        var result = await _submission.SubmitAsync(request, cancellationToken);
        if (result.Job is null)
        {
            _logger.LogWarning("Server 作业 {JobId} 本地提交失败：{Code} {Message}", job.JobId, result.ErrorCode, result.ErrorMessage);
            await _poller.ReportResultAsync(
                job.JobId,
                new ServerJobResult("Failed", 0, job.TotalItems, result.ErrorMessage),
                cancellationToken);
            return;
        }

        // 幂等重放：同一 requestId 可能返回既有本地作业
        _localToServer[result.Job.Id] = job.JobId;
        _logger.LogInformation("Server 作业 {JobId} 已投入本地队列 {LocalJobId}（{Items} 张）", job.JobId, result.Job.Id, job.TotalItems);
    }

    private async Task ReportFinishedAsync(CancellationToken cancellationToken)
    {
        foreach (var (localJobId, serverJobId) in _localToServer.ToList())
        {
            var local = await _queue.GetAsync(localJobId, cancellationToken);
            if (local is null || local.Status is not (LabelJobStatus.Completed or LabelJobStatus.Failed or LabelJobStatus.Cancelled))
            {
                continue;
            }

            var status = local.Status == LabelJobStatus.Completed ? "Completed" : "Failed";
            var errorMessage = local.Items.FirstOrDefault(i => i.ErrorMessage is not null)?.ErrorMessage;
            await _poller.ReportResultAsync(
                serverJobId,
                new ServerJobResult(
                    status,
                    local.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
                    local.Items.Count(i => i.Status is LabelJobItemStatus.Failed or LabelJobItemStatus.Cancelled),
                    errorMessage),
                cancellationToken);
            _localToServer.Remove(localJobId);
            _logger.LogInformation("Server 作业 {ServerJobId} 已回报：{Status}", serverJobId, status);
        }
    }
}