namespace LabelFrame.Core.Jobs;

/// <summary>作业整体状态。</summary>
public enum LabelJobStatus
{
    /// <summary>等待打印（含恢复后）。</summary>
    Pending,

    /// <summary>正在打印（有 Item 在途）。</summary>
    Printing,

    /// <summary>挂起（如传输失败后等待人工恢复）。</summary>
    Suspended,

    /// <summary>全部 Item 打印完成。</summary>
    Completed,

    /// <summary>已取消。</summary>
    Cancelled,

    /// <summary>已结束且存在失败 Item（无剩余可打 Item）。</summary>
    Failed,
}