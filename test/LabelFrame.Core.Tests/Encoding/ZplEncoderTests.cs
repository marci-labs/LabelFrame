using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Tests.Samples;

namespace LabelFrame.Core.Tests.Encoding;

public class ZplEncoderTests
{
    [Fact]
    public void Location_label_should_match_golden_zpl()
    {
        var zpl = new ZplEncoder().Encode(LocationLabelSamples.CreateDocument(), dpi: 203);

        const string expected =
            "^XA\n" +
            "^FO40,32^A0N,40,40^FDA-01^FS\n" +
            "^FO40,112^A0N,64,64^FDA-01-02-03^FS\n" +
            "^FO40,208^BY2,3^BCN,176,Y,N,N^FDA-01-02-03^FS\n" +
            "^FX image:logo placeholder (10mm x 10mm) at (80mm,4mm) iteration 2^FS\n" +
            "^XZ";
        Assert.Equal(expected, zpl, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Location_label_pipeline_should_validate_and_encode_with_barcode()
    {
        var document = LocationLabelSamples.CreateDocument();
        var validation = LabelFrame.Core.Validation.LabelValidator.Validate(LocationLabelSamples.Contract, document.Data);
        Assert.True(validation.IsValid);

        var zpl = new ZplEncoder().Encode(document, dpi: 203);

        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
        Assert.Contains("^BC", zpl);
        Assert.Contains("A-01-02-03", zpl);
    }

    [Fact]
    public void Mm_to_dots_conversion_should_use_dpi()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "conv",
                ContractName = "conv",
                ContractVersion = "1.0",
                WidthMm = 20,
                HeightMm = 10,
                Elements =
                [
                    new LabelTextElement
                    {
                        SourceKey = "text",
                        XMm = 10,
                        YMm = 5,
                        FontHeightMm = 4,
                        FontWidthMm = 4,
                    },
                ],
            },
            Data = new Dictionary<string, string> { ["text"] = "x" },
        };

        // 10mm @203dpi = 80 点；4mm = 32 点
        var zpl = new ZplEncoder().Encode(document, dpi: 203);

        Assert.Contains("^FO80,40^A0N,32,32", zpl);
    }

    [Fact]
    public void Text_with_special_chars_should_use_field_hex_escaping()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "esc",
                ContractName = "esc",
                ContractVersion = "1.0",
                WidthMm = 10,
                HeightMm = 10,
                Elements =
                [
                    new LabelTextElement { SourceKey = "text", XMm = 0, YMm = 0, FontHeightMm = 2, FontWidthMm = 2 },
                ],
            },
            Data = new Dictionary<string, string> { ["text"] = "A^B~C_D" },
        };

        var zpl = new ZplEncoder().Encode(document);

        Assert.Contains("^FH", zpl);
        Assert.Contains("^FDA_5EB_7EC_5FD^FS", zpl);
    }

    [Fact]
    public void Qr_code_element_should_throw_not_supported()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "qr",
                ContractName = "qr",
                ContractVersion = "1.0",
                WidthMm = 10,
                HeightMm = 10,
                Elements =
                [
                    new LabelQrCodeElement { SourceKey = "text", XMm = 0, YMm = 0, SizeMm = 5 },
                ],
            },
            Data = new Dictionary<string, string> { ["text"] = "x" },
        };

        var exception = Assert.Throws<NotSupportedException>(() => new ZplEncoder().Encode(document));

        Assert.Contains("迭代 2", exception.Message);
    }

    [Fact]
    public void Line_element_should_throw_not_supported()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "line",
                ContractName = "line",
                ContractVersion = "1.0",
                WidthMm = 10,
                HeightMm = 10,
                Elements =
                [
                    new LabelLineElement { XMm = 0, YMm = 0, X2Mm = 10, Y2Mm = 10, ThicknessMm = 0.5 },
                ],
            },
            Data = new Dictionary<string, string>(),
        };

        Assert.Throws<NotSupportedException>(() => new ZplEncoder().Encode(document));
    }

    [Fact]
    public void Missing_data_key_should_throw()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "missing",
                ContractName = "missing",
                ContractVersion = "1.0",
                WidthMm = 10,
                HeightMm = 10,
                Elements =
                [
                    new LabelTextElement { SourceKey = "text", XMm = 0, YMm = 0, FontHeightMm = 2, FontWidthMm = 2 },
                ],
            },
            Data = new Dictionary<string, string>(),
        };

        var exception = Assert.Throws<ArgumentException>(() => new ZplEncoder().Encode(document));

        Assert.Contains("text", exception.Message);
    }
}