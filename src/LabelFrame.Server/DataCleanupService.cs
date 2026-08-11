using LabelFrame.Core.Logs;

namespace LabelFrame.Server;

/// <summary>历史数据定期清理：删除超过保留期的终态作业与设备日志（迭代 18）。</summary>
public sealed class DataCleanupService : BackgroundService
{
    private readonly ServerDb _db;
    private readonly SqliteLogStore _logStore;
    private readonly ServerOptions _options;
    private readonly ILogger<DataCleanupService> _logger;

    public DataCleanupService(ServerDb db, SqliteLogStore logStore, ServerOptions options, ILogger<DataCleanupService> logger)
    {
        _db = db;
        _logStore = logStore;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后延迟 60 秒执行一次，之后按 CleanupIntervalHours 周期执行
        var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "历史数据清理失败。");
            }
        }
    }

    /// <summary>执行一次清理：终态作业按 JobRetentionDays、日志按 LogRetentionDays。</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var jobCutoff = now.AddDays(-Math.Max(0, _options.JobRetentionDays));
        var jobDeleted = await _db.DeleteTerminalJobsBeforeAsync(jobCutoff, cancellationToken);
        var logCutoff = now.AddDays(-Math.Max(0, _options.LogRetentionDays));
        var logDeleted = await _logStore.DeleteBeforeAsync(logCutoff, cancellationToken);
        _logger.LogInformation("历史数据清理完成：删除终态作业 {JobCount} 条、日志 {LogCount} 条。", jobDeleted, logDeleted);
    }
}
