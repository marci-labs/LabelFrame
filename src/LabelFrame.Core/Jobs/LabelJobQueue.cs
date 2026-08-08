namespace LabelFrame.Core.Jobs;

/// <summary>
/// 作业队列：幂等提交、逐张状态、挂起 / 恢复 / 取消、批内顺序。
/// 由单个打印 Worker 调用 <see cref="ClaimNextItemAsync"/> 取下一张并按序打印。
/// </summary>
public sealed class LabelJobQueue
{
    private readonly ILabelJobStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>创建作业队列。</summary>
    public LabelJobQueue(ILabelJobStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>提交作业：requestId 已存在时返回已有作业（幂等）。</summary>
    /// <param name="requestId">幂等键。</param>
    /// <param name="zplLabels">每张标签的 ZPL（批内顺序）。</param>
    /// <returns>作业与是否新建（false 表示 requestId 重放返回已有作业）。</returns>
    public async Task<(LabelJob Job, bool Created)> SubmitAsync(string requestId, IReadOnlyList<string> zplLabels, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(zplLabels);
        if (zplLabels.Count == 0)
        {
            throw new ArgumentException("作业至少包含一张标签。", nameof(zplLabels));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _store.GetJobByRequestIdAsync(requestId, cancellationToken);
            if (existing is not null)
            {
                return (existing, Created: false);
            }

            var now = DateTimeOffset.UtcNow;
            var jobId = Guid.NewGuid().ToString("N");
            var job = new LabelJob
            {
                Id = jobId,
                RequestId = requestId,
                Status = LabelJobStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Items = zplLabels
                    .Select((zpl, index) => new LabelJobItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        JobId = jobId,
                        Index = index,
                        Status = LabelJobItemStatus.Pending,
                        Zpl = zpl,
                    })
                    .ToList(),
            };

            return (await _store.CreateJobAsync(job, cancellationToken), Created: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>按作业标识查询。</summary>
    public Task<LabelJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => _store.GetJobAsync(jobId, cancellationToken);

    /// <summary>
    /// 取下一个待打 Item：最旧 Pending 作业中序号最小的 Pending Item，
    /// 并置为 Printing；无待打作业时返回 null。
    /// </summary>
    public async Task<(string JobId, LabelJobItem Item)?> ClaimNextItemAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 一个批内可连续领取：同时扫描 Pending 与在途 Printing 作业（最旧优先）
            var activeJobs = new List<LabelJob>();
            activeJobs.AddRange(await _store.ListJobsByStatusAsync(LabelJobStatus.Pending, cancellationToken));
            activeJobs.AddRange(await _store.ListJobsByStatusAsync(LabelJobStatus.Printing, cancellationToken));
            var job = activeJobs
                .Where(j => j.Items.Any(i => i.Status == LabelJobItemStatus.Pending))
                .OrderBy(j => j.CreatedAt)
                .ThenBy(j => j.Id)
                .FirstOrDefault();
            if (job is null)
            {
                return null;
            }

            var item = job.Items
                .Where(i => i.Status == LabelJobItemStatus.Pending)
                .OrderBy(i => i.Index)
                .First();

            await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Printing, null, null, cancellationToken);
            await _store.SetJobStatusAsync(job.Id, LabelJobStatus.Printing, cancellationToken);

            var fresh = await _store.GetJobAsync(job.Id, cancellationToken);
            var freshItem = fresh!.Items.First(i => i.Id == item.Id);
            return (job.Id, freshItem);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Item 打印完成。</summary>
    public async Task<LabelJob> CompleteItemAsync(string jobId, string itemId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.SetItemStatusAsync(jobId, itemId, LabelJobItemStatus.Completed, null, null, cancellationToken)
                ?? throw new LabelJobException(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。");

            if (job.Status is LabelJobStatus.Cancelled or LabelJobStatus.Completed)
            {
                return job;
            }

            if (job.Items.All(i => i.Status == LabelJobItemStatus.Completed))
            {
                return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Completed, cancellationToken) ?? job;
            }

            if (!job.Items.Any(i => i.Status == LabelJobItemStatus.Pending) && job.Status is LabelJobStatus.Pending or LabelJobStatus.Printing)
            {
                // 无剩余 Pending 且非全部完成（存在 Failed）→ 作业结束为 Failed
                return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Failed, cancellationToken) ?? job;
            }

            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Item 发送失败：仍有未打 Item 时挂起作业，否则作业结束为 Failed。</summary>
    public async Task<LabelJob> FailItemAsync(string jobId, string itemId, string errorCode, string errorMessage, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.SetItemStatusAsync(jobId, itemId, LabelJobItemStatus.Failed, errorCode, errorMessage, cancellationToken)
                ?? throw new LabelJobException(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。");

            if (job.Status is LabelJobStatus.Cancelled or LabelJobStatus.Completed)
            {
                return job;
            }

            if (job.Items.Any(i => i.Status == LabelJobItemStatus.Pending))
            {
                return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Suspended, cancellationToken) ?? job;
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Failed, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>挂起作业（允许 Pending / Printing → Suspended）。</summary>
    public async Task<LabelJob> SuspendAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw new LabelJobException(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。");
            if (job.Status is not (LabelJobStatus.Pending or LabelJobStatus.Printing))
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, $"作业当前状态 {job.Status} 不允许挂起。");
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Suspended, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>恢复挂起作业（Suspended → Pending，且必须有未打 Item）。</summary>
    public async Task<LabelJob> ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw new LabelJobException(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。");
            if (job.Status != LabelJobStatus.Suspended)
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, $"作业当前状态 {job.Status} 不允许恢复。");
            }

            if (!job.Items.Any(i => i.Status == LabelJobItemStatus.Pending))
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, "作业没有可续打的标签，无法恢复。");
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Pending, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取消作业：剩余 Pending / Printing Item 置 Cancelled。</summary>
    public async Task<LabelJob> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw new LabelJobException(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。");
            if (job.Status is LabelJobStatus.Completed or LabelJobStatus.Cancelled or LabelJobStatus.Failed)
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, $"作业当前状态 {job.Status} 不允许取消。");
            }

            foreach (var item in job.Items.Where(i => i.Status is LabelJobItemStatus.Pending or LabelJobItemStatus.Printing))
            {
                await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Cancelled, null, null, cancellationToken);
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Cancelled, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>失败项单独重打：把指定序号的 Failed Item 重置为 Pending（迭代 6）。</summary>
    public async Task<LabelJob> RetryItemAsync(string jobId, int itemIndex, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw new LabelJobException(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。");
            if (job.Status == LabelJobStatus.Completed || job.Status == LabelJobStatus.Cancelled)
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, $"作业当前状态 {job.Status} 不允许重打。");
            }

            if (itemIndex < 0 || itemIndex >= job.Items.Count)
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, $"作业没有第 {itemIndex} 张标签。");
            }

            var item = job.Items[itemIndex];
            if (item.Status != LabelJobItemStatus.Failed)
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, $"第 {itemIndex} 张状态为 {item.Status}，仅 Failed 可重打。");
            }

            await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Pending, null, null, cancellationToken);
            if (job.Status == LabelJobStatus.Failed)
            {
                // 整批无待打且含失败项时作业为 Failed；重打后恢复可打
                await _store.SetJobStatusAsync(job.Id, LabelJobStatus.Pending, cancellationToken);
            }
            else if (job.Status == LabelJobStatus.Suspended)
            {
                // 挂起作业重打后保持挂起，由调用方决定是否恢复；若其它 Item 已在打则无需改动
            }

            return (await _store.GetJobAsync(jobId, cancellationToken))!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 服务启动时调用：把 in-flight（Printing）作业置 Suspended，
    /// 并把在途（Printing）Item 重置为 Pending，恢复后续打优先保证不漏打。
    /// </summary>
    public async Task MarkInterruptedJobsSuspendedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var interrupted = await _store.ListJobsByStatusAsync(LabelJobStatus.Printing, cancellationToken);
            foreach (var job in interrupted)
            {
                foreach (var item in job.Items.Where(i => i.Status == LabelJobItemStatus.Printing))
                {
                    await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Pending, null, null, cancellationToken);
                }

                await _store.SetJobStatusAsync(job.Id, LabelJobStatus.Suspended, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}