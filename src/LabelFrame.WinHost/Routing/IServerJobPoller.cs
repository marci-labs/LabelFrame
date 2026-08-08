namespace LabelFrame.WinHost.Routing;

/// <summary>Server 路由客户端接口（便于测试替换）。</summary>
public interface IServerJobPoller
{
    /// <summary>注册设备（同时作为心跳）。</summary>
    Task RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>领取本设备的定向作业。</summary>
    Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>回报作业结果。</summary>
    Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default);
}