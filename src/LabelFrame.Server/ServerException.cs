namespace LabelFrame.Server;

/// <summary>Server 领域异常：携带问题码。</summary>
public sealed class ServerException : Exception
{
    /// <summary>创建异常。</summary>
    public ServerException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>问题码（见 <see cref="ServerErrorCodes"/>）。</summary>
    public string Code { get; }
}