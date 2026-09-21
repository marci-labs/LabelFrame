using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Api;

/// <summary>提交作业请求（模板自包含：契约 + 版式 + 标签数据；路由模式附加目标设备 / 模板名）。</summary>
/// <param name="CallbackUrl">终态回调地址（可选可空，决策 #154）：仅路由模式 Server 消费——作业到达终态（Completed / Failed / Expired）后异步 POST 通知；仅允许 http/https 绝对地址，提交即校验拒绝。直连模式（WinHost / AndroidHost）接受但忽略。</param>
public sealed record SubmitJobRequest(
    string? RequestId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels,
    string? TargetDeviceId = null,
    string? TemplateName = null,
    string? TargetIp = null,
    string? CallbackUrl = null);

/// <summary>自包含模板（Contract + Layout 必带；Name / Images 可选）。</summary>
public sealed record TemplateDto(
    LabelContract? Contract,
    LabelLayout? Layout,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Images = null);

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>模板提交 DTO（保存到模板库；图片资源经导入 / 导出传输，testData 可选）。</summary>
public sealed record TemplatePackageDto(
    string? Name,
    string? Group,
    LabelContract? Contract,
    LabelLayout? Layout,
    IReadOnlyDictionary<string, string>? TestData = null);

/// <summary>预览请求。</summary>
public sealed record PreviewRequest(IReadOnlyDictionary<string, string>? Data);

/// <summary>设备日志回传请求（PDA / 客户端 → 服务端；客户端本地查看用）。</summary>
public sealed record PushLogRequest(string? DeviceId, IReadOnlyList<string>? Lines);

/// <summary>Excel 模板生成请求（columns 顺序即表头，sampleRow 示例行）。</summary>
public sealed record ExcelTemplateRequest(IReadOnlyList<ExcelTemplateColumnDto>? Columns, IReadOnlyDictionary<string, string>? SampleRow);

/// <summary>Excel 模板列。</summary>
public sealed record ExcelTemplateColumnDto(string? Key, string? DisplayName);

/// <summary>错误响应：问题码 + 中文消息（可选字段键）。</summary>
public sealed record ErrorView(string Code, string Message, string? FieldKey = null);
