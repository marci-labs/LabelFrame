using System.Globalization;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;

namespace LabelFrame.Core.Tests.Encoding;

public class ZplImageEncoderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    public void Output_should_match_original_format_for_all_pixel_values(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            foreach (var dpi in new[] { 203, 300, 600 })
            foreach (var width in new[] { 1, 7, 8, 9, 799 })
            {
                var bitmap = new LabelBitmap(width, 256);
                for (var i = 0; i < bitmap.Pixels.Length; i++) bitmap.Pixels[i] = (byte)i;
                foreach (var size in new[] { -1d, 0, 25.4, 100.125 })
                    Assert.Equal(OriginalEncode(bitmap, size, 60, dpi), new ZplImageEncoder().EncodeImage(bitmap, size, 60, dpi));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Encoding_should_allocate_only_output_and_small_header()
    {
        var bitmap = new LabelBitmap(2362, 1417);
        var encoder = new ZplImageEncoder();
        for (var i = 0; i < 5; i++) encoder.EncodeImage(bitmap, 100, 60, 600);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = encoder.EncodeImage(bitmap, 100, 60, 600);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var limit = result.Length * 2L + 4096;
        output.WriteLine($"分配 {allocated} bytes，输出 {result.Length} chars");
        Assert.True(allocated < limit, $"分配 {allocated} bytes，输出 {result.Length} chars，预算 {limit} bytes");
        Assert.Equal(OriginalEncode(bitmap, 100, 60, 600), result);
    }

    private static string OriginalEncode(LabelBitmap bitmap, double width, double height, int dpi)
    {
        static int Dots(double mm, int resolution) => Math.Max(0, (int)Math.Round(mm / 25.4 * resolution, MidpointRounding.AwayFromZero));
        var total = bitmap.RowBytes * bitmap.Height;
        var hex = Convert.ToHexString(bitmap.Pixels);
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^PW{Math.Max(1, Dots(width, dpi))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^LL{Math.Max(1, Dots(height, dpi))}");
        sb.Append(CultureInfo.InvariantCulture, $"^FO0,0^GFA,{total},{total},{bitmap.RowBytes},{hex}^FS").AppendLine();
        sb.Append("^XZ");
        return sb.ToString();
    }

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
