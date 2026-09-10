namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 批次节流策略：发送前暂停（claim-then-delay）的纯函数判定，保证可单测。
/// 语义：每发满 N 张后，下一张发送前暂停间隔；计数按作业累计（作业切换时归零，作业首张不等待）。
/// </summary>
public static class BatchPrintPolicy
{
    /// <summary>
    /// 发送前是否应暂停：已开启、且已发送数 &gt; 0、且已发送数为批次大小的整数倍。
    /// </summary>
    /// <param name="settings">当前批次设置（已 Normalize，batchSize ≥ 1）。</param>
    /// <param name="sendsCompleted">当前作业内发送成功的累计张数（作业首张前恒为 0）。</param>
    public static bool ShouldPauseBeforeSend(PrintSettingsDto settings, int sendsCompleted)
        => settings.BatchEnabled
           && settings.BatchSize > 0
           && sendsCompleted > 0
           && sendsCompleted % settings.BatchSize == 0;
}
