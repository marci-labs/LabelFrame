using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Rendering;
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
            var submission = new JobSubmissionService(queue, new ZplEncoder(), new GdiTextRasterizer(), dpi: 203);

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