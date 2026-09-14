namespace LabelFrame.Bootstrapper.Ba;

using System.Windows.Forms;
using LabelFrame.Bootstrapper.Wizard;
using WixToolset.BootstrapperApplicationApi;

/// <summary>LabelFrame 引导 BA（WiX Burn out-of-proc，决策 #122）：五步问卷 + dry-run——问卷答案写入 Burn 变量后 <see cref="IEngine.Plan"/> 只计划不执行，绝不 Apply。</summary>
internal sealed class LabelFrameBootstrapperBa : BootstrapperApplication
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(30);

    private readonly object _plannedPackagesLock = new();
    private readonly List<PlannedPackage> _plannedPackages = [];
    private readonly TaskCompletionSource<int> _detectCompleted = CreateSource();
    private readonly TaskCompletionSource<int> _planCompleted = CreateSource();

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
        PlanComplete += (_, args) => _planCompleted.TrySetResult(args.Status);
    }

    /// <summary>失败退出码（Windows 安装致命错误惯例 ERROR_INSTALL_FAILURE = 1603）。</summary>
    private const int ExitCodeFailure = 1603;

    protected override void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // UI 线程未处理异常显式诊断（迭代 60 返修：异常不得被吞）：写 Burn 日志 + 对话框后退出，不静默继续
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

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

        // 触发 Detect（只读探测已装包状态；问卷本身不依赖其结果，Plan 前必须 Detect 完成）
        engine.Detect();
        Application.Run(wizard);

        // 问卷关闭即结束 BA（dry-run：全程未调用 Apply，无任何下载 / 安装）
        engine.Quit(exitCode);
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

    /// <summary>dry-run 主入口：问卷答案 → Burn 变量（DESIGN §6.3 变量契约）→ 引擎 Plan（只计划不执行）。</summary>
    public async Task<PlanPreview> PreviewPlanAsync(WizardSession session)
    {
        var plan = session.BuildPlan();
        var variables = BundleVariableMap.ToVariables(plan);

        engine.SetVariableString(BundleVariableMap.PresetVariable, variables[BundleVariableMap.PresetVariable], false);
        engine.SetVariableNumeric(BundleVariableMap.ServerVariable, ParseFlag(variables[BundleVariableMap.ServerVariable]));
        engine.SetVariableNumeric(BundleVariableMap.ClientVariable, ParseFlag(variables[BundleVariableMap.ClientVariable]));
        engine.SetVariableNumeric(BundleVariableMap.WebUiVariable, ParseFlag(variables[BundleVariableMap.WebUiVariable]));
        engine.SetVariableNumeric(BundleVariableMap.ZebraPluginVariable, ParseFlag(variables[BundleVariableMap.ZebraPluginVariable]));

        await WaitStageAsync(_detectCompleted.Task, "包探测（Detect）").ConfigureAwait(true);
        engine.Plan(LaunchAction.Install, BundleScope.Default);
        var planStatus = await WaitStageAsync(_planCompleted.Task, "安装计划（Plan）").ConfigureAwait(true);

        lock (_plannedPackagesLock)
        {
            return new PlanPreview(plan, [.. _plannedPackages], planStatus);
        }
    }

    /// <summary>记录引擎日志（供 Burn 日志（%TEMP%\\LabelFrame*.log）回溯问卷决策）。</summary>
    public void Log(string message) => engine.Log(LogLevel.Standard, message);

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

/// <summary>dry-run 预览结果：解析器拓扑计划 + Burn 引擎计划状态（均未执行）。</summary>
internal sealed record PlanPreview(Topology.TopologyPlan Plan, IReadOnlyList<PlannedPackage> PlannedPackages, int PlanStatus);
