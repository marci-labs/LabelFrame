using LabelFrame.WinHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// HostExitCoordinator（统一退出路径）单元测试（缺陷 #58，AC-02 / AC-05）：
/// 「优雅停止 + 限时兜底强退」序列的记账日志（收到退出请求（来源）→ StopApplication 发起 →
/// 优雅等待超时与否 → 清理执行 → 最终退出方式）、超时强退 + 清理、幂等与异常兜底。
/// 强退动作注入替身（不真正 Environment.Exit），进程内可断言。
/// </summary>
public sealed class HostExitCoordinatorTests
{
    /// <summary>IHostApplicationLifetime 测试替身：记录 StopApplication 调用，可注入异常。</summary>
    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public bool ThrowOnStop { get; set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            Interlocked.Increment(ref _stopCalls);
            if (ThrowOnStop)
            {
                throw new InvalidOperationException("模拟 StopApplication 失败");
            }

            _stopping.Cancel();
            _stopped.Cancel();
        }

        public void Dispose()
        {
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }

    private sealed class Harness : IDisposable
    {
        public List<string> Logs { get; } = [];

        public List<int> Exits { get; } = [];

        public FakeLifetime Lifetime { get; } = new();

        public int CleanupRuns;

        public HostExitCoordinator Create(TimeSpan? gracefulTimeout = null)
            => new(
                Logs.Add,
                gracefulTimeout ?? TimeSpan.FromSeconds(5),
                Exits.Add);

        public void AssertLogContains(string fragment)
            => Assert.True(Logs.Any(l => l.Contains(fragment)), $"记账日志缺少「{fragment}」，实际：{string.Join(Environment.NewLine, Logs)}");

        public void Dispose()
        {
            Lifetime.Dispose();
        }
    }

    [Fact]
    public async Task Request_shutdown_should_log_stages_and_stop_application_when_natural_exit_completes()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        // 主流程在宽限期内自然退出完成（模拟 RunAsync 已返回且清理已执行）——请求发起后再标记，贴合生产时序
        var shutdownTask = coordinator.RequestShutdownAsync("托盘菜单「退出」");
        coordinator.MarkNaturalExitCompleted();

        await shutdownTask;

        Assert.Equal(1, harness.Lifetime.StopCalls);
        harness.AssertLogContains("收到退出请求（来源：托盘菜单「退出」）");
        harness.AssertLogContains("发起 StopApplication");
        harness.AssertLogContains("主流程自然退出完成");
        harness.AssertLogContains("兜底看门狗收手");
        // 自然退出：不强退、强退清理不由此触发（清理由主流程自己经 RunCleanup 执行）
        Assert.Empty(harness.Exits);
        Assert.Equal(0, harness.CleanupRuns);
    }

    [Fact]
    public async Task Request_shutdown_should_force_exit_with_cleanup_after_graceful_timeout()
    {
        var harness = new Harness();
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        // 主流程永不自然退出（RunAsync 僵死场景）——宽限期后应清理 + 强退
        await coordinator.RequestShutdownAsync("HTTP /api/host/shutdown（Web UI 设置页「退出程序」）");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(1, harness.CleanupRuns);
        var exitCode = Assert.Single(harness.Exits);
        Assert.Equal(0, exitCode);
        harness.AssertLogContains("收到退出请求（来源：HTTP /api/host/shutdown");
        harness.AssertLogContains("优雅等待超时");
        harness.AssertLogContains("执行退出清理（移除托盘图标、释放界面壳）");
        harness.AssertLogContains("退出清理完成");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public async Task Request_shutdown_should_be_idempotent_for_duplicate_requests()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        coordinator.Bind(harness.Lifetime);
        coordinator.MarkNaturalExitCompleted();

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");
        await coordinator.RequestShutdownAsync("HTTP /api/host/shutdown");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Empty(harness.Exits);
        harness.AssertLogContains("已忽略——退出流程进行中");
    }

    [Fact]
    public async Task Request_shutdown_without_bound_lifetime_should_still_force_exit()
    {
        var harness = new Harness();
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));

        // 未 Bind（异常装配场景）：跳过 StopApplication，仍走清理与强退兜底
        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(0, harness.Lifetime.StopCalls);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("宿主生命周期未绑定");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public async Task Request_shutdown_should_still_force_exit_when_stop_application_throws()
    {
        var harness = new Harness();
        harness.Lifetime.ThrowOnStop = true;
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("StopApplication 异常");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public void Run_cleanup_should_be_idempotent()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        var runs = 0;
        coordinator.RegisterCleanup(() => runs++);

        coordinator.RunCleanup();
        coordinator.RunCleanup();

        Assert.Equal(1, runs);
        harness.AssertLogContains("执行退出清理");
    }

    [Fact]
    public void Run_cleanup_should_log_and_continue_when_cleanup_throws()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        coordinator.RegisterCleanup(() => throw new InvalidOperationException("模拟清理失败"));

        coordinator.RunCleanup();

        harness.AssertLogContains("退出清理异常（忽略，继续退出流程）");
    }
}
