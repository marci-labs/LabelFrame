using System.Diagnostics;
using System.IO.Pipes;
using System.ServiceProcess;
using LabelFrame.WinHost.Updates;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>安装器启动更新服务时的等待判定：尽快发现失败、观察期、兜底超时。</summary>
public sealed class UpdaterServiceStarterTests
{
    [Fact]
    public void Service_that_reaches_running_and_stays_is_reported_running()
    {
        var states = new Queue<ServiceControllerStatus>([ServiceControllerStatus.Stopped, ServiceControllerStatus.StartPending,
            ServiceControllerStatus.StartPending, ServiceControllerStatus.Running, ServiceControllerStatus.Running, ServiceControllerStatus.Running]);
        var starts = 0;

        var outcome = UpdaterServiceStarter.Await(states.Dequeue, () => true, () => starts++, TimeSpan.FromSeconds(5), TimeSpan.Zero);

        Assert.Equal(UpdaterServiceStarter.Outcome.Running, outcome);
        Assert.Equal(1, starts);
    }

    [Fact]
    public void Service_already_pending_or_running_is_not_started_again()
    {
        foreach (var state in new[] { ServiceControllerStatus.Running, ServiceControllerStatus.StartPending })
        {
            var starts = 0;

            var outcome = UpdaterServiceStarter.Await(() => state == ServiceControllerStatus.Running ? state : ServiceControllerStatus.Running, () => true,
                () => starts++, TimeSpan.FromSeconds(5), TimeSpan.Zero);

            Assert.Equal(UpdaterServiceStarter.Outcome.Running, outcome);
            Assert.Equal(0, starts);
        }
    }

    [Fact]
    public void Service_that_reports_stopped_fails_at_once_without_waiting_for_the_backstop()
    {
        var states = new Queue<ServiceControllerStatus>([ServiceControllerStatus.Stopped, ServiceControllerStatus.StartPending,
            ServiceControllerStatus.StartPending, ServiceControllerStatus.Stopped, ServiceControllerStatus.Stopped]);
        var clock = Stopwatch.StartNew();

        var outcome = UpdaterServiceStarter.Await(states.Dequeue, () => true, () => { }, TimeSpan.FromSeconds(30), TimeSpan.Zero);

        Assert.Equal(UpdaterServiceStarter.Outcome.Stopped, outcome);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"提前返回耗时 {clock.Elapsed}");
    }

    [Fact]
    public void Service_that_stops_right_after_reaching_running_is_reported_stopped()
    {
        var states = new Queue<ServiceControllerStatus>([ServiceControllerStatus.StartPending, ServiceControllerStatus.Running, ServiceControllerStatus.Stopped]);

        var outcome = UpdaterServiceStarter.Await(states.Dequeue, () => true, () => { }, TimeSpan.FromSeconds(5), TimeSpan.Zero);

        Assert.Equal(UpdaterServiceStarter.Outcome.Stopped, outcome);
    }

    [Fact]
    public void Service_that_never_becomes_ready_ends_with_the_backstop_timeout()
    {
        var clock = Stopwatch.StartNew();

        var outcome = UpdaterServiceStarter.Await(() => ServiceControllerStatus.StartPending, () => true, () => { }, TimeSpan.FromMilliseconds(300), TimeSpan.Zero);

        Assert.Equal(UpdaterServiceStarter.Outcome.TimedOut, outcome);
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void After_a_failed_verdict_a_service_that_is_still_alive_is_stopped_and_waited_for()
    {
        var readings = new Queue<ServiceControllerStatus>([ServiceControllerStatus.StartPending, ServiceControllerStatus.StopPending,
            ServiceControllerStatus.StopPending, ServiceControllerStatus.Stopped, ServiceControllerStatus.Stopped]);
        var stops = 0;

        var stopped = UpdaterServiceStarter.Stop(readings.Dequeue, () => stops++, TimeSpan.FromSeconds(5));

        Assert.True(stopped);
        Assert.Equal(1, stops);
    }

    [Fact]
    public void Stopping_is_skipped_when_the_service_is_already_stopped_and_tolerates_a_rejected_stop_request()
    {
        var stops = 0;

        Assert.True(UpdaterServiceStarter.Stop(() => ServiceControllerStatus.Stopped, () => stops++, TimeSpan.Zero));
        Assert.True(UpdaterServiceStarter.Stop(() => ServiceControllerStatus.Stopped, () => throw new InvalidOperationException("rejected"), TimeSpan.Zero));
        Assert.False(UpdaterServiceStarter.Stop(() => ServiceControllerStatus.StartPending, () => throw new InvalidOperationException("rejected"), TimeSpan.FromMilliseconds(200)));
        Assert.Equal(0, stops);
    }

    [Fact]
    public void Detail_log_follows_the_install_log_or_falls_back_to_the_account_temp_folder()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "MSIabc.log.updater-service.log");
        Assert.Equal(rooted, UpdaterServiceStarter.DetailLogPath(rooted));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "LabelFrame-updater-service-start.log"), UpdaterServiceStarter.DetailLogPath(".updater-service.log"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "LabelFrame-updater-service-start.log"), UpdaterServiceStarter.DetailLogPath(null));
    }

    [Fact]
    public void Only_events_written_by_the_service_after_the_start_request_are_quoted_newest_first()
    {
        var time = new DateTime(2026, 11, 25, 0, 29, 0);
        (int Index, string Source, long InstanceId, DateTime Time, string Message)[] entries =
        [
            new(42, "LabelFrameClientUpdater", 1, time, "更新服务启动失败，停在阶段「安装目录自检」：自动安装仅支持管理员部署在 Program Files 下的客户端。"),
            new(41, "LabelFrame.WinHost", 9, time.AddSeconds(-1), "BackgroundService failed"),
            new(40, "LabelFrameClientUpdater", 0, time.AddSeconds(-2), "Service cannot be started."),
            new(39, "LabelFrameClientUpdater", 0, time.AddMinutes(-1), "Service stopped successfully."),
            new(38, "MsiInstaller", 1033, time.AddMinutes(-2), "irrelevant"),
        ];

        var quoted = UpdaterServiceStarter.ServiceEvents(39, entries);

        Assert.Equal("[00:29:00 事件 1] 更新服务启动失败，停在阶段「安装目录自检」：自动安装仅支持管理员部署在 Program Files 下的客户端。 | [00:28:58 事件 0] Service cannot be started.", quoted);
        Assert.Equal("（本次启动期间服务没有留下记录）", UpdaterServiceStarter.ServiceEvents(42, entries));
    }

    [Fact]
    public async Task Request_pipe_is_reported_ready_without_connecting_to_it()
    {
        var name = "LabelFrame.Tests." + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cancellation = new CancellationTokenSource();
        var connection = pipe.WaitForConnectionAsync(cancellation.Token);
        try
        {
            Assert.True(UpdaterServiceStarter.PipeExists(name));
            Assert.False(UpdaterServiceStarter.PipeExists(name + ".missing"));

            // 安装器在成功路径上会探测两次：任何一次连上管道，更新服务都会记一条「请求被拒绝」的警告
            Assert.True(UpdaterServiceStarter.PipeExists(name));
            Assert.False(connection.IsCompleted);

            // 对照：真实客户端连上时，同一种观察方式看得到，上面「没有连上」不是因为观察不到
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out);
            client.Connect(5000);
            await connection;
            Assert.True(pipe.IsConnected);
        }
        finally
        {
            cancellation.Cancel();
            try { await connection; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public void Running_state_without_the_request_pipe_is_not_treated_as_ready()
    {
        var clock = Stopwatch.StartNew();

        var outcome = UpdaterServiceStarter.Await(() => ServiceControllerStatus.Running, () => false, () => { }, TimeSpan.FromMilliseconds(300), TimeSpan.Zero);

        Assert.Equal(UpdaterServiceStarter.Outcome.TimedOut, outcome);
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5));
    }
}
