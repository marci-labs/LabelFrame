using System.Diagnostics;
using LabelFrame.WinHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// HostExitCoordinator（统一退出路径）单元测试（缺陷 #58 AC-02 / AC-05；Issue #64 AC-01 事件驱动提前退出）：
/// 「优雅停止 + 稳定窗提前退出 + 限时兜底强退」序列的记账日志（收到退出请求（来源）→ StopApplication 发起 →
/// 宿主停止与否 → 稳定窗观察 / 提前退出 / 宽限超时 → 清理执行 → 最终退出方式）、时序断言
/// （ApplicationStopped 后主流程未返回时 ≤ 稳定窗内清理 + 强退）、订阅时已停止竞态、幂等与异常兜底。
/// 强退动作注入替身（不真正 Environment.Exit），进程内可断言。
/// </summary>
public sealed class HostExitCoordinatorTests
{
    /// <summary>IHostApplicationLifetime 测试替身：记录 StopApplication 调用，可注入异常 / 抑制宿主停止信号。</summary>
    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public bool ThrowOnStop { get; set; }

        /// <summary>StopApplication 是否发出宿主停止信号（默认发出；置 false 模拟宿主停止本身挂死、ApplicationStopped 永不到来）。</summary>
        public bool StopSignalsStopped { get; set; } = true;

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
            if (StopSignalsStopped)
            {
                _stopped.Cancel();
            }
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

        public HostExitCoordinator Create(TimeSpan? gracefulTimeout = null, TimeSpan? stabilizationWindow = null)
            => new(
                Logs.Add,
                gracefulTimeout ?? TimeSpan.FromSeconds(5),
                Exits.Add,
                stabilizationWindow);

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
    public async Task Request_shutdown_should_force_exit_within_stabilization_window_when_host_stopped_but_main_flow_hung()
    {
        // Issue #64 AC-01（时序断言）：宿主停止信号（ApplicationStopped）到来而主流程未返回——
        // 协调器应在稳定窗（默认 500 毫秒）内进入清理 + 强退，不再烧满宽限（2.5 秒兜底不动）。
        var harness = new Harness();
        var coordinator = harness.Create(
            gracefulTimeout: HostExitCoordinator.DefaultGracefulTimeout,
            stabilizationWindow: HostExitCoordinator.DefaultStabilizationWindow);
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        var stopwatch = Stopwatch.StartNew();
        // 主流程永不自然退出（RunAsync 僵死场景）；FakeLifetime 在 StopApplication 内同步发出宿主停止信号
        await coordinator.RequestShutdownAsync("HTTP /api/host/shutdown（Web UI 设置页「退出程序」）");
        stopwatch.Stop();

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(1, harness.CleanupRuns);
        Assert.Equal(0, Assert.Single(harness.Exits));
        // 时序：不早于稳定窗（观察期必须走满），且远小于宽限上限（Issue #64 提速目标 <1.5 秒）
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(450),
            $"清理 + 强退早于稳定窗（实测 {stopwatch.Elapsed.TotalMilliseconds:0} 毫秒，应 ≥500 毫秒观察期）。");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500),
            $"稳定窗提前退出超时（实测 {stopwatch.Elapsed.TotalMilliseconds:0} 毫秒，应 <1500 毫秒）。");
        harness.AssertLogContains("收到退出请求（来源：HTTP /api/host/shutdown");
        harness.AssertLogContains("宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察（500 毫秒）");
        harness.AssertLogContains("稳定窗（500 毫秒）届满主流程仍未返回，判定卡死——稳定窗提前退出");
        harness.AssertLogContains("执行退出清理（移除托盘图标、释放界面壳）");
        harness.AssertLogContains("退出清理完成");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public async Task Request_shutdown_should_stand_down_when_natural_exit_completes_within_stabilization_window()
    {
        // 稳定窗内主流程自然返回：看门狗收手，不强杀不强退（优雅返回场景不回归）
        var harness = new Harness();
        var coordinator = harness.Create(
            gracefulTimeout: TimeSpan.FromSeconds(5),
            stabilizationWindow: TimeSpan.FromSeconds(5));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        var shutdownTask = coordinator.RequestShutdownAsync("托盘菜单「退出」");
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        coordinator.MarkNaturalExitCompleted();
        await shutdownTask;

        Assert.Empty(harness.Exits);
        Assert.Equal(0, harness.CleanupRuns);
        harness.AssertLogContains("宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察（5000 毫秒）");
        harness.AssertLogContains("稳定窗内主流程自然退出，兜底看门狗收手");
    }

    [Fact]
    public async Task Request_shutdown_should_force_exit_promptly_when_host_already_stopped_before_subscription()
    {
        // 竞态：订阅 ApplicationStopped 时宿主已停止（先外部 StopApplication 再发起退出请求）——
        // 「挂回调后补查已置位」应立即命中信号，照常走稳定窗提前退出，而不是退回宽限兜底干等。
        var harness = new Harness();
        var coordinator = harness.Create(
            gracefulTimeout: TimeSpan.FromSeconds(5),
            stabilizationWindow: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));
        harness.Lifetime.StopApplication();

        var stopwatch = Stopwatch.StartNew();
        await coordinator.RequestShutdownAsync("托盘菜单「退出」");
        stopwatch.Stop();

        Assert.Equal(1, harness.CleanupRuns);
        Assert.Equal(0, Assert.Single(harness.Exits));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"订阅时已停止未即时命中信号（实测 {stopwatch.Elapsed.TotalMilliseconds:0} 毫秒，应 <1000 毫秒而非干等宽限）。");
        harness.AssertLogContains("宿主已停止（ApplicationStopped）而主流程未返回");
        harness.AssertLogContains("稳定窗提前退出");
    }

    [Fact]
    public async Task Request_shutdown_should_force_exit_after_graceful_timeout_when_host_stop_never_completes()
    {
        // 宽限兜底保留：宿主停止本身挂死（ApplicationStopped 未在宽限期内到来）——按现状超时清理 + 强退
        var harness = new Harness();
        harness.Lifetime.StopSignalsStopped = false;
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(1, harness.CleanupRuns);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("优雅等待超时（0.05 秒");
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
