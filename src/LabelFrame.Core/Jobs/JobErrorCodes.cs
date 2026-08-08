namespace LabelFrame.Core.Jobs;

/// <summary>作业 / API / IO 问题码常量。约定：LF_JOB_xxx / LF_IO_xxx / LF_API_xxx。</summary>
public static class JobErrorCodes
{
    /// <summary>作业不存在。</summary>
    public const string JobNotFound = "LF_JOB_001";

    /// <summary>当前状态不允许该操作。</summary>
    public const string InvalidTransition = "LF_JOB_002";

    /// <summary>发送到打印机失败。</summary>
    public const string TransportSendFailed = "LF_IO_001";

    /// <summary>请求格式错误（JSON / 必填字段缺失）。</summary>
    public const string InvalidRequest = "LF_API_001";

    /// <summary>ZPL 编码失败（模板不支持的元素等）。</summary>
    public const string EncodeFailed = "LF_ENC_001";
}