using System.Net;
using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using Microsoft.AspNetCore.Http;

namespace LabelFrame.WinHost.Api;

/// <summary>
/// 批次作业设置 API（迭代 24）：GET/POST /api/host/print-settings。
/// 仅回环可写（与 /api/host/config 一致）；保存即生效（更新单例 PrintSettings）。
/// </summary>
public static class PrintSettingsApi
{
    /// <summary>GET：返回当前设置（缺失 / 损坏 / 越界已在加载时 Normalize，永不返回非法值）。</summary>
    public static IResult Get(PrintSettings settings)
        => Results.Ok(settings.Snapshot());

    /// <summary>POST：仅回环可写；校验 batchSize ≥ 1、batchIntervalMs ≥ 0；保存即生效。</summary>
    public static IResult Post(IPAddress? remoteIp, PrintSettingsDto? request, PrintSettingsStore store, PrintSettings settings)
    {
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request is null)
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少批次作业设置。"));
        }

        var problem = PrintSettings.Validate(request.BatchSize, request.BatchIntervalMs);
        if (problem is not null)
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, problem));
        }

        var value = new PrintSettingsDto(request.BatchEnabled, request.BatchSize, request.BatchIntervalMs);
        store.Save(value);
        settings.Update(value);
        return Results.Ok(value);
    }
}
