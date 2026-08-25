using BenchmarkDotNet.Attributes;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using LabelFrame.Core.Contracts;

namespace LabelFrame.Benchmarks;

/// <summary>
/// 批量规模伸缩基准：一次作业 N 张（N = 1 / 2 / 50 / 200），测「渲染+编码全部 N 张 → 入队」总耗时与分配。
/// 场景对齐 REQUIREMENTS：出库拆分 1-2 张、库位码 50 张、压力验证 100+ 张。
/// 元素复杂度两档：简（单条码）vs 密（文本 + 条码 + 二维码，同 PrintPipelineBenchmarks）。
/// </summary>
[MemoryDiagnoser]
public class BatchScalingBenchmarks
{
    private readonly SkiaLabelRenderer _renderer = new();
    private readonly ZplImageEncoder _encoder = new();
    private string _dbPath = null!;

    [Params(1, 2, 50, 200)]
    public int Labels { get; set; }

    [Params(LabelComplexity.Simple, LabelComplexity.Dense)]
    public LabelComplexity Complexity { get; set; }

    public enum LabelComplexity { Simple, Dense }

    [GlobalSetup]
    public void Setup() => _dbPath = Path.Combine(Path.GetTempPath(), $"lfbench-{Guid.NewGuid():N}.db");

    [GlobalCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) { File.Delete(_dbPath); } } catch (IOException) { }
    }

    [Benchmark(Description = "N 张：渲染+编码+入队")]
    public async Task<object> BatchPipeline()
    {
        var store = new SqliteLabelJobStore(_dbPath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);

        var zpl = new List<string>(Labels);
        for (var i = 0; i < Labels; i++)
        {
            var document = BuildDocument(Complexity, i);
            var bitmap = _renderer.RenderLabelBitmap(document, 203);
            zpl.Add(_encoder.EncodeImage(bitmap, 100, 60, 203));
        }

        var (job, _) = await queue.SubmitAsync($"bench-{Guid.NewGuid():N}", zpl);
        return job.Id;
    }

    private static LabelDocument BuildDocument(LabelComplexity complexity, int index) => complexity switch
    {
        LabelComplexity.Simple => new LabelDocument
        {
            Layout = SimpleLayout,
            Data = new Dictionary<string, string> { ["code"] = $"LF-{index:D4}" },
        },
        _ => new LabelDocument
        {
            Layout = DenseLayout,
            Data = new Dictionary<string, string> { ["code"] = $"LF-{index:D4}" },
        },
    };

    private static LabelContract Contract { get; } = new()
    {
        Name = "bench", Version = "1.0",
        Fields = [new LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
    };

    private static LabelLayout SimpleLayout { get; } = new()
    {
        Name = "s", ContractName = "bench", ContractVersion = "1.0", WidthMm = 100, HeightMm = 60,
        Elements = [new LabelBarcodeElement { SourceKey = "code", XMm = 5, YMm = 5, HeightMm = 15, DisplayValue = true }],
    };

    private static LabelLayout DenseLayout { get; } = new()
    {
        Name = "d", ContractName = "bench", ContractVersion = "1.0", WidthMm = 100, HeightMm = 60,
        Elements =
        [
            new LabelTextElement { Literal = "LabelFrame 批量基准", XMm = 2, YMm = 2, FontHeightMm = 4, Bold = true },
            new LabelBarcodeElement { SourceKey = "code", XMm = 2, YMm = 14, HeightMm = 12, DisplayValue = true },
            new LabelQrCodeElement { SourceKey = "code", XMm = 75, YMm = 2, SizeMm = 18 },
            new LabelLineElement { XMm = 2, YMm = 32, X2Mm = 98, Y2Mm = 32 },
        ],
    };
}
