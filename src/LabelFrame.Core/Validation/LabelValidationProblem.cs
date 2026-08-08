namespace LabelFrame.Core.Validation;

/// <summary>单个校验问题：问题码 + 字段键 + 人话消息。</summary>
public sealed record LabelValidationProblem(string Code, string FieldKey, string Message);