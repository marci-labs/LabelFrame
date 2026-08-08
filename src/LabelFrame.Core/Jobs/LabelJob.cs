namespace LabelFrame.Core.Jobs;

/// <summary>一次打印请求 = 一个作业 + N 张标签（Item），幂等键为 RequestId。</summary>
public sealed class LabelJob
{
    /// <summary>作业标识（API 返回给调用方）。</summary>
    public required string Id { get; init; }

    /// <summary>幂等键：同一 RequestId 重放返回同一作业。</summary>
    public required string RequestId { get; init; }

    /// <summary>作业整体状态。</summary>
    public LabelJobStatus Status { get; set; } = LabelJobStatus.Pending;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>批内 Item，按 Index 升序。</summary>
    public required IReadOnlyList<LabelJobItem> Items { get; init; }
}