using LabelFrame.WinHost.Updates;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>更新服务启动握手：就绪、启动失败、兜底超时三种结局的判定与原因记录。</summary>
public sealed class UpdaterStartupTests
{
    [Fact]
    public async Task Ready_completes_startup_without_failure_or_report()
    {
        var reports = new List<string>();
        var startup = new UpdaterStartup(reports.Add);
        startup.Enter(UpdaterStartup.Stages.PipeReady);
        await Task.Run(startup.MarkReady);

        var failure = startup.Await(TimeSpan.FromSeconds(5), out var timedOut);

        Assert.Null(failure);
        Assert.False(timedOut);
        Assert.True(startup.IsReady);
        Assert.Equal(0, startup.Complete());
        Assert.Empty(reports);
    }

    [Fact]
    public void Failure_arriving_first_is_returned_with_its_stage_and_reported_once()
    {
        var reports = new List<string>();
        var startup = new UpdaterStartup(reports.Add);
        startup.Enter(UpdaterStartup.Stages.PipeReady);
        var error = new InvalidOperationException("管道被占用");

        startup.MarkFailed(error);
        var failure = startup.Await(TimeSpan.FromSeconds(5), out var timedOut);

        Assert.Same(error, failure);
        Assert.False(timedOut);
        Assert.Equal(UpdaterExitCodes.StartupFailed, startup.Complete());
        startup.ReportFailure();
        var message = Assert.Single(reports);
        Assert.Contains(UpdaterStartup.Stages.PipeReady, message);
        Assert.Contains(error.Message, message);
    }

    [Fact]
    public void Backstop_timeout_is_returned_when_neither_ready_nor_failure_arrives()
    {
        var reports = new List<string>();
        var startup = new UpdaterStartup(reports.Add);

        var failure = startup.Await(TimeSpan.FromMilliseconds(200), out var timedOut);

        Assert.True(timedOut);
        Assert.IsType<System.TimeoutException>(failure);
        startup.MarkFailed(failure!);
        Assert.Equal(UpdaterExitCodes.StartupTimeout, startup.Complete());
        Assert.Contains("未完成启动", Assert.Single(reports));
    }

    [Fact]
    public void Stopping_before_ready_is_a_failure_that_names_the_last_stage()
    {
        var startup = new UpdaterStartup(_ => { });
        startup.Enter(UpdaterStartup.Stages.PipeReady);

        Assert.Equal(UpdaterExitCodes.StartupFailed, startup.Complete());
        Assert.Equal(UpdaterStartup.Stages.PipeReady, startup.Stage);
    }

    [Fact]
    public void Self_check_rejects_an_unprotected_install_location_before_anything_is_created()
    {
        var startup = new UpdaterStartup(_ => { });

        Assert.ThrowsAny<Exception>(() => ClientUpdaterService.SelfCheck(startup));

        Assert.Equal(UpdaterStartup.Stages.SelfCheck, startup.Stage);
        Assert.False(startup.IsSelfChecked);
    }
}
