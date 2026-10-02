# LabelFrame BA 向导前置断言（迭代 116 / Issue #265，AC-02——S1 场景重放）
#
# 目的：复现口径 S1 重放——由无前台权进程（Interactive 计划任务上下文）启动 Bootstrapper EXE，
#   采样断言 BA 向导主窗口在 -AssertSeconds（默认 5 秒，AC-02 口径）内成为前台。
#
# 采样器（移植 #265 复现采样器 fgmon，宿主机 C:\work\OpenCode\itenv\bug-uac-fg\ 同口径）：
#   每 0.9 秒记录 GetForegroundWindow 归属（句柄 / pid / 进程名 / 标题）＋ 被观察进程（BA）主窗口
#   句柄是否等于前台句柄（fg_match）；PASS = 启动后 5 秒内出现 fg_match=True（修复后组合置前成功形态）。
#   闪烁兜底（组合置前被拒时任务栏闪至前置）为视觉形态，不参与自动断言——由交互会话观察与
#   Burn 日志「向导首显置前：」行（-l 参数落盘）佐证。
#
# 用法（目标机交互会话内，Windows PowerShell 5.1+）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-wizard-foreground.ps1 -BundlePath <Bootstrapper.exe> `
#       [-Seconds 10] [-AssertSeconds 5] [-LogDir <目录>] [-BundleArgs '-l C:\replay\burn.log'] [-KeepRunning]
#   -BundleArgs：透传引导程序参数（如 -l 落盘 Burn 日志，取证「向导首显置前：」行）。
#   -KeepRunning：断言结束后不杀 BA 进程（人工继续观察闪烁 / 交互时用）。
#
# 无前台权启动者的构成（S1 复现形态，#265 复现评论口径）：本脚本经 Interactive 计划任务驱动——
#   任务进程即无前台权启动者。注册示例（目标机管理员 PowerShell）：
#     $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-ExecutionPolicy Bypass -File <本脚本全路径> -BundlePath <EXE 全路径> -LogDir <证据目录>"
#     $principal = New-ScheduledTaskPrincipal -UserId <当前用户> -LogonType Interactive
#     Register-ScheduledTask -TaskName 'LabelFrame-FG-Replay' -Action $action -Principal $principal | Out-Null
#     Start-ScheduledTask -TaskName 'LabelFrame-FG-Replay'
#   （跑毕 Unregister-ScheduledTask -TaskName 'LabelFrame-FG-Replay' -Confirm:$false 清理）
#
# 证据：<LogDir>\fg-assert.log（逐采样行 + 结论，行格式与 #265 复现日志一致便于对照）。
# 临时产物不进仓（artifacts/ 已 gitignore）；本脚本随仓版本化（重放可复现）。
param(
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [int]$Seconds = 10,
    [int]$AssertSeconds = 5,
    [string]$LogDir = '',
    [string]$BundleArgs = '',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
if (-not $LogDir) { $LogDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\bootstrapper-tests\wizard-foreground' }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$logPath = Join-Path $LogDir 'fg-assert.log'
$watch = 'LabelFrame.Bootstrapper.Ba' # BA 进程名（Burn 解压运行，见 #265 复现日志）

# ---------- Win32 采样（#265 复现采样器同款）----------
Add-Type -Namespace WizardFg -Name Native -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr window, out uint processId);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr window, System.Text.StringBuilder text, int max);
"@

function Get-ForegroundView {
    $handle = [WizardFg.Native]::GetForegroundWindow()
    if ($handle -eq [IntPtr]::Zero) { return @{ Handle = $handle; OwnerPid = 0; Name = '<none>'; Title = '' } }
    [uint32]$ownerPid = 0
    [void][WizardFg.Native]::GetWindowThreadProcessId($handle, [ref]$ownerPid)
    $name = '<gone>'
    $title = ''
    try {
        $proc = Get-Process -Id ([int]$ownerPid) -ErrorAction Stop
        $name = $proc.ProcessName
        $builder = New-Object System.Text.StringBuilder 256
        [void][WizardFg.Native]::GetWindowText($handle, $builder, 256)
        $title = $builder.ToString()
    } catch { }
    return @{ Handle = $handle; OwnerPid = [int]$ownerPid; Name = $name; Title = $title }
}

function Write-Log([string]$Line) {
    $Line | Tee-Object -FilePath $logPath -Append | Write-Host
}

# ---------- S1：无前台权上下文启动（本脚本进程即启动者）----------
$bundleItem = Get-Item -LiteralPath $BundlePath
Write-Log ("FGASSERT tag=s1 assertSeconds={0} seconds={1} bundle={2} start={3:o}" -f $AssertSeconds, $Seconds, $bundleItem.FullName, (Get-Date))
$initial = Get-ForegroundView
Write-Log ("{0:HH:mm:ss.fff} 初始前台：FG=h{1} pid={2} name={3} title={4}" -f (Get-Date), $initial.Handle, $initial.OwnerPid, $initial.Name, $initial.Title)

$launchTime = Get-Date
$engine = if ($BundleArgs) {
    Start-Process -FilePath $bundleItem.FullName -ArgumentList $BundleArgs -PassThru
} else {
    Start-Process -FilePath $bundleItem.FullName -PassThru
}
$firstMatchSeconds = $null
$samples = 0
try {
    $deadline = $launchTime.AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 900
        $samples++
        $fg = Get-ForegroundView
        $watchProc = Get-Process -Name $watch -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        $main = if ($watchProc) { [int64]$watchProc.MainWindowHandle } else { 0 }
        $fgMatch = ($watchProc -ne $null) -and ($fg.Handle -ne [IntPtr]::Zero) -and ([int64]$fg.Handle -eq $main)
        Write-Log ("{0:HH:mm:ss.fff} FG=h{1} pid={2} name={3} title={4} | watch:{5} main={6} fg_match={7}" -f `
            (Get-Date), $fg.Handle, $fg.OwnerPid, $fg.Name, $fg.Title, $watch, $main, $fgMatch)
        if ($fgMatch -and $null -eq $firstMatchSeconds) {
            $firstMatchSeconds = ((Get-Date) - $launchTime).TotalSeconds
        }
        if ($null -ne $firstMatchSeconds) { break } # 前置达成即停止采样（后续保持形态由交互会话观察）
    }
}
finally {
    if (-not $KeepRunning) {
        # 清理：BA 向导无人交互停留在首页，直接结束；引擎进程（Burn EXE）随 BA 退出后自行收尾，兜底再杀一次
        Get-Process -Name $watch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        if (-not $engine.HasExited) { Stop-Process -Id $engine.Id -Force -ErrorAction SilentlyContinue }
    }
}

# ---------- 断言（AC-02：启动后 AssertSeconds 秒内成为前台）----------
if ($null -ne $firstMatchSeconds -and $firstMatchSeconds -le $AssertSeconds) {
    Write-Log ("FGASSERT result=PASS 首次前置={0:F1}s（阈值 {1}s）samples={2} end={3:o}" -f $firstMatchSeconds, $AssertSeconds, $samples, (Get-Date))
    Write-Host '向导前置断言：PASS' -ForegroundColor Green
    exit 0
}
$firstMatchText = if ($null -ne $firstMatchSeconds) { '{0:F1}s' -f $firstMatchSeconds } else { '<全程未前置>' }
Write-Log ("FGASSERT result=FAIL 首次前置={0} samples={1}（兜底闪烁为视觉形态不参与断言；Burn 日志「向导首显置前：」行佐证）end={2:o}" -f $firstMatchText, $samples, (Get-Date))
Write-Host '向导前置断言：FAIL' -ForegroundColor Red
exit 1
