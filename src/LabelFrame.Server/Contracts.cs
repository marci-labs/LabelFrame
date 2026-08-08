using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Server;

/// <summary>业务提交请求（与宿主本地 API 同构，模板自包含）。</summary>
public sealed record SubmitJobRequest(
    string? RequestId,
    string? TargetDeviceId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels);

/// <summary>自包含模板。</summary>
public sealed record TemplateDto(LabelContract? Contract, LabelLayout? Layout);

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>设备注册请求。</summary>
public sealed record RegisterDeviceRequest(string? DeviceId, string? Name);

/// <summary>设备视图。</summary>
public sealed record DeviceView(string DeviceId, string Name, DateTimeOffset RegisteredAt, DateTimeOffset LastSeenAt, string Status);

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

/// <summary>错误响应。</summary>
public sealed record ErrorView(string Code, string Message);