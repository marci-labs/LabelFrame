namespace LabelFrame.Core.Jobs;

/// <summary>作业领域异常：携带问题码，供 API 层映射 HTTP 状态。</summary>
public sealed class LabelJobException : Exception
{
    /// <summary>创建异常。</summary>
    public LabelJobException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>问题码（见 <see cref="JobErrorCodes"/>）。</summary>
    public string Code { get; }
}