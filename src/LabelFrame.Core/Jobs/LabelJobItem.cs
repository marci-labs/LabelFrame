namespace LabelFrame.Core.Jobs;

/// <summary>作业中的单张标签：批内顺序 + 逐张状态 + 不可变 ZPL。</summary>
public sealed class LabelJobItem
{
    /// <summary>Item 标识。</summary>
    public required string Id { get; init; }

    /// <summary>所属作业标识。</summary>
    public required string JobId { get; init; }

    /// <summary>批内序号（0 起，按序打印）。</summary>
    public int Index { get; init; }

    /// <summary>逐张状态。</summary>
    public LabelJobItemStatus Status { get; set; } = LabelJobItemStatus.Pending;

    /// <summary>编码后的打印机指令（不可变，重启后可续打且不重打）。</summary>
    public required string Zpl { get; init; }

    /// <summary>失败问题码。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>失败原因（中文）。</summary>
    public string? ErrorMessage { get; set; }
}