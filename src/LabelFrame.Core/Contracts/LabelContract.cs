namespace LabelFrame.Core.Contracts;

/// <summary>标签契约：一个标签场景的字段清单，可版本化。</summary>
public sealed class LabelContract
{
    /// <summary>契约名称，如 location-label。</summary>
    public required string Name { get; init; }

    /// <summary>契约版本号。</summary>
    public required string Version { get; init; }

    /// <summary>字段清单（顺序即展示顺序）。</summary>
    public required IReadOnlyList<LabelField> Fields { get; init; }
}