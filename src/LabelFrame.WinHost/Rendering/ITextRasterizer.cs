using LabelFrame.Core.Documents;

namespace LabelFrame.WinHost.Rendering;

/// <summary>文本栅格化：把需要位图化的文本元素替换为图片元素。</summary>
public interface ITextRasterizer
{
    /// <summary>
    /// 把标签文档中的非 ASCII 文本元素栅格化为单色位图（放入 Images），
    /// 返回替换后的新文档；ASCII 文本保持原生 ZPL 文本。
    /// </summary>
    LabelDocument Rasterize(LabelDocument document, int dpi);
}