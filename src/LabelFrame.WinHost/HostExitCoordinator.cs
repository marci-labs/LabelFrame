using Microsoft.Extensions.Hosting;

namespace LabelFrame.WinHost;

/// <summary>
/// 宿主统一退出协调器（缺陷 #58）：托盘菜单「退出」与 /api/host/shutdown 共用同一
/// 「优雅停止 + 限时兜底强退」路径——StopApplication 后有限等待主流程自然退出
/// （RunAsync 返回并完成清理），超时则执行注册的清理（托盘图标移除 / 界面壳释放）后强制退出。
/// 两条退出入口不得再有强弱退差异；退出链路各阶段均写 host.log 记账，
/// 用户侧「点了退出没反应」时可定位具体阶段。
/// </summary>
public sealed class HostExitCoordinator
{
    /// <summary>优雅等待上限（主流程自然退出的宽限；Issue #58 修复方向：2~3 秒量级）。</summary>
    public static readonly TimeSpan DefaultGracefulTimeout = TimeSpan.FromSeconds(2.5);

    private readonly Action<string> _log;
    private readonly TimeSpan _gracefulTimeout;
    private readonly Action<int> _forceExit;
    private readonly TaskCompletionSource _naturalExitCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _cleanupGate = new();
    private IHostApplicationLifetime? _lifetime;
    private Action? _cleanup;
    private bool _cleanupDone;
    private int _shutdownRequested;

    /// <param name="log">host.log 记账回调。</param>
    /// <param name="gracefulTimeout">优雅等待上限（默认 <see cref="DefaultGracefulTimeout"/>）。</param>
    /// <param name="forceExit">强退动作（默认 Environment.Exit；测试注入替身以便进程内观测）。</param>
    public HostExitCoordinator(Action<string> log, TimeSpan? gracefulTimeout = null, Action<int>? forceExit = null)
    {
        _log = log;
        _gracefulTimeout = gracefulTimeout ?? DefaultGracefulTimeout;
        _forceExit = forceExit ?? Environment.Exit;
    }

    /// <summary>绑定宿主生命周期（应用 Build 后由装配方调用一次；退出请求据此发起 StopApplication）。</summary>
    public void Bind(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    /// <summary>
    /// 注册退出清理（托盘 WM_QUIT 投递、界面壳 Dispose 等）。
    /// 自然退出路径（Main 的 finally）与强退兜底路径经 <see cref="RunCleanup"/> 共用同一清理，幂等执行。
    /// </summary>
    public void RegisterCleanup(Action cleanup)
    {
        _cleanup = cleanup;
    }

    /// <summary>
    /// 收到退出请求（来源：托盘菜单 / HTTP）。幂等：首个请求执行「优雅停止 + 限时兜底强退」序列，
    /// 后续请求只记账不重复执行。返回的 Task 在序列结束时完成（强退路径实际不会完成——进程已退出）。
    /// </summary>
    /// <param name="source">请求来源（写入 host.log 记账，如「托盘菜单『退出』」/「HTTP /api/host/shutdown」）。</param>
    /// <param name="delayBeforeStop">停止宿主前的缓冲（HTTP 路径让响应先送达客户端；默认无）。</param>
    public Task RequestShutdownAsync(string source, TimeSpan delayBeforeStop = default)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1)
        {
            _log($"退出请求（来源：{source}）已忽略——退出流程进行中。");
            return Task.CompletedTask;
        }

        return Task.Run(() => ShutdownSequenceAsync(source, delayBeforeStop));
    }

    /// <summary>主流程自然退出完成（RunAsync 已返回且清理已执行，即将退出进程）——兜底看门狗据此收手。</summary>
    public void MarkNaturalExitCompleted()
    {
        _naturalExitCompleted.TrySetResult();
        if (Volatile.Read(ref _shutdownRequested) == 1)
        {
            _log("主流程自然退出完成（RunAsync 已返回、清理已执行）。");
        }
    }

    /// <summary>执行注册的退出清理（幂等；自然退出与强退兜底共用，并发调用会等待首次执行完成）。</summary>
    public void RunCleanup()
    {
        lock (_cleanupGate)
        {
            if (_cleanupDone)
            {
                return;
            }

            _cleanupDone = true;
            var cleanup = _cleanup;
            _cleanup = null;
            _log("执行退出清理（移除托盘图标、释放界面壳）…");
            try
            {
                cleanup?.Invoke();
                _log("退出清理完成。");
            }
            catch (Exception ex)
            {
                _log($"退出清理异常（忽略，继续退出流程）：{ex}");
            }
        }
    }

    private async Task ShutdownSequenceAsync(string source, TimeSpan delayBeforeStop)
    {
        _log($"收到退出请求（来源：{source}）。");
        if (delayBeforeStop > TimeSpan.Zero)
        {
            await Task.Delay(delayBeforeStop).ConfigureAwait(false);
        }

        var lifetime = _lifetime;
        if (lifetime is null)
        {
            _log("宿主生命周期未绑定，跳过 StopApplication（直接进入清理与强退兜底）。");
        }
        else
        {
            try
            {
                _log("发起 StopApplication（停止宿主与全部后台服务）。");
                lifetime.StopApplication();
            }
            catch (Exception ex)
            {
                _log($"StopApplication 异常（继续走退出兜底）：{ex}");
            }
        }

        var naturalExit = _naturalExitCompleted.Task;
        var gracePeriod = Task.Delay(_gracefulTimeout);
        var finished = await Task.WhenAny(naturalExit, gracePeriod).ConfigureAwait(false);
        if (finished == naturalExit)
        {
            _log("主流程自然退出确认，兜底看门狗收手（退出收尾由主流程完成）。");
            return;
        }

        // RunAsync 未自然返回（托盘 / 界面消息循环在场的已知问题，Issue #58 证据链）或清理未完成：
        // 显式清理（托盘 NIM_DELETE / 界面壳释放）后强制退出，进程不再僵死、图标不再残留。
        _log($"优雅等待超时（{_gracefulTimeout.TotalSeconds:0.##} 秒，RunAsync 未返回或清理未完成），执行清理后强制退出。");
        RunCleanup();
        _log("强制退出：Environment.Exit(0)。");
        _forceExit(0);
    }
}
