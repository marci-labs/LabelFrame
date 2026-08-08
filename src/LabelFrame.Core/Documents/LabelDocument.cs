using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Documents;

/// <summary>标签文档：版式 + 数据解析后的中间结果，与打印机指令无关。</summary>
public sealed class LabelDocument
{
    /// <summary>版式。</summary>
    public required LabelLayout Layout { get; init; }

    /// <summary>字段数据（契约字段键 → 值）。</summary>
    public required IReadOnlyDictionary<string, string> Data { get; init; }

    /// <summary>图片数据（图片元素键 → 单色位图），中文栅格化后由编码器输出 ^GF。</summary>
    public IReadOnlyDictionary<string, LabelBitmap> Images { get; init; } = new Dictionary<string, LabelBitmap>();
}