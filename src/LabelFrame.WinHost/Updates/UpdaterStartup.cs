using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.ServiceProcess;
using System.Security;
using LabelFrame.Updates;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace LabelFrame.WinHost.Updates;

/// <summary>更新服务进程的退出码；系统服务管理器记录的始终是 1066，细分原因见事件日志。</summary>
internal static class UpdaterExitCodes
{
    public const int SelfCheckFailed = 10;
    public const int StartupFailed = 11;
    public const int StartupTimeout = 12;
}

/// <summary>更新服务启动握手：记录当前阶段，并把「已就绪」或「启动失败」通知等待中的系统服务启动线程。</summary>
internal sealed class UpdaterStartup
{
    /// <summary>启动阶段名，按发生顺序；失败时写进事件日志，用来判断停在哪一步。</summary>
    internal static class Stages
    {
        public const string SelfCheck = "安装目录自检";
        public const string PipeReady = "监听升级请求管道";
    }

    /// <summary>事件日志里更新服务启动失败的固定事件号，便于按来源加事件号检索。</summary>
    internal const int FailureEventId = 1;

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception> _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string> _writeFailure;
    private volatile string _stage = Stages.SelfCheck;
    private int _failureReported;

    /// <param name="writeFailure">启动失败原因的落点，默认写事件日志（来源 = 服务名）。</param>
    public UpdaterStartup(Action<string>? writeFailure = null) => _writeFailure = writeFailure ?? WriteFailureEvent;

    public string Stage => _stage;
    public bool IsSelfChecked { get; private set; }
    public bool IsReady => _ready.Task.IsCompleted;
    public Exception? Failure { get; private set; }

    public void Enter(string stage) => _stage = stage;
    public void MarkSelfChecked() => IsSelfChecked = true;
    public void MarkReady() => _ready.TrySetResult();

    public void MarkFailed(Exception error)
    {
        Failure ??= error;
        _failed.TrySetResult(error);
    }

    /// <summary>同步等待就绪或失败，先到者为准；超过时限返回超时异常（timedOut 为 true），已就绪返回 null。</summary>
    public Exception? Await(TimeSpan timeout, out bool timedOut)
    {
        try
        {
            var completed = Task.WhenAny(_ready.Task, _failed.Task).WaitAsync(timeout).GetAwaiter().GetResult();
            timedOut = false;
            return completed == _failed.Task ? _failed.Task.Result : null;
        }
        catch (System.TimeoutException)
        {
            timedOut = true;
            return new System.TimeoutException($"更新服务在 {timeout.TotalSeconds:0} 秒内未完成启动。");
        }
    }

    /// <summary>报告启动失败（默认同步写事件日志：进程随后即退出，宿主的异步日志可能来不及落盘），重复调用只写一次。</summary>
    public void ReportFailure()
    {
        if (Failure is null || Interlocked.Exchange(ref _failureReported, 1) != 0) return;
        _writeFailure(Describe(Stage, Failure));
    }

    /// <summary>启动结束时结算：已就绪且无失败返回 0；否则补记原因并给出进程退出码。</summary>
    public int Complete()
    {
        if (IsReady && Failure is null) return 0;
        Failure ??= new InvalidOperationException("更新服务在就绪前停止。");
        ReportFailure();
        return Failure is System.TimeoutException ? UpdaterExitCodes.StartupTimeout
            : Stage == Stages.SelfCheck ? UpdaterExitCodes.SelfCheckFailed
            : UpdaterExitCodes.StartupFailed;
    }

    /// <summary>把启动失败整理成一行可读的原因。</summary>
    public static string Describe(string stage, Exception error) => $"更新服务启动失败，停在阶段「{stage}」：{error.Message}";

    private static void WriteFailureEvent(string message)
    {
        try
        {
            new EventLog("Application") { Source = ClientUpdateContract.ServiceName }
                .WriteEntry(message, EventLogEntryType.Error, FailureEventId);
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception or ArgumentException or SecurityException) { }
    }
}

/// <summary>更新服务的系统服务生命周期：自检在系统记为「已启动」之前完成，就绪等待放到后台兜底。</summary>
internal sealed class ClientUpdaterLifetime : WindowsServiceLifetime
{
    private readonly UpdaterStartup startup;
    private readonly IHostApplicationLifetime applicationLifetime;

    public ClientUpdaterLifetime(IHostEnvironment environment, IHostApplicationLifetime applicationLifetime, ILoggerFactory loggerFactory,
        IOptions<Microsoft.Extensions.Hosting.HostOptions> hostOptions, IOptions<WindowsServiceLifetimeOptions> serviceOptions, UpdaterStartup startup)
        : base(environment, applicationLifetime, loggerFactory, hostOptions, serviceOptions)
    {
        this.applicationLifetime = applicationLifetime;
        this.startup = startup;
    }

    /// <summary>启动就绪的兜底时限：正常启动约 1~2 秒，留足余量，慢机器不致被误判。</summary>
    internal static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>兜底触发后留给宿主自行退出的时间，超过即结束进程。</summary>
    internal static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);

    protected override void OnStart(string[] args)
    {
        try
        {
            ClientUpdaterService.SelfCheck(startup);
        }
        catch (Exception error)
        {
            startup.MarkFailed(error);
            startup.ReportFailure();
            throw; // 防回归警示：OnStart 抛异常系统才记「启动失败」；放行后再失败会记成启动成功后自行退出，安装器会反复重试
        }

        // 防回归警示：不要在 OnStart 里等就绪。服务处于「启动中」时系统会丢弃停止请求，升级、卸载或回滚
        // 恰好撞上启动期就只能干等；自检只有几毫秒，放行宿主后立即返回
        base.OnStart(args);
        _ = Task.Run(WatchStartup);
    }

    private void WatchStartup()
    {
        var failure = startup.Await(ReadyTimeout, out var timedOut);
        if (failure is null) return;
        startup.MarkFailed(failure);
        startup.ReportFailure();
        if (!timedOut) return; // 超时以外的失败由宿主自行停止，退出码在启动收尾时结算
        applicationLifetime.StopApplication();
        Environment.ExitCode = UpdaterExitCodes.StartupTimeout;
        _ = Task.Delay(StopGrace).ContinueWith(_ => Environment.Exit(UpdaterExitCodes.StartupTimeout), TaskScheduler.Default);
    }
}
