using LabelFrame.Core.Contracts;

namespace LabelFrame.Core.Validation;

/// <summary>契约数据校验：迭代 1 仅校验必填字段缺失（底线：缺数据不打半张）。</summary>
public static class LabelValidator
{
    /// <summary>校验数据是否满足契约要求。</summary>
    /// <param name="contract">契约。</param>
    /// <param name="data">字段数据。</param>
    /// <returns>校验结果；必填字段缺失时返回问题码 <see cref="LabelProblemCodes.RequiredFieldMissing"/>。</returns>
    public static LabelValidationResult Validate(LabelContract contract, IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(data);

        var problems = new List<LabelValidationProblem>();
        foreach (var field in contract.Fields)
        {
            if (!field.IsRequired)
            {
                continue;
            }

            if (!data.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                problems.Add(new LabelValidationProblem(
                    LabelProblemCodes.RequiredFieldMissing,
                    field.Key,
                    $"缺少必填字段「{field.DisplayName}」（{field.Key}）。"));
            }
        }

        return problems.Count == 0
            ? LabelValidationResult.Valid
            : new LabelValidationResult(false, problems);
    }
}