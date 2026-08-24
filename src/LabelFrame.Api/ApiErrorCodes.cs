namespace LabelFrame.Api;

/// <summary>
/// API 问题码注册表（Server / WinHost 共用端点的语义化错误码）。
/// 约定：LF_TPL_xxx（模板）/ LF_TRANSPORT_xxx（连接）/ LF_PLUGIN_xxx（插件）；
/// 通用请求 / 作业 / IO 错误沿用 Core 的 LF_API_xxx / LF_JOB_xxx / LF_IO_xxx，服务端专属错误沿用 LF_SRV_xxx。
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>模板不存在（宿主侧模板库查询失败；服务端沿用 LF_SRV_006）。</summary>
    public const string TemplateNotFound = "LF_TPL_001";

    /// <summary>连接配置无效（pluginId / 参数校验失败）。</summary>
    public const string TransportInvalid = "LF_TRANSPORT_INVALID";

    /// <summary>插件包无效（zip / manifest / 预检校验失败）。</summary>
    public const string PluginInvalid = "LF_PLUGIN_INVALID";

    /// <summary>插件文件被占用（卸载 / 覆盖需重启客户端）。</summary>
    public const string PluginBusy = "LF_PLUGIN_BUSY";

    /// <summary>插件安装失败（解压 / 写入等 IO 异常）。</summary>
    public const string PluginInstallFailed = "LF_PLUGIN_INSTALL_FAILED";
}
