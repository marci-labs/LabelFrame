using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;

namespace LabelFrame.Core.Tests.Encoding;

public class ZplImageEncoderTests
{
    [Fact]
    public void EncodeImage_should_output_pw_ll_and_gf_hex()
    {
        var bitmap = new LabelBitmap(100, 50);
        bitmap.Pixels[0] = 0x80; // 左上角一个黑点

        var zpl = new ZplImageEncoder().EncodeImage(bitmap, 70, 50, dpi: 203);

        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
        Assert.Contains("^PW559", zpl);
        Assert.Contains("^LL400", zpl);
        Assert.Contains($"^GFA,{bitmap.RowBytes * bitmap.Height},{bitmap.RowBytes * bitmap.Height},{bitmap.RowBytes},", zpl);
        Assert.Contains("80", zpl);
    }

    [Fact]
    public void EncodeImage_should_validate_args()
    {
        var encoder = new ZplImageEncoder();
        Assert.Throws<ArgumentNullException>(() => encoder.EncodeImage(null!, 70, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.EncodeImage(new LabelBitmap(1, 1), 70, 50, dpi: 0));
    }
}