using LabelFrame.Core.Contracts;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;

namespace LabelFrame.WinHost.Api;

/// <summary>提交作业请求（模板自包含：契约 + 版式 + 标签数据）。</summary>
public sealed record SubmitJobRequest(
    string? RequestId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels);

/// <summary>自包含模板。</summary>
public sealed record TemplateDto(LabelContract? Contract, LabelLayout? Layout)
{
    /// <summary>模板名（可选）：本机提交时用于从本地模板库加载图片资源。</summary>
    public string? Name { get; init; }

    /// <summary>模板图片资源（base64，键 → 图片字节；路由作业由 Server 附带，本机提交可省略按 Name 加载）。</summary>
    public IReadOnlyDictionary<string, string>? Images { get; init; }
}

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>作业视图（API 响应；CreatedAt / FailedItems / ErrorMessage / TargetDeviceId 为作业历史列表列，迭代 18 B10 扩展）。</summary>
public sealed record JobView(
    string JobId,
    string RequestId,
    string Status,
    int TotalItems,
    int CompletedItems,
    IReadOnlyList<JobItemView> Items,
    string? PrintImageDir = null,
    int? PrintImageCount = null,
    DateTimeOffset? CreatedAt = null,
    int? FailedItems = null,
    string? ErrorMessage = null,
    string? TargetDeviceId = null);

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
    /// <summary>把作业映射为视图（可附带 Log 模拟打印图片目录）。</summary>
    public static JobView From(LabelJob job, string? printImageDir = null, int? printImageCount = null) => new(
        job.Id,
        job.RequestId,
        job.Status.ToString(),
        job.Items.Count,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
        job.Items.Select(i => new JobItemView(i.Index, i.Status.ToString(), i.ErrorCode, i.ErrorMessage)).ToList(),
        printImageDir,
        printImageCount,
        job.CreatedAt,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Failed),
        job.Items.FirstOrDefault(i => i.Status == LabelJobItemStatus.Failed)?.ErrorMessage,
        null);
}

/// <summary>传输插件参数规格（前端动态表单渲染用；迭代 22）。</summary>
public sealed record TransportPluginParameterDto(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? DefaultValue,
    IReadOnlyList<TransportParameterOptionDto>? Options,
    string? Hint);

/// <summary>Select 参数枚举项。</summary>
public sealed record TransportParameterOptionDto(string Value, string? Label);

/// <summary>已装配传输插件描述（GET /api/transport.availablePlugins 与 /api/transport/plugins）。</summary>
public sealed record TransportPluginDescriptorDto(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<TransportPluginParameterDto> Parameters,
    bool IsExternal = false,
    string? AssemblyPath = null);

/// <summary>连接状态（GET /api/transport 与 POST 响应共用；迭代 22：pluginId + params 字典 + displayText + availablePlugins，旧字段 mode / availableModes 保留兼容）。</summary>
public sealed record TransportConfigDto(
    string PluginId,
    string DisplayName,
    string DisplayText,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<TransportPluginDescriptorDto> AvailablePlugins,
    string Mode,
    IReadOnlyList<string> AvailableModes);

/// <summary>连接切换 / 测试请求（POST /api/transport；迭代 22：pluginId + params 字典优先，旧字段 mode + 平铺参数兼容）。</summary>
public sealed record TransportApplyRequest(
    string? PluginId,
    IReadOnlyDictionary<string, string>? Params,
    bool? TestOnly,
    string? Mode = null,
    string? TcpHost = null,
    int? TcpPort = null,
    string? PrinterName = null,
    string? ZebraKind = null,
    string? ZebraUsbName = null);

/// <summary>连接切换 / 测试响应：ok + 中文消息 + 当前生效连接（失败时 config = 未变前的连接）。</summary>
public sealed record TransportApplyResponse(bool Ok, string Message, TransportConfigDto Config);

/// <summary>Excel 模板生成请求（POST /api/import/excel-template；columns 顺序即表头，sampleRow 示例行）。</summary>
public sealed record ExcelTemplateRequest(IReadOnlyList<ExcelTemplateColumnDto>? Columns, IReadOnlyDictionary<string, string>? SampleRow);

/// <summary>Excel 模板列。</summary>
public sealed record ExcelTemplateColumnDto(string? Key, string? DisplayName);

/// <summary>机器级配置响应（GET /api/host/config；Ips 为本机枚举 IPv4，状态栏展示用）。</summary>/// <summary>机器级配置响应（GET /api/host/config；Ips 为本机枚举 IPv4，状态栏展示用）。</summary>
public sealed record HostConfigDto(string ServerUrl, string DeviceId, string DeviceName, IReadOnlyList<string>? Ips = null);

/// <summary>机器级配置请求（POST /api/host/config；仅 serverUrl 可写）。</summary>
public sealed record HostConfigRequest(string? ServerUrl);
