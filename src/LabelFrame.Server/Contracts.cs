using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Server;

/// <summary>业务提交请求（模板可自包含，或引用服务端模板库 templateName）。</summary>
public sealed record SubmitJobRequest(
    string? RequestId,
    string? TargetDeviceId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels,
    string? TemplateName = null,
    string? TargetIp = null);

/// <summary>模板（自包含或由 templateName 解析后附带；Images 为 base64 图片资源，领取时随载荷下发）。</summary>
public sealed record TemplateDto(
    LabelContract? Contract,
    LabelLayout? Layout,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Images = null);

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

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

/// <summary>错误响应。</summary>
public sealed record ErrorView(string Code, string Message);
/// <summary>模板提交 DTO（保存到服务端模板库）。</summary>
public sealed record TemplatePackageDto(
    string? Name,
    string? Group,
    LabelContract? Contract,
    LabelLayout? Layout,
    IReadOnlyDictionary<string, string>? TestData = null);

/// <summary>预览请求。</summary>
public sealed record PreviewRequest(IReadOnlyDictionary<string, string>? Data);
/// <summary>设备日志回传请求（客户端 / PDA）。</summary>
public sealed record PushLogRequest(string? DeviceId, IReadOnlyList<string>? Lines);
