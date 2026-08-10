using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Tests.Encoding;

public class ZplEncoderBitmapTests
{
    [Fact]
    public void Image_with_bitmap_should_encode_gf()
    {
        var bitmap = new LabelBitmap(8, 4, new byte[] { 0xFF, 0x81, 0x81, 0xFF });
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "gf",
                ContractName = "gf",
                ContractVersion = "1.0",
                WidthMm = 20,
                HeightMm = 10,
                Elements =
                [
                    new LabelImageElement { SourceKey = "logo", XMm = 5, YMm = 5, WidthMm = 10, HeightMm = 10 },
                ],
            },
            Data = new Dictionary<string, string>(),
            Images = new Dictionary<string, LabelBitmap> { ["logo"] = bitmap },
        };

        var zpl = new ZplEncoder().Encode(document, dpi: 203);

        // 5mm @203dpi = 40 点；8x4 位图 RowBytes=1，总字节=4
        Assert.Contains("^FO40,40^GFA,4,4,1,FF8181FF^FS", zpl);
    }

    [Fact]
    public void Image_without_bitmap_should_fall_back_to_placeholder()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "placeholder",
                ContractName = "placeholder",
                ContractVersion = "1.0",
                WidthMm = 20,
                HeightMm = 10,
                Elements =
                [
                    new LabelImageElement { SourceKey = "logo", XMm = 0, YMm = 0, WidthMm = 10, HeightMm = 10 },
                ],
            },
            Data = new Dictionary<string, string>(),
        };

        var zpl = new ZplEncoder().Encode(document, dpi: 203);

        Assert.Contains("^FX image:logo placeholder", zpl);
        Assert.DoesNotContain("^GF", zpl);
    }

    [Fact]
    public void Encode_image_should_emit_pw_ll_and_full_gf()
    {
        var bitmap = new LabelBitmap(16, 8, new byte[16]);
        var zpl = new ZplEncoder().EncodeImage(bitmap, widthMm: 70, heightMm: 50, dpi: 203);

        // 70mm / 50mm @203dpi => PW559 / LL400；16x8 位图 RowBytes=2，总字节 16
        Assert.StartsWith("^XA", zpl);
        Assert.Contains("^PW559", zpl);
        Assert.Contains("^LL400", zpl);
        Assert.Contains("^FO0,0^GFA,16,16,2,", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
    }
    [Fact]
    public void Bitmap_row_bytes_should_pad_to_byte_boundary()
    {
        var bitmap = new LabelBitmap(10, 2);

        Assert.Equal(2, bitmap.RowBytes);
        Assert.Equal(4, bitmap.Pixels.Length);
    }
}