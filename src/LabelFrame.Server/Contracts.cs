using LabelFrame.Api;

namespace LabelFrame.Server;

// 通用契约（SubmitJobRequest / TemplateDto / LabelDto / ErrorView / 模板与日志 DTO）在 LabelFrame.Api 共享库，
// 本文件只保留服务端专属类型（设备 / 作业视图 / 领取与回报）。

/// <summary>设备注册请求。</summary>
public sealed record RegisterDeviceRequest(string? DeviceId, string? Name);

/// <summary>设备视图。</summary>
public sealed record DeviceView(
    string DeviceId,
    string Name,
    DateTimeOffset RegisteredAt,
    DateTimeOffset LastSeenAt,
    string Status,
    string? LastIp = null);

/// <summary>作业视图。</summary>
public sealed record ServerJobView(
    string JobId,
    string RequestId,
    string TargetDeviceId,
    string Status,
    DateTimeOffset CreatedAt,
    int TotalItems,
    int CompletedItems,
    int FailedItems,
    string? ErrorMessage,
    string DeviceStatus);

/// <summary>设备领取到的作业（含载荷）。</summary>
public sealed record ClaimedJob(string JobId, string RequestId, int TotalItems, JobPayload Payload);

/// <summary>作业载荷：模板 + labels。</summary>
public sealed record JobPayload(TemplateDto Template, IReadOnlyList<LabelDto> Labels);

/// <summary>设备回报结果（POST 体）。</summary>
public sealed record ReportResultRequest(string? Status, int? CompletedItems, int? FailedItems, string? ErrorMessage);
