using LabelFrame.Core.Documents;

namespace LabelFrame.Core.Encoding;

/// <summary>ZPL 编码器：LabelDocument → ZPL 指令。</summary>
public interface IZplEncoder
{
    /// <summary>将标签文档编码为 ZPL 指令。</summary>
    /// <param name="document">标签文档。</param>
    /// <param name="dpi">打印机分辨率（默认 <see cref="ZplEncoder.DefaultDpi"/>）。</param>
    /// <returns>ZPL 指令文本。</returns>
    string Encode(LabelDocument document, int dpi = ZplEncoder.DefaultDpi);

    /// <summary>整版位图编码（图片打印模式）：LabelBitmap → ^GF 整图。</summary>
    string EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi = ZplEncoder.DefaultDpi);
}