using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using LabelFrame.Api;
using LabelFrame.Core.Logs;

namespace LabelFrame.Api.Endpoints;

/// <summary>设备日志端点选项。</summary>
public sealed record LogApiOptions(SqliteLogStore Store, string InvalidRequestCode);

/// <summary>设备日志端点（Server 与 WinHost 共用）：客户端 / PDA 回传 + 查询。</summary>
public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogApi(this IEndpointRouteBuilder app, LogApiOptions options)
    {
        app.MapPost("/api/logs", async (PushLogRequest? request, CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || request.Lines is null || request.Lines.Count == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少 deviceId / lines。"));
            }

            await options.Store.AppendAsync(request.DeviceId, request.Lines, ct);
            return Results.Ok(new { received = request.Lines.Count });
        });

        app.MapGet("/api/logs", async (string? deviceId, DateTimeOffset? since, CancellationToken ct) =>
        {
            var entries = await options.Store.QueryAsync(deviceId, since, ct);
            return Results.Ok(entries.Select(e => new { e.DeviceId, Time = e.Time, e.Line }));
        });

        return app;
    }
}
