using LabelFrame.WinHost.Ui;

namespace LabelFrame.WinHost.Tests.Ui;

/// <summary>单实例守卫：互斥抢占与跨进程激活信号（二次启动 → 激活已有实例界面）。</summary>
public class SingleInstanceGuardTests
{
    private static string UniqueName(string prefix)
    {
        return $@"Global\LabelFrame.Test.{prefix}.{Guid.NewGuid():N}";
    }

    [Fact]
    public void 首个实例成功获得守卫()
    {
        var name = UniqueName("acquire");
        using var guard = SingleInstanceGuard.TryAcquire(name, name + ".activate");

        Assert.NotNull(guard);
    }

    [Fact]
    public void 已有实例在运行_第二次获取返回空()
    {
        var name = UniqueName("second");
        using var guard = SingleInstanceGuard.TryAcquire(name, name + ".activate");

        // 互斥锁按线程计持有：模拟另一进程需在别的线程抢占（同线程重入会成功）
        SingleInstanceGuard? second = null;
        var thread = new Thread(() => second = SingleInstanceGuard.TryAcquire(name, name + ".activate"));
        thread.Start();
        thread.Join();

        Assert.Null(second);
    }

    [Fact]
    public void 释放后可重新获取()
    {
        var name = UniqueName("release");
        var first = SingleInstanceGuard.TryAcquire(name, name + ".activate");
        first!.Dispose();

        using var second = SingleInstanceGuard.TryAcquire(name, name + ".activate");

        Assert.NotNull(second);
    }

    [Fact]
    public async Task 二次启动信号_已有实例激活事件被置位()
    {
        var name = UniqueName("activate");
        using var guard = SingleInstanceGuard.TryAcquire(name, name + ".activate");
        Assert.NotNull(guard);

        SingleInstanceGuard.SignalActivate(name + ".activate");

        // WaitAny 超时即视为未收到；激活事件为 AutoReset，等待成功即消费信号
        var index = WaitHandle.WaitAny([guard!.ActivateEvent], TimeSpan.FromSeconds(2));
        Assert.Equal(0, index);
        await Task.CompletedTask;
    }
}
