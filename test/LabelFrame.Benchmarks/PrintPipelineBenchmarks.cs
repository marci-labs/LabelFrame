using BenchmarkDotNet.Attributes;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using SkiaLabelRenderer = LabelFrame.Rendering.SkiaLabelRenderer;

namespace LabelFrame.Benchmarks;

/// <summary>
/// 打印热路径基准：每张标签 = Skia 整版渲染 → 1bpp 位图 → ^GF 编码。
/// 常见规格（60x40 / 100x60，203/300 dpi）× 单张与 50 张批量。
/// </summary>
[MemoryDiagnoser]
public class PrintPipelineBenchmarks
{
    private readonly SkiaLabelRenderer _renderer = new();
    private readonly LabelFrame.Core.Encoding.ZplImageEncoder _encoder = new();

    [Params(203, 300)]
    public int Dpi { get; set; }

    [ParamsSource(nameof(Specs))]
    public (double W, double H) Spec { get; set; }

    public static IEnumerable<(double, double)> Specs { get { yield return (60.0, 40.0); yield return (100.0, 60.0); } }

    private LabelDocument Document => new()
    {
        Layout = new LabelLayout
        {
            Name = "bench", ContractName = "c", ContractVersion = "1.0",
            WidthMm = Spec.W, HeightMm = Spec.H,
            Elements =
            [
                new LabelTextElement { Literal = "LabelFrame 基准", XMm = 2, YMm = 2, FontHeightMm = 4, Bold = true },
                new LabelBarcodeElement { SourceKey = "code", XMm = 2, YMm = 12, HeightMm = 10, DisplayValue = true },
                new LabelQrCodeElement { SourceKey = "code", XMm = Spec.W - 18, YMm = 2, SizeMm = 15 },
            ],
        },
        Data = new Dictionary<string, string> { ["code"] = "LF-BENCH-001" },
    };

    [Benchmark(Baseline = true, Description = "渲染→1bpp 位图")]
    public LabelFrame.Core.Documents.LabelBitmap Render() => _renderer.RenderLabelBitmap(Document, Dpi);

    [Benchmark(Description = "位图→^GF ZPL")]
    public object Encode() => _encoder.EncodeImage(Render(), Spec.W, Spec.H, Dpi);

    [Benchmark(Description = "整链路（单张）")]
    public object FullPipeline()
    {
        var bitmap = _renderer.RenderLabelBitmap(Document, Dpi);
        return _encoder.EncodeImage(bitmap, Spec.W, Spec.H, Dpi);
    }
}
