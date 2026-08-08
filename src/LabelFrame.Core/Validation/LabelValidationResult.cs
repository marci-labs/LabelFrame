namespace LabelFrame.Core.Validation;

/// <summary>契约数据校验结果。</summary>
public sealed record LabelValidationResult(bool IsValid, IReadOnlyList<LabelValidationProblem> Problems)
{
    /// <summary>校验通过的常量结果。</summary>
    public static LabelValidationResult Valid { get; } = new(true, Array.Empty<LabelValidationProblem>());
}