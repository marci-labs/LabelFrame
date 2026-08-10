using LabelFrame.Core.Contracts;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;

namespace LabelFrame.WinHost.Api;

/// <summary>提交作业请求（模板自包含：契约 + 版式 + 标签数据）。</summary>
public sealed record SubmitJobRequest(
    string? RequestId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels,
    PrintMode? PrintMode = null);

/// <summary>自包含模板。</summary>
public sealed record TemplateDto(LabelContract? Contract, LabelLayout? Layout)
{
    /// <summary>模板名（可选）：Image 打印模式用于从模板库加载图片资源；Vector 模式忽略。</summary>
    public string? Name { get; init; }
}

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>作业视图（API 响应）。</summary>
public sealed record JobView(
    string JobId,
    string RequestId,
    string Status,
    int TotalItems,
    int CompletedItems,
    IReadOnlyList<JobItemView> Items);

/// <summary>单张标签视图。</summary>
public sealed record JobItemView(int Index, string Status, string? ErrorCode, string? ErrorMessage);

/// <summary>错误响应：问题码 + 中文消息（可选字段键）。</summary>
public sealed record ErrorView(string Code, string Message, string? FieldKey = null);

/// <summary>提交结果：成功带作业（含是否新建），失败带问题码。</summary>
public sealed record SubmitJobResult(LabelJob? Job, bool Created, string? ErrorCode, string? ErrorMessage, string? FieldKey)
{
    /// <summary>成功。</summary>
    public static SubmitJobResult Success(LabelJob job, bool created) => new(job, created, null, null, null);

    /// <summary>失败。</summary>
    public static SubmitJobResult Failure(string code, string message, string? fieldKey = null)
        => new(null, false, code, message, fieldKey);
}

/// <summary>模板提交 DTO（图片资源经导入/导出传输；testData 可选）。</summary>
public sealed record TemplatePackageDto(
    string? Name,
    string? Group,
    LabelContract? Contract,
    LabelLayout? Layout,
    IReadOnlyDictionary<string, string>? TestData = null);

/// <summary>预览请求。</summary>
public sealed record PreviewRequest(IReadOnlyDictionary<string, string>? Data);

/// <summary>设备日志回传请求（PDA）。</summary>
public sealed record PushLogRequest(string? DeviceId, IReadOnlyList<string>? Lines);

/// <summary>作业与视图映射。</summary>
public static class JobViews
{
    /// <summary>把作业映射为视图。</summary>
    public static JobView From(LabelJob job) => new(
        job.Id,
        job.RequestId,
        job.Status.ToString(),
        job.Items.Count,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
        job.Items.Select(i => new JobItemView(i.Index, i.Status.ToString(), i.ErrorCode, i.ErrorMessage)).ToList());
}