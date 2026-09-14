namespace LabelFrame.Bootstrapper.Ba;

using System.Windows.Forms;
using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Wizard;
using WixToolset.BootstrapperApplicationApi;

/// <summary>
/// LabelFrame 引导 BA（WiX Burn out-of-proc，决策 #122 / #124）：五步问卷（只读）→ 确认页「安装」→
/// <see cref="IEngine.Plan"/> + <see cref="IEngine.Apply"/> 真装。执行边界契约见 DESIGN §6.3 / §6.9（确认前绝不 Apply）。
/// </summary>
internal sealed class LabelFrameBootstrapperBa : BootstrapperApplication
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(30);

    private readonly object _plannedPackagesLock = new();
    private readonly List<PlannedPackage> _plannedPackages = [];
    private readonly TaskCompletionSource<int> _detectCompleted = CreateSource();

    /// <summary>每轮执行（含重试）重置的等待源：Plan / Apply。</summary>
    private readonly object _runLock = new();
    private TaskCompletionSource<int> _planCompleted = CreateSource();
    private TaskCompletionSource<int> _applyCompleted = CreateSource();

    /// <summary>安装状态快照（引擎事件线程写、UI 线程读；整体替换保证一致视图）。</summary>
    private readonly object _stateLock = new();
    private InstallState _state = new();

    public LabelFrameBootstrapperBa()
    {
        DetectComplete += (_, args) => _detectCompleted.TrySetResult(args.Status);
        PlanPackageComplete += (_, args) =>
        {
            lock (_plannedPackagesLock)
            {
                _plannedPackages.Add(new PlannedPackage(args.PackageId, args.Requested));
            }
        };
        PlanComplete += (_, args) =>
        {
            lock (_runLock)
            {
                _planCompleted.TrySetResult(args.Status);
            }
        };
        ApplyComplete += (_, args) =>
        {
            lock (_runLock)
            {
                _applyCompleted.TrySetResult(args.Status);
            }

            UpdateState(state => state with
            {
                Phase = args.Status == 0 ? InstallPhase.Completed : InstallPhase.Failed,
                ApplyStatus = args.Status,
                Restart = args.Restart,
                OverallPercentage = 100,
            });
        };

        // ---- 进度 / 分包事件（DESIGN §6.9 UI 事件映射；多源下载体验完整消费属 #54 修订范围） ----
        CachePackageBegin += (_, args) => UpdateState(state => state with
        {
            Packages = WithPackage(state, args.PackageId, new PackageRunState(PackagePhase.Downloading, 0)),
            CurrentPackageId = args.PackageId,
        });
        CacheAcquireProgress += (_, args) => UpdateState(state => state with
        {
            Phase = InstallPhase.Downloading,
            CurrentPackageId = args.PackageOrContainerId,
            OverallPercentage = Math.Max(state.OverallPercentage, args.OverallPercentage),
        });
        CachePackageComplete += (_, args) => UpdateState(state => state with
        {
            Packages = WithPackage(state, args.PackageId, new PackageRunState(args.Status == 0 ? PackagePhase.Downloaded : PackagePhase.Failed, args.Status)),
        });

        ExecutePackageBegin += (_, args) =>
        {
            if (args.ShouldExecute)
            {
                UpdateState(state => state with
                {
                    Packages = WithPackage(state, args.PackageId, new PackageRunState(PackagePhase.Installing, 0)),
                    CurrentPackageId = args.PackageId,
                });
            }
        };
        ExecuteProgress += (_, args) => UpdateState(state => state with
        {
            Phase = InstallPhase.Installing,
            OverallPercentage = Math.Max(state.OverallPercentage, args.OverallPercentage),
        });
        ExecutePackageComplete += (_, args) => UpdateState(state => state with
        {
            Packages = WithPackage(state, args.PackageId, new PackageRunState(args.Status == 0 ? PackagePhase.Succeeded : PackagePhase.Failed, args.Status)),
        });

        // 引擎错误（下载失败 / 包退出码非零等）：追加中文报告素材，不打断流程（失败判定以阶段 Status 为准）
        Error += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.ErrorMessage))
            {
                AppendError($"[{args.PackageId ?? "引擎"}] 0x{args.ErrorCode:X8} {args.ErrorMessage}");
            }
        };
    }

    /// <summary>失败退出码（Windows 安装致命错误惯例 ERROR_INSTALL_FAILURE = 1603）。</summary>
    private const int ExitCodeFailure = 1603;

    /// <summary>安装状态变化通知（引擎事件线程触发；UI 订阅方自行封送到 UI 线程）。</summary>
    public event Action? StateChanged;

    /// <summary>当前安装状态快照（线程安全读取）。</summary>
    public InstallState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    /// <summary>启动时的运行时探测结果（确认页「已装则跳过」标注与完成页摘要共用）。</summary>
    public RuntimeProbeResult RuntimeStatus { get; private set; } = new(false, null, false);

    protected override void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // UI 线程未处理异常显式诊断（迭代 60 返修：异常不得被吞）：写 Burn 日志 + 对话框后退出，不静默继续
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // 运行时探测变量（链内 DetectCondition 消费，DESIGN §6.9）：必须在 engine.Detect() 之前写入；
        // 探测异常按「未装」处理（fail-open 到计划安装，运行时安装器自身幂等兜底）
        try
        {
            RuntimeStatus = new RuntimeProbe().Probe();
            Log($"运行时探测：.NET Desktop Runtime = {(RuntimeStatus.DesktopRuntimeInstalled ? $"已装 {RuntimeStatus.DesktopRuntimeVersion}" : "未装")}；WebView2 = {(RuntimeStatus.WebView2Installed ? "已装" : "未装")}");
        }
        catch (Exception ex)
        {
            Log($"运行时探测失败（按未装处理，引擎兜底）：{ex}");
        }

        engine.SetVariableNumeric(BundleVariableMap.DesktopRuntimeVariable, RuntimeStatus.DesktopRuntimeInstalled ? 1 : 0);
        engine.SetVariableNumeric(BundleVariableMap.WebView2Variable, RuntimeStatus.WebView2Installed ? 1 : 0);

        using var wizard = CreateWizardOrExit();
        if (wizard is null)
        {
            return; // 装配失败已显式处理（诊断 + 非零退出）
        }

        var exitCode = 0;
        Application.ThreadException += (_, args) =>
        {
            Log($"向导 UI 未处理异常：{args.Exception}");
            MessageBox.Show(
                wizard,
                $"向导发生未处理异常，安装引导即将退出。\n\n{args.Exception.GetType().Name}: {args.Exception.Message}\n\n详情已写入安装日志。",
                "LabelFrame 安装引导",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            exitCode = ExitCodeFailure;
            wizard.Close();
        };

        // 触发 Detect（只读探测已装包状态；Plan 前必须 Detect 完成）
        engine.Detect();
        Application.Run(wizard);

        // 问卷关闭即结束 BA；执行过安装则以上轮 Apply 状态为准（0 = 成功，非零传给引擎退出码）
        lock (_stateLock)
        {
            if (_state.Phase is InstallPhase.Completed or InstallPhase.Failed)
            {
                exitCode = _state.ApplyStatus;
            }
        }

        engine.Quit(exitCode);
    }

    /// <summary>安装主入口（确认页「安装」触发）：问卷答案 → Burn 变量（含落位目标）→ Plan → Apply → 终态。</summary>
    /// <remarks>失败后可再次调用（重试 = 全新 Plan + Apply；链内已装包由引擎检测跳过，幂等口径见 §6.9）。</remarks>
    public async Task<InstallState> ExecuteInstallAsync(WizardSession session, IntPtr windowHandle)
    {
        lock (_runLock)
        {
            _planCompleted = CreateSource();
            _applyCompleted = CreateSource();
        }

        lock (_plannedPackagesLock)
        {
            _plannedPackages.Clear();
        }

        SetState(new InstallState { Phase = InstallPhase.Planning });

        var plan = session.BuildPlan();
        var variables = BundleVariableMap.ToVariables(plan);
        WriteVariablesToEngine(variables);

        await WaitStageAsync(_detectCompleted.Task, "包探测（Detect）").ConfigureAwait(true);
        engine.Plan(LaunchAction.Install, BundleScope.Default);
        var planStatus = await WaitStageAsync(CurrentPlanSource().Task, "安装计划（Plan）").ConfigureAwait(true);

        if (planStatus != 0)
        {
            return FailAs(unchecked((int)0x8000F0DE), $"安装计划（Plan）阶段失败（0x{planStatus:X8}），请查看安装日志。");
        }

        SetState(State with { Phase = InstallPhase.Downloading, OverallPercentage = 0 });
        Log($"开始安装：预设 {variables[BundleVariableMap.PresetVariable]}，组件 [{string.Join(",", plan.Components.Select(item => item.Component.Id))}]");

        // Apply 无超时：厂商直链下载耗时取决于网络；取消 / 退出由向导关闭与引擎 Quit 承担。
        // 失败不走异常路径——ApplyComplete 事件已把真实引擎状态写入快照（失败报告展示真实状态码与回滚语义）
        engine.Apply(windowHandle);
        await CurrentApplySource().Task.ConfigureAwait(true);

        lock (_stateLock)
        {
            return _state;
        }
    }

    /// <summary>记录引擎日志（供 Burn 日志（%TEMP%\LabelFrame*.log）回溯问卷决策与执行序列）。</summary>
    public void Log(string message) => engine.Log(LogLevel.Standard, message);

    /// <summary>Burn 日志文件路径（失败报告展示；不可得时返回 null，由 UI 兜底 %TEMP% 通配说明）。</summary>
    public string? GetBundleLogPath()
    {
        try
        {
            var path = engine.GetVariableString("WixBundleLog");
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null; // 变量尚未建立（日志未启动）等——兜底文案处理
        }
    }

    /// <summary>构造失败终态快照（Plan / Apply 前置阶段异常等未走到引擎事件的失败）。</summary>
    public InstallState FailAs(int status, string message)
    {
        AppendError(message);
        return UpdateState(state => state with { Phase = InstallPhase.Failed, ApplyStatus = status });
    }

    private void WriteVariablesToEngine(IReadOnlyDictionary<string, string> variables)
    {
        engine.SetVariableString(BundleVariableMap.PresetVariable, variables[BundleVariableMap.PresetVariable], false);
        engine.SetVariableNumeric(BundleVariableMap.ServerVariable, ParseFlag(variables[BundleVariableMap.ServerVariable]));
        engine.SetVariableNumeric(BundleVariableMap.ClientVariable, ParseFlag(variables[BundleVariableMap.ClientVariable]));
        engine.SetVariableNumeric(BundleVariableMap.WebUiVariable, ParseFlag(variables[BundleVariableMap.WebUiVariable]));
        engine.SetVariableNumeric(BundleVariableMap.ZebraPluginVariable, ParseFlag(variables[BundleVariableMap.ZebraPluginVariable]));

        // 落位目标目录（InstallArguments 以 [变量] 引用，DESIGN §6.9 落位机制）
        engine.SetVariableString(BundleVariableMap.WebUiTargetDirVariable, BundleVariableMap.WebUiTargetDir(), false);
        engine.SetVariableString(BundleVariableMap.PluginZebraTargetDirVariable, BundleVariableMap.PluginZebraTargetDir(), false);
    }

    private TaskCompletionSource<int> CurrentPlanSource()
    {
        lock (_runLock)
        {
            return _planCompleted;
        }
    }

    private TaskCompletionSource<int> CurrentApplySource()
    {
        lock (_runLock)
        {
            return _applyCompleted;
        }
    }

    private void AppendError(string message) =>
        UpdateState(state => state with { Errors = [.. state.Errors, message] });

    /// <summary>复制并更新单包状态（net48 Dictionary 无 (IDictionary, IComparer) 构造重载）。</summary>
    private static Dictionary<string, PackageRunState> WithPackage(InstallState state, string packageId, PackageRunState run)
    {
        var packages = new Dictionary<string, PackageRunState>(StringComparer.Ordinal);
        foreach (var pair in state.Packages)
        {
            packages[pair.Key] = pair.Value;
        }

        packages[packageId] = run;
        return packages;
    }

    private void SetState(InstallState state)
    {
        lock (_stateLock)
        {
            _state = state;
        }

        StateChanged?.Invoke();
    }

    private InstallState UpdateState(Func<InstallState, InstallState> update)
    {
        InstallState updated;
        lock (_stateLock)
        {
            updated = update(_state);
            _state = updated;
        }

        StateChanged?.Invoke();
        return updated;
    }

    /// <summary>
    /// 装配向导窗体（含首页装配，迭代 60 返修）；失败时显式失败——诊断对话框 + Burn 日志 + 非零退出，
    /// 绝不带着未装配状态（-1 索引、空白内容区）进入消息循环（Issue #53 验收回流教训）。
    /// </summary>
    private Ui.WizardForm? CreateWizardOrExit()
    {
        try
        {
            return new Ui.WizardForm(this);
        }
        catch (Exception ex)
        {
            Log($"向导装配失败：{ex}");
            MessageBox.Show(
                $"向导装配失败，安装引导无法继续。\n\n{ex.GetType().Name}: {ex.Message}\n\n详情已写入安装日志。",
                "LabelFrame 安装引导",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            engine.Quit(ExitCodeFailure);
            return null;
        }
    }

    private static TaskCompletionSource<int> CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static long ParseFlag(string value) => value == "1" ? 1 : 0;

    private static async Task<int> WaitStageAsync(Task<int> stage, string stageName)
    {
        var completed = await Task.WhenAny(stage, Task.Delay(StageTimeout)).ConfigureAwait(true);
        if (completed != stage)
        {
            throw new TimeoutException($"{stageName}超时（{StageTimeout.TotalSeconds:0} 秒），请查看安装日志。");
        }

        var status = await stage.ConfigureAwait(true);
        if (status != 0)
        {
            throw new InvalidOperationException($"{stageName}失败（0x{status:X8}），请查看安装日志。");
        }

        return status;
    }
}

/// <summary>Burn Plan 阶段产出的单个包计划状态（RequestState）。</summary>
internal sealed record PlannedPackage(string PackageId, RequestState State);

/// <summary>单包执行状态（phase + 退出码）。</summary>
internal sealed record PackageRunState(PackagePhase Phase, int Status);

/// <summary>单包阶段。</summary>
internal enum PackagePhase
{
    /// <summary>计划纳入但尚未开始。</summary>
    Pending,

    /// <summary>下载 / 校验中。</summary>
    Downloading,

    /// <summary>下载完成（等待执行）。</summary>
    Downloaded,

    /// <summary>安装 / 落位执行中。</summary>
    Installing,

    /// <summary>执行成功（含落位幂等跳过）。</summary>
    Succeeded,

    /// <summary>执行失败（引擎已回滚）。</summary>
    Failed,
}

/// <summary>安装整体阶段。</summary>
internal enum InstallPhase
{
    /// <summary>尚未开始（问卷中）。</summary>
    Idle,

    /// <summary>引擎 Plan 中。</summary>
    Planning,

    /// <summary>下载 / 校验包。</summary>
    Downloading,

    /// <summary>执行安装链。</summary>
    Installing,

    /// <summary>Apply 成功完成。</summary>
    Completed,

    /// <summary>失败（引擎已回滚本次已执行包）。</summary>
    Failed,
}

/// <summary>安装状态快照（不可变整体替换）。</summary>
internal sealed record InstallState
{
    public InstallPhase Phase { get; init; }

    public int OverallPercentage { get; init; }

    public string? CurrentPackageId { get; init; }

    public IReadOnlyDictionary<string, PackageRunState> Packages { get; init; } =
        new Dictionary<string, PackageRunState>(StringComparer.Ordinal);

    public IReadOnlyList<string> Errors { get; init; } = [];

    public int ApplyStatus { get; init; }

    public ApplyRestart Restart { get; init; }
}
