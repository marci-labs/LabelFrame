using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LabelFrame.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Rendering;
using LabelFrame.TransportPlugin.Tspl;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Perf;

public class ImagePipelineWithoutPngTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Compare_image_pipeline_with_and_without_png_using_capture_transport()
    {
        var expected = new Dictionary<int, string[]>();
        var round = 0;
        foreach (var png in new[] { true, false, false, true })
        {
            round++;
            var root = Path.Combine(Path.GetTempPath(), $"lf-no-png-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var store = new SqliteLabelJobStore(Path.Combine(root, "jobs.db"));
            await store.InitializeAsync();
            using var queue = new LabelJobQueue(store);
            var templates = new TemplateStore(Path.Combine(root, "templates.db"));
            await templates.InitializeAsync();
            var renderer = new TimedRenderer();
            var config = new TransportConfig { PluginId = png ? "log" : "tcp9100" };
            config.SyncLegacyFields();
            var capture = new CaptureTransport();
            var manager = new CaptureManager(config, capture);
            var service = new JobSubmissionService(queue, new ZplImageEncoder(), () => 300, renderer, templates,
                manager, new TransportPluginRegistry(), new TransportPluginContext(TextWriter.Null, root),
                TextWriter.Null, Path.Combine(root, "print"));
            using var worker = new JobPrintWorker(queue, manager, NullLogger<JobPrintWorker>.Instance, new PrintSettings());
            try
            {
                await worker.StartAsync(CancellationToken.None);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                foreach (var count in new[] { 1, 50, 200 })
                {
                    var request = Request($"round-{round}-{count}", count);
                    var sendsBefore = capture.Hashes.Count;
                    renderer.Ticks = 0;
                    var watch = Stopwatch.StartNew();
                    var result = await service.SubmitAsync(request, timeout.Token);
                    var submitMs = watch.Elapsed.TotalMilliseconds;
                    Assert.Null(result.ErrorCode);
                    Assert.True(result.Created);
                    LabelJobState? state;
                    do
                    {
                        state = await queue.GetStateAsync(result.Job!.Id, timeout.Token);
                        if (state!.Status == LabelJobStatus.Completed) break;
                        Assert.DoesNotContain(state.Status, new[] { LabelJobStatus.Failed, LabelJobStatus.Cancelled });
                        await Task.Delay(5, timeout.Token);
                    } while (true);
                    var completedMs = watch.Elapsed.TotalMilliseconds;
                    Assert.All(state.Items, i => Assert.Equal(LabelJobItemStatus.Completed, i.Status));
                    var hashes = capture.Hashes.ToArray().Skip(sendsBefore).ToArray();
                    Assert.Equal(count, hashes.Length);
                    Assert.Equal(result.Job!.Items.Select(i => CaptureTransport.Hash(i.Zpl)), hashes);
                    if (expected.TryGetValue(count, out var original)) Assert.Equal(original, hashes);
                    else expected[count] = hashes;
                    var directory = Path.Combine(root, "print", result.Job.Id);
                    var pngCount = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.png").Length : 0;
                    Assert.Equal(png ? count : 0, pngCount);
                    output.WriteLine($"round={round}; png={png}; count={count}; submitMs={submitMs:F2}; completedMs={completedMs:F2}; renderMs={renderer.Ticks * 1000d / Stopwatch.Frequency:F2}; sends={hashes.Length}; pngCount={pngCount}");
                    var replay = await service.SubmitAsync(request, timeout.Token);
                    Assert.False(replay.Created);
                    Assert.Equal(result.Job.Id, replay.Job!.Id);
                    Assert.Null(await queue.ClaimNextItemAsync(timeout.Token));
                    Assert.Equal(sendsBefore + count, capture.Hashes.Count);
                }
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Characterize_tspl_legacy_mode_and_extra_png_without_sending()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lf-tspl-png-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var registry = new TransportPluginRegistry();
            registry.Register(new TsplTransportPlugin());
            var context = new TransportPluginContext(TextWriter.Null, root);
            var configPath = Path.Combine(root, "connection.json");
            File.WriteAllText(configPath, """{"pluginId":"labelframe-transport-tspl","params":{"kind":"Tcp","host":"127.0.0.1","port":"9"}}""");
            var manager = new TransportManager(registry, context, new HostOptions(), TextWriter.Null, configPath);
            var store = new SqliteLabelJobStore(Path.Combine(root, "jobs.db"));
            await store.InitializeAsync();
            using var queue = new LabelJobQueue(store);
            var templates = new TemplateStore(Path.Combine(root, "templates.db"));
            await templates.InitializeAsync();
            var service = new JobSubmissionService(queue, new ZplImageEncoder(), () => 300,
                new SkiaLabelRenderer(), templates, manager, registry, context, TextWriter.Null, Path.Combine(root, "print"));
            var result = await service.SubmitAsync(Request("tspl-characterization", 3));
            Assert.NotNull(result.Job);
            Assert.Equal(TransportPluginIdPolicy.TsplPluginId, manager.CurrentConfig.PluginId);
            Assert.All(result.Job.Items, item => Assert.NotEmpty(item.CommandBytes!));
            var files = Directory.GetFiles(Path.Combine(root, "print", result.Job.Id), "*.png");
            Assert.Equal(3, files.Length);
            Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
            output.WriteLine($"plugin={manager.CurrentConfig.PluginId}; legacyMode={manager.CurrentConfig.Mode}; binaryItems={result.Job.Items.Count}; pngCount={files.Length}; transportSends=0 (Worker 未启动)");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static SubmitJobRequest Request(string id, int count) => new(id,
        new TemplateDto(new LabelContract
        {
            Name = "perf", Version = "1.0", Fields =
            [new LabelField { Key = "code", DisplayName = "编码", IsRequired = true }, new LabelField { Key = "text", DisplayName = "文本" }],
        }, new LabelLayout
        {
            Name = "perf", ContractName = "perf", ContractVersion = "1.0", WidthMm = 100, HeightMm = 60,
            Elements =
            [
                new LabelTextElement { SourceKey = "text", XMm = 3, YMm = 3, WidthMm = 80, HeightMm = 10, FontHeightMm = 5 },
                new LabelBarcodeElement { SourceKey = "code", XMm = 3, YMm = 18, HeightMm = 22, ModuleWidth = 2 },
                new LabelQrCodeElement { SourceKey = "code", XMm = 75, YMm = 40, SizeMm = 15 },
            ],
        }), Enumerable.Range(1, count).Select(i => new LabelDto(new Dictionary<string, string>
        { ["code"] = $"LF-{i:0000}", ["text"] = $"模拟打印-{i:0000}" })).ToArray());

    private sealed class TimedRenderer : ILabelBitmapRenderer
    {
        private readonly SkiaLabelRenderer _inner = new();
        public long Ticks { get; set; }
        public LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
        {
            var start = Stopwatch.GetTimestamp();
            try { return _inner.RenderLabelBitmap(document, dpi, templateImages); }
            finally { Ticks += Stopwatch.GetTimestamp() - start; }
        }
        public byte[] RenderLabelBitmapPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
            => throw new InvalidOperationException("打印渲染不应调用预览 PNG 入口");
    }

    private sealed class CaptureTransport : IPrintTransport
    {
        public ConcurrentQueue<string> Hashes { get; } = new();
        public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(value.AsSpan())));
        public Task SendAsync(string zpl, CancellationToken cancellationToken = default)
        {
            Hashes.Enqueue(Hash(zpl));
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureManager(TransportConfig config, IPrintTransport transport) : ITransportManager
    {
        public TransportConfig CurrentConfig => config;
        public IPrintTransport CurrentTransport => transport;
        public string ConfigFilePath => string.Empty;
        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
