namespace LabelFrame.Core.Jobs;

/// <summary>作业内单张标签的状态。</summary>
public enum LabelJobItemStatus
{
    /// <summary>等待打印。</summary>
    Pending,

    /// <summary>正在发送到打印机。</summary>
    Printing,

    /// <summary>打印完成。</summary>
    Completed,

    /// <summary>发送失败（如打印机离线）。</summary>
    Failed,

    /// <summary>随作业取消。</summary>
    Cancelled,
}