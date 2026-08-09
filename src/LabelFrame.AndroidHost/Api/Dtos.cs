using LabelFrame.Core.Contracts;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelFrame.AndroidHost.Api;

/// <summary>提交作业请求（与 WinHost / Server 同构，模板自包含）。</summary>
public sealed record SubmitJobRequest(
    string? RequestId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels);

/// <summary>自包含模板。</summary>
public sealed record TemplateDto(LabelContract? Contract, LabelLayout? Layout);

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>作业视图。</summary>
public sealed record JobView(
    string JobId,
    string RequestId,
    string Status,
    int TotalItems,
    int CompletedItems,
    IReadOnlyList<JobItemView> Items);

/// <summary>单张标签视图。</summary>
public sealed record JobItemView(int Index, string Status, string? ErrorCode, string? ErrorMessage);

/// <summary>错误响应。</summary>
public sealed record ErrorView(string Code, string Message, string? FieldKey = null);

/// <summary>提交结果。</summary>
public sealed record SubmitResult(LabelJob? Job, bool Created, string? ErrorCode, string? ErrorMessage, string? FieldKey)
{
    public static SubmitResult Success(LabelJob job, bool created) => new(job, created, null, null, null);

    public static SubmitResult Failure(string code, string message, string? fieldKey = null)
        => new(null, false, code, message, fieldKey);
}

/// <summary>JSON 选项（版式元素转换器 + 枚举字符串）。</summary>
public static class HostJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };
}

/// <summary>作业视图映射。</summary>
public static class JobViews
{
    public static JobView From(LabelJob job) => new(
        job.Id,
        job.RequestId,
        job.Status.ToString(),
        job.Items.Count,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
        job.Items.Select(i => new JobItemView(i.Index, i.Status.ToString(), i.ErrorCode, i.ErrorMessage)).ToList());
}