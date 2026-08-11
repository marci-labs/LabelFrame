namespace LabelFrame.Server;

/// <summary>Server 问题码。约定：LF_SRV_xxx。</summary>
public static class ServerErrorCodes
{
    /// <summary>设备未注册。</summary>
    public const string DeviceNotFound = "LF_SRV_001";

    /// <summary>请求格式错误。</summary>
    public const string InvalidRequest = "LF_SRV_002";

    /// <summary>作业不存在。</summary>
    public const string JobNotFound = "LF_SRV_003";

    /// <summary>设备不是该作业的领取者。</summary>
    public const string NotJobOwner = "LF_SRV_004";

    /// <summary>作业状态不允许该操作。</summary>
    public const string InvalidTransition = "LF_SRV_005";

    /// <summary>模板不存在。</summary>
    public const string TemplateNotFound = "LF_SRV_006";
}