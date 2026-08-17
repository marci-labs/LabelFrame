using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Tests.Transport;
using LabelFrame.WinHost.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFrame.WinHost.Tests.Routing;

public class ServerRoutingWorkerTests
{
    private sealed class FakePoller : IServerJobPoller
    {
        private readonly Queue<ServerJobPayload> _pending = new();
        public List<(string JobId, ServerJobResult Result)> Reported { get; } = [];
        public int RegisterCount { get; private set; }

        /// <summary>长轮询等待时长（默认 20ms；可调大模拟服务端长时间挂起，验证回报不依赖它）。</summary>
        public TimeSpan WaitDelay { get; set; } = TimeSpan.FromMilliseconds(20);

        public void Enqueue(ServerJobPayload payload) => _pending.Enqueue(payload);

        public Task RegisterAsync(CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default)
        {
            var batch = new List<ServerJobPayload>();
            while (_pending.Count > 0)
            {
                batch.Add(_pending.Dequeue());
            }

            return Task.FromResult<IReadOnlyList<ServerJobPayload>>(batch);
        }

        public async Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_pending.Count > 0)
            {
                return true;
            }

            await Task.Delay(WaitDelay, cancellationToken);
            return false;
        }

        public Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default)
        {
            Reported.Add((jobId, result));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Worker_should_print_routed_job_and_report_result()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var submission = new JobSubmissionService(queue, new ZplImageEncoder(), dpi: 203, new SkiaLabelRenderer(), templates, TestTransportRegistry.CreateManager(new HostOptions { Transport = TransportMode.Log }), TextWriter.Null);

            var poller = new FakePoller();
            var payload = new ServerJobPayload(
                "server-job-1",
                "req-route",
                1,
                new TemplateDto(SampleContract, SampleLayout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })]);
            poller.Enqueue(payload);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = new ServerRoutingWorker(poller, submission, queue, TimeSpan.FromMilliseconds(100), NullLogger<ServerRoutingWorker>.Instance);
            var workerTask = worker.StartAsync(cts.Token);

            // 等待本地作业创建
            LabelJob? localJob = null;
            for (var i = 0; i < 100 && localJob is null; i++)
            {
                await Task.Delay(50, cts.Token);
                localJob = await store.GetJobByRequestIdAsync("req-route", cts.Token);
            }

            Assert.NotNull(localJob);

            // 模拟打印 Worker：领取并完成
            var claimed = await queue.ClaimNextItemAsync(cts.Token);
            Assert.NotNull(claimed);
            await queue.CompleteItemAsync(claimed!.Value.JobId, claimed.Value.Item.Id, cts.Token);

            // 等待回报
            for (var i = 0; i < 100 && poller.Reported.Count == 0; i++)
            {
                await Task.Delay(50, cts.Token);
            }

            var report = Assert.Single(poller.Reported);
            Assert.Equal("server-job-1", report.JobId);
            Assert.Equal("Completed", report.Result.Status);
            Assert.Equal(1, report.Result.CompletedItems);

            await worker.StopAsync(cts.Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task Worker_should_report_finished_job_without_waiting_for_long_poll()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var submission = new JobSubmissionService(queue, new ZplImageEncoder(), dpi: 203, new SkiaLabelRenderer(), templates, TestTransportRegistry.CreateManager(new HostOptions { Transport = TransportMode.Log }), TextWriter.Null);

            var poller = new FakePoller
            {
                // 模拟长轮询长时间挂起：完成回报必须由独立循环送达，不能等长轮询返回
                WaitDelay = TimeSpan.FromSeconds(10),
            };
            var payload = new ServerJobPayload(
                "server-job-2",
                "req-route-fast",
                1,
                new TemplateDto(SampleContract, SampleLayout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-02", ["locationCode"] = "A-02-01-01" })]);
            poller.Enqueue(payload);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = new ServerRoutingWorker(poller, submission, queue, TimeSpan.FromMilliseconds(100), NullLogger<ServerRoutingWorker>.Instance);
            var workerTask = worker.StartAsync(cts.Token);

            LabelJob? localJob = null;
            for (var i = 0; i < 100 && localJob is null; i++)
            {
                await Task.Delay(50, cts.Token);
                localJob = await store.GetJobByRequestIdAsync("req-route-fast", cts.Token);
            }

            Assert.NotNull(localJob);

            var claimed = await queue.ClaimNextItemAsync(cts.Token);
            Assert.NotNull(claimed);
            await queue.CompleteItemAsync(claimed!.Value.JobId, claimed.Value.Item.Id, cts.Token);

            // 回报应在独立循环（1s 周期）内及时送达，而不是等 10s 长轮询超时
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 60 && poller.Reported.Count == 0; i++)
            {
                await Task.Delay(50, cts.Token);
            }

            stopwatch.Stop();
            Assert.Single(poller.Reported);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"完成回报被长轮询阻塞，延迟 {stopwatch.Elapsed.TotalSeconds:F1}s。");

            await worker.StopAsync(cts.Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
    private static LabelFrame.Core.Contracts.LabelContract SampleContract { get; } = new()
    {
        Name = "location-label",
        Version = "1.0",
        Fields =
        [
            new LabelFrame.Core.Contracts.LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            new LabelFrame.Core.Contracts.LabelField { Key = "zone", DisplayName = "区域", IsRequired = true },
        ],
    };

    private static LabelLayout SampleLayout { get; } = new()
    {
        Name = "location-label-100x60",
        ContractName = "location-label",
        ContractVersion = "1.0",
        WidthMm = 100,
        HeightMm = 60,
        Elements =
        [
            new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
            new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
        ],
    };
}