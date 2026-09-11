using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.WinHost.Api;

/// <summary>机器级端点：ServerUrl 配置（仅回环可写）、批次打印设置、本机服务关闭。</summary>
internal static class HostApi
{
    public static IEndpointRouteBuilder MapHostApi(this IEndpointRouteBuilder app, Action<string> hostInfo)
    {
    // ---- 机器级配置（/api/host/config，前端读写 ServerUrl；仅回环可写）----
    app.MapGet("/api/host/config", (HostOptions options) =>
        Results.Ok(new Api.HostConfigDto(options.ServerUrl ?? string.Empty, options.DeviceId, options.DeviceName, LocalIpAddresses.EnumerateIpv4())));

    app.MapPost("/api/host/config", (HttpContext context, Api.HostConfigRequest? request, HostConfigStore store, HostOptions options) =>
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ServerUrl))
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少 serverUrl。"));
        }

        var serverUrl = request.ServerUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "serverUrl 格式不正确（http://主机:端口）。"));
        }

        store.SaveServerUrl(serverUrl);
        options.ServerUrl = serverUrl;
        hostInfo($"机器级配置已更新：ServerUrl={serverUrl}");
        return Results.Ok(new Api.HostConfigDto(serverUrl, options.DeviceId, options.DeviceName, LocalIpAddresses.EnumerateIpv4()));
    });
    // ---- 批次作业设置：GET/POST /api/host/print-settings；仅回环可写；保存即生效 ----
    app.MapGet("/api/host/print-settings", (PrintSettings printSettings) =>
        Api.PrintSettingsApi.Get(printSettings));

    app.MapPost("/api/host/print-settings", (HttpContext context, PrintSettingsDto? request, PrintSettingsStore store, PrintSettings printSettings) =>
        Api.PrintSettingsApi.Post(context.Connection.RemoteIpAddress, request, store, printSettings));

    // ---- 本机服务关闭（Web UI 设置页「退出程序」用；与托盘菜单共用统一退出路径，缺陷 #58）----
    app.MapPost("/api/host/shutdown", (HttpContext context, HostExitCoordinator exit) =>
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // 200ms 缓冲让本响应先送达客户端，再进入「优雅停止 + 限时兜底强退」序列——
        // 与托盘菜单退出同一协调器（HostExitCoordinator），两条路径无强弱退差异
        _ = exit.RequestShutdownAsync("HTTP /api/host/shutdown（Web UI 设置页「退出程序」）", TimeSpan.FromMilliseconds(200));
        return Results.Ok(new { shuttingDown = true });
    });

        return app;
    }
}
