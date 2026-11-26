using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using System.Text;
using LabelFrame.Updates;

namespace LabelFrame.WinHost.Updates;

/// <summary>安装器入口：启动更新服务并等到就绪；服务一报告停止立即返回，超时只作兜底。结论以 ASCII 写进安装日志（系统代码页文件，混入其他编码会乱码），中文详情写到带 BOM 的 UTF-8 明细日志。</summary>
internal static class UpdaterServiceStarter
{
    /// <summary>等待就绪的兜底时限，略长于服务自身的启动看门狗，好让服务先报出停在哪个阶段。</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(75);

    /// <summary>服务进入运行状态后的观察期：刚进入运行就退出的情况按失败处理。</summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    /// <summary>判定失败后等服务停下的上限：覆盖服务自身的看门狗时限加退出宽限。</summary>
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    internal enum Outcome { Running, Stopped, TimedOut }

    public static int Run(TextWriter output, string? detailLogPath = null)
    {
        var path = DetailLogPath(detailLogPath);
        using var detail = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(true)) { AutoFlush = true };
        var clock = Stopwatch.StartNew();
        // 安装器可能只保留子进程输出的最后一行：结论与明细日志位置必须合写在这一行
        void Say(string consoleLine, string detailLine)
        {
            output.WriteLine(consoleLine);
            detail.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {detailLine}");
        }
        var pointer = $"Details: {path} ; service events: Windows Application log, source {ClientUpdateContract.ServiceName}.";

        Say("LabelFrame updater service start: begin.", "更新服务启动：开始。");
        var since = LatestEventIndex();
        using var controller = new ServiceController(ClientUpdateContract.ServiceName);
        try
        {
            var outcome = Await(() => Status(controller), PipeReady, controller.Start, Timeout, SettleTime);
            if (outcome == Outcome.Running)
            {
                Say($"LabelFrame updater service start: OK (running and request pipe ready after {clock.Elapsed.TotalSeconds:0.0}s).",
                    $"更新服务启动：已进入运行状态，升级请求管道已就绪（用时 {clock.Elapsed.TotalSeconds:0.0} 秒）。");
                return 0;
            }

            var timedOut = outcome == Outcome.TimedOut;
            if (!Stop(() => Status(controller), controller.Stop, StopTimeout))
                Say("LabelFrame updater service start: the service could not be stopped; the rollback may leave files in use.", "更新服务启动：服务未能停下，安装回滚时可能留下占用。");
            var events = ServiceEvents(since);
            if (timedOut)
                Say($"LabelFrame updater service start: FAILED - not ready within {Timeout.TotalSeconds:0}s (backstop timeout); the installation will be rolled back. {pointer}",
                    $"更新服务启动：{Timeout.TotalSeconds:0} 秒内未就绪（兜底超时），安装将中止并撤回本次改动。服务最后的记录：{events}");
            else
                Say("LabelFrame updater service start: FAILED - the service did not stay running; the installation will be rolled back. " + pointer,
                    $"更新服务启动：失败，安装将中止并撤回本次改动。服务记录的原因：{events}");
            return timedOut ? UpdaterExitCodes.StartupTimeout : UpdaterExitCodes.StartupFailed;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or ArgumentException)
        {
            var code = (error as Win32Exception)?.NativeErrorCode.ToString(CultureInfo.InvariantCulture) ?? "-";
            Say($"LabelFrame updater service start: FAILED - {error.GetType().Name} (native error {code}); the installation will be rolled back. {pointer}",
                $"更新服务启动：系统未能启动或查询服务，安装将中止并撤回本次改动。原因：{error.Message}。服务记录的原因：{ServiceEvents(since)}");
            return UpdaterExitCodes.StartupFailed;
        }
    }

    // 明细日志紧跟安装日志（同一个用户临时目录）；调用方没给有效路径时落到当前账户的临时目录。
    internal static string DetailLogPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Path.IsPathRooted(requested)) return requested;
        return Path.Combine(Path.GetTempPath(), "LabelFrame-updater-service-start.log");
    }

    /// <summary>轮询服务状态与就绪信号：运行且就绪并保持一个观察期返回 Running，报告停止返回 Stopped，超过时限返回 TimedOut。</summary>
    internal static Outcome Await(Func<ServiceControllerStatus> status, Func<bool> ready, Action start, TimeSpan timeout, TimeSpan settle)
    {
        if (status() is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)) start();
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var current = status();
            if (current == ServiceControllerStatus.Stopped) return Outcome.Stopped;
            if (current == ServiceControllerStatus.Running && ready())
            {
                Thread.Sleep(settle);
                var settled = status();
                if (settled == ServiceControllerStatus.Stopped) return Outcome.Stopped;
                if (settled == ServiceControllerStatus.Running && ready()) return Outcome.Running;
            }
            if (DateTime.UtcNow >= deadline) return Outcome.TimedOut;
            Thread.Sleep(PollInterval);
        }
    }

    // 服务一过自检就会被系统记为「运行中」，管道开始监听才算真的可用。
    private static bool PipeReady() => PipeExists(ClientUpdateContract.PipeName);

    /// <summary>按名字判断管道是否已存在，不连接它。</summary>
    // 平台坑：对管道路径调用 File.Exists 会作为客户端连上管道再断开，服务端读到空连接后按被拒绝的请求记一条警告；
    // 列举管道目录不产生连接。读不到目录时按不存在处理，由调用方继续轮询直到兜底时限。
    internal static bool PipeExists(string pipeName)
    {
        try
        {
            return Directory.EnumerateFiles(@"\\.\pipe\").Any(path => string.Equals(Path.GetFileName(path), pipeName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>判定失败后的收尾：把还活着的服务停下并等到停止；停不下时返回 false。安装回滚要删文件，服务不放开占用就会留下残留。</summary>
    internal static bool Stop(Func<ServiceControllerStatus> status, Action stop, TimeSpan timeout)
    {
        try
        {
            if (status() == ServiceControllerStatus.Stopped) return true;
            stop();
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or ArgumentException) { }
        var deadline = DateTime.UtcNow + timeout;
        while (status() != ServiceControllerStatus.Stopped)
        {
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(PollInterval);
        }
        return true;
    }

    // ServiceController 的状态是缓存值，每次读取前必须刷新，否则启动后仍会读到启动前的「已停止」。
    private static ServiceControllerStatus Status(ServiceController controller)
    {
        controller.Refresh();
        return controller.Status;
    }

    private static int LatestEventIndex()
    {
        using var log = new EventLog("Application");
        return log.Entries.Count == 0 ? 0 : log.Entries[log.Entries.Count - 1].Index;
    }

    /// <summary>本次启动期间服务写到事件日志的条目，新的在前拼成一行；服务侧的失败原因与停在哪一步都在这里。</summary>
    internal static string ServiceEvents(int since)
    {
        try
        {
            using var log = new EventLog("Application");
            var entries = log.Entries.Cast<EventLogEntry>()
                .Select(entry => (entry.Index, entry.Source, entry.InstanceId, entry.TimeGenerated, Message: FirstLine(entry.Message)))
                .Reverse().ToList();
            return ServiceEvents(since, entries);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or ArgumentException)
        {
            return $"（读取事件日志失败：{error.Message}）";
        }
    }

    internal static string ServiceEvents(int since, IReadOnlyList<(int Index, string Source, long InstanceId, DateTime Time, string Message)> entries)
    {
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Index <= since) break;
            if (entry.Source == ClientUpdateContract.ServiceName) lines.Add($"[{entry.Time:HH:mm:ss} 事件 {entry.InstanceId}] {entry.Message}");
        }
        return lines.Count == 0 ? "（本次启动期间服务没有留下记录）" : string.Join(" | ", lines);
    }

    private static string FirstLine(string message) => message.Split('\n')[0].TrimEnd();
}
