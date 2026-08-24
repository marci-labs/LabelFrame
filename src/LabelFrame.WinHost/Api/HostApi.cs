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

    // ---- 本机服务关闭（Web UI 设置页「退出程序」用）----
    app.MapPost("/api/host/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            hostInfo("收到关闭请求，正在停止宿主…");
            lifetime.StopApplication();
            // 托盘线程（WinForms 消息循环）可能阻止 RunAsync 自然返回，延迟后强制退出
            await Task.Delay(500);
            hostInfo("关闭完成。");
            Environment.Exit(0);
        });
        return Results.Ok(new { shuttingDown = true });
    });

        return app;
    }
}
