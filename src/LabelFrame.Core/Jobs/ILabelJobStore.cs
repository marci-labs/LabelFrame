namespace LabelFrame.Core.Jobs;

/// <summary>作业存储：负责作业与 Item 的持久化（SQLite 实现）。</summary>
public interface ILabelJobStore
{
    /// <summary>初始化（建表）；可在服务启动时调用。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建作业；若 RequestId 已存在则返回已有作业（幂等）。
    /// </summary>
    Task<LabelJob> CreateJobAsync(LabelJob job, CancellationToken cancellationToken = default);

    /// <summary>按作业标识查询。</summary>
    Task<LabelJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>按幂等键查询。</summary>
    Task<LabelJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>更新作业状态并返回最新作业；作业不存在返回 null。</summary>
    Task<LabelJob?> SetJobStatusAsync(string jobId, LabelJobStatus status, CancellationToken cancellationToken = default);

    /// <summary>更新 Item 状态并返回最新作业；作业不存在返回 null。</summary>
    Task<LabelJob?> SetItemStatusAsync(
        string jobId,
        string itemId,
        LabelJobItemStatus status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>按作业状态列出（含 Item）。</summary>
    Task<IReadOnlyList<LabelJob>> ListJobsByStatusAsync(LabelJobStatus status, CancellationToken cancellationToken = default);
}