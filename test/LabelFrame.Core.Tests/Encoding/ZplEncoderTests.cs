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
    public void Qr_code_element_should_encode_bq()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "qr",
                ContractName = "qr",
                ContractVersion = "1.0",
                WidthMm = 30,
                HeightMm = 30,
                Elements =
                [
                    new LabelQrCodeElement { SourceKey = "text", XMm = 5, YMm = 5, SizeMm = 20 },
                ],
            },
            Data = new Dictionary<string, string> { ["text"] = "LABELFRAME-001" },
        };

        var zpl = new ZplEncoder().Encode(document);

        // 20mm @203dpi = 160 点；mag = 160/24 ≈ 7
        Assert.Contains("^FO40,40^BQN,2,7^FDQA,LABELFRAME-001^FS", zpl);
    }

    [Fact]
    public void Line_element_should_encode_gb_line()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "line",
                ContractName = "line",
                ContractVersion = "1.0",
                WidthMm = 20,
                HeightMm = 20,
                Elements =
                [
                    new LabelLineElement { XMm = 5, YMm = 5, X2Mm = 15, Y2Mm = 5, ThicknessMm = 0.5 },
                ],
            },
            Data = new Dictionary<string, string>(),
        };

        var zpl = new ZplEncoder().Encode(document);

        Assert.Contains("^FO40,40^GB80,0,4,L,0^FS", zpl);
    }

    [Fact]
    public void Region_should_encode_border_and_center_children()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "region",
                ContractName = "region",
                ContractVersion = "1.0",
                WidthMm = 100,
                HeightMm = 60,
                Elements =
                [
                    new LabelRegionElement { Id = "top", XMm = 5, YMm = 5, WidthMm = 90, HeightMm = 50, BorderMm = 0.5 },
                    new LabelTextElement
                    {
                        SourceKey = "locationCode",
                        XMm = 5,
                        YMm = 5,
                        FontHeightMm = 5,
                        FontWidthMm = 5,
                        TextAlign = LabelTextAlign.Center,
                        RegionId = "top",
                    },
                    new LabelBarcodeElement
                    {
                        SourceKey = "locationCode",
                        XMm = 5,
                        YMm = 5,
                        HeightMm = 20,
                        ModuleWidth = 2,
                        RegionId = "top",
                    },
                ],
            },
            Data = new Dictionary<string, string> { ["locationCode"] = "A-01-02-03" },
        };

        var zpl = new ZplEncoder().Encode(document);

        Assert.Contains("^FO40,40^GB719,400,4,B,0^FS", zpl);
        // 文本：区域居中，块宽 = 区域宽 90mm = 720 点，justify=1（居中）
        Assert.Contains("^FO40,220^A0N,40,40^FB719,1,0,1^FDA-01-02-03^FS", zpl);
        // 条码：宽 = 20*2.5=50mm，水平居中 x=25mm=200；垂直居中 y=20mm=160
        Assert.Contains("^FO200,160^BY2,3^BCN,160,Y,N,N^FDA-01-02-03^FS", zpl);
    }

    [Fact]
    public void Text_with_width_align_border_and_padding_should_encode_fb_and_box()
    {
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "style",
                ContractName = "style",
                ContractVersion = "1.0",
                WidthMm = 100,
                HeightMm = 60,
                Elements =
                [
                    new LabelTextElement
                    {
                        SourceKey = "zone",
                        XMm = 5,
                        YMm = 14,
                        FontHeightMm = 8,
                        FontWidthMm = 8,
                        WidthMm = 40,
                        TextAlign = LabelTextAlign.Center,
                        PaddingMm = 1,
                        BorderMm = 0.3,
                    },
                ],
            },
            Data = new Dictionary<string, string> { ["zone"] = "A-01" },
        };

        var zpl = new ZplEncoder().Encode(document);

        // 边框盒：x=40,y=112,宽 320+16=336,高 64+16=80,线宽 2 点
        Assert.Contains("^FO40,112^GB336,80,2,B,0^FS", zpl);
        // 文本：x+padding=48,y+padding=120，^FB 320 点 justify=1
        Assert.Contains("^FO48,120^A0N,64,64^FB320,1,0,1^FDA-01^FS", zpl);
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