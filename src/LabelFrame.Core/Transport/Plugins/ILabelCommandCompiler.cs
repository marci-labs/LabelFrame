using LabelFrame.Core.Documents;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>文档编译能力：把标签文档编译为品牌原生指令（可选实现于 ITransportPlugin 同一插件对象）。</summary>
public interface ILabelCommandCompiler
{
    /// <summary>编译一张标签：成功返回整页自包含指令文本；预期内失败以结果返回（不抛异常）。</summary>
    Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>编译输入选项（首版 = DPI + 插件上下文；后续扩展只加字段，接口签名不动）。</summary>
public sealed record LabelCommandCompileOptions(int Dpi, ITransportPluginContext Context);

/// <summary>编译结果：成功（Command 非空）或失败（ErrorCode + ErrorMessage，FieldKey 可选）。</summary>
public sealed record LabelCommandCompileResult(
    string? Command,
    string? ErrorCode,
    string? ErrorMessage,
    string? FieldKey);
