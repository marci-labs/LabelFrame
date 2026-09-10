using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>
/// 失败上下文结构化日志验证（决策 #102 / AC-06）：发送失败日志须含作业 / 项索引 / 插件 / 打印目标 / 错误码 / 原因，
/// 经与生产同模板的 Serilog 文件通道落盘后逐字段断言。
/// </summary>
public class JobPrintWorkerFailureLogTests
{
    private sealed class FailingTransportManager : ITransportManager
    {
        public TransportConfig CurrentConfig { get; } = new()
        {
            PluginId = "tcp9100",
            Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["host"] = "203.0.113.1",
                ["port"] = "9100",
            },
        };

        public IPrintTransport CurrentTransport { get; } = new ThrowingTransport();

        public string ConfigFilePath => "unused";

        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("测试桩不支持切换连接");

        private sealed class ThrowingTransport : IPrintTransport
        {
            public Task SendAsync(string command, CancellationToken cancellationToken = default)
                => throw new IOException("连接超时（模拟打印机不可达）");
        }
    }

    [Fact]
    public async Task Send_failure_log_should_carry_structured_context()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lffaillog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(directory, "app-.log"),
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            var loggerFactory = LoggerFactory.Create(factory => factory.AddSerilog(logger, dispose: true));

            var dbPath = Path.Combine(directory, "jobs.db");
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var (job, _) = await queue.SubmitAsync($"req-fail-{Guid.NewGuid():N}", ["^XA^FO50,50^GB100,100,3^FS^XZ"]);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = new JobPrintWorker(queue, new FailingTransportManager(), loggerFactory.CreateLogger<JobPrintWorker>(), new PrintSettings());
            await worker.StartAsync(cts.Token);

            // 等待失败落库（发送抛异常 → FailItemAsync → 作业挂起）
            for (var i = 0; i < 200; i++)
            {
                var current = await queue.GetAsync(job.Id, cts.Token);
                if (current?.Items.Any(it => it.Status == LabelJobItemStatus.Failed) == true)
                {
                    break;
                }

                await Task.Delay(50, cts.Token);
            }

            await worker.StopAsync(cts.Token);
            loggerFactory.Dispose(); // 释放 Serilog sink 并落盘

            var logFile = Directory.GetFiles(directory, "app-*.log").Single();
            var content = File.ReadAllText(logFile);
            Assert.Contains("发送失败", content);
            Assert.Contains(job.Id, content);                       // 作业
            Assert.Contains("第 0 张", content);                    // 项索引（0 起）
            Assert.Contains("tcp9100", content);                    // 传输插件
            Assert.Contains("host=203.0.113.1", content);          // 打印目标
            Assert.Contains("port=9100", content);
            Assert.Contains("LF_IO_001", content);                  // 错误码
            Assert.Contains("连接超时", content);                    // 失败原因
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
                // 日志文件句柄延迟释放时忽略清理失败
            }
        }
    }
}
