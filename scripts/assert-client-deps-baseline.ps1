# Client 发布工件关键依赖「不低于上一发版基线」回归断言（迭代 64 返修，#57 AC-01，决策 #130）
#
# 背景：v0.27.0 Client 发布闭包较 v0.26.0 降版（SkiaSharp.dll 3.119.2.0→3.119.1.0、
# Microsoft.Extensions.DependencyModel.dll 10.0.726.21808→8.0.23.53103——0.26 的高版本是
# Zebra.Printer SDK 传递钉定顺带抬升的，迭代 63（#123）将 SDK 外置化移出 WinHost 闭包后，
# 剩余引用回落到本仓自钉低版），叠加 packaging/generate-files.ps1 对同名文件生成确定性
# 组件 GUID（相对路径 + 盐），触发 Windows Installer 组件规则「同组件 keyfile 版本只升不降」
# 拒绝安装降版文件；同次 MajorUpgrade 卸载旧产品又删除旧文件 → 覆盖升级后两文件净缺失、
# WinHost 首启即崩（System.IO.FileNotFoundException: SkiaSharp）。
#
# 本脚本对 Client 发布产物（-PublishDir）或 Client MSI File 表（-MsiPath）断言关键程序集
# 版本不低于发版基线；已挂接 scripts/build-msi.ps1（CI「MSI 结构断言」必需检查与 release.yml
# 发版链共用该入口），降版时构建直接失败，防止再次静默出包。
#
# 维护口径（决策 #130）：基线 = 上一发版 Release 工件实测值；每次发版后按当版实测**只升不降**
# 同步上调（可用 -Snapshot 打印当前值便于抄录）。Windows Installer 组件规则是硬约束，
# 基线禁止下调；如确需更换依赖实现（文件名变化），走 DESIGN 决策。
#
# 用法：
#   powershell -File scripts/assert-client-deps-baseline.ps1 -MsiPath artifacts\LabelFrame-Client-0.0.1.msi
#   powershell -File scripts/assert-client-deps-baseline.ps1 -PublishDir artifacts\client\win-x64
#   powershell -File scripts/assert-client-deps-baseline.ps1 -MsiPath <msi> -Snapshot   # 打印不判定
param(
    [string]$MsiPath = '',
    [string]$PublishDir = '',
    [switch]$Snapshot
)
$ErrorActionPreference = 'Stop'
if (-not $MsiPath -and -not $PublishDir) { throw '必须指定 -MsiPath 或 -PublishDir 之一' }

# 发版基线（v0.26.0 Release MSI File 表实测，2026-09-16 验收取证）：
# - SkiaSharp.dll：0.26 = 3.119.2.0（当时由 Zebra SDK 传递钉定，现为 Rendering 显式自钉）
# - Microsoft.Extensions.DependencyModel.dll：0.26 = 10.0.726.21808（同上，现为 WinHost 显式自钉）
# - libSkiaSharp.dll：原生库，MSI File 表无版本列（未触发组件版本规则，0.27 覆盖升级实际成功），
#   按在表断言（Min 留空 = 仅断言存在，不做版本比较）
$baseline = @(
    @{ Name = 'SkiaSharp.dll'; Min = '3.119.2.0' },
    @{ Name = 'Microsoft.Extensions.DependencyModel.dll'; Min = '10.0.726.21808' },
    @{ Name = 'libSkiaSharp.dll'; Min = '' }
)

function Get-FileVersionString([string]$path) {
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($vi.FileVersion) { return $vi.FileVersion }
    return ''
}

# 收集实测值：文件长名 -> 版本字符串（可能为空）
$actual = @{}
if ($MsiPath) {
    if (-not (Test-Path $MsiPath)) { throw "MSI 不存在：$MsiPath" }
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $MsiPath).Path, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @('SELECT FileName, Version FROM File'))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $rec) { break }
        # FileName 形如 "LF000012.DLL|SkiaSharp.dll"（短名|长名），取长名
        $name = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(1))
        $ver = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(2))
        if ($name -like '*|*') { $name = ($name -split '\|')[-1] }
        if (-not $actual.ContainsKey($name)) { $actual[$name] = "$ver" }
        if ($rec) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($rec); $rec = $null }
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    # 显式释放 COM RCW（#199）：本脚本被 build-msi.ps1 进程内调用（& 调用），Database 只读句柄
    # 随 RCW 存活持有文件共享读锁——GC 未及时回收时，紧随其后的 signtool 签名写 MSI 即被拒
    # （「The file is being used by another process」）；v0.28.0 前签名从不执行故未暴露。
    foreach ($o in @($view, $db, $installer)) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($o) }
    $view = $db = $installer = $null
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
} else {
    if (-not (Test-Path (Join-Path $PublishDir 'LabelFrame.WinHost.exe'))) { throw "发布目录无效：$PublishDir" }
}

$failures = @()
foreach ($entry in $baseline) {
    $name = $entry.Name
    if ($MsiPath) {
        if (-not $actual.ContainsKey($name)) {
            $failures += "  缺失：$name 不在 MSI File 表（依赖被整条移出发布闭包？）"
            continue
        }
        $ver = $actual[$name]
    } else {
        $p = Join-Path $PublishDir $name
        if (-not (Test-Path $p)) {
            $failures += "  缺失：$name 不在发布目录（依赖被整条移出发布闭包？）"
            continue
        }
        $ver = Get-FileVersionString $p
    }
    if ($Snapshot) {
        Write-Host ("snapshot: {0} = {1}" -f $name, $(if ($ver) { $ver } else { '(无版本信息)' }))
        continue
    }
    if (-not $entry.Min) {
        Write-Host ("  {0}: 在工件内，无版本基线，按存在断言通过（实测：{1}）" -f $name, $(if ($ver) { $ver } else { '无版本信息' }))
        continue
    }
    if (-not $ver) {
        $failures += ("  降版：{0} 无版本信息（期望 >= {1}）" -f $name, $entry.Min)
        continue
    }
    $minV = [version]$entry.Min
    $actV = [version]$ver
    if ($actV -ge $minV) {
        Write-Host ("  {0}: {1} >= 基线 {2} OK" -f $name, $ver, $entry.Min)
    } else {
        $failures += ("  降版：{0} = {1} < 基线 {2}（Windows Installer 组件规则拒装降版 keyfile，" -f $name, $ver, $entry.Min) + "覆盖升级将净丢失该文件——检查依赖版本固化）"
    }
}

if ($Snapshot) {
    Write-Host 'snapshot 完成（未做判定）。'
    return
}
if ($failures.Count -gt 0) {
    Write-Host '依赖基线断言失败（发布工件降版）：' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw ("依赖降版回归断言失败：{0} 项（基线见 scripts/assert-client-deps-baseline.ps1 头注，决策 #130）" -f $failures.Count)
}
Write-Host ("依赖基线断言通过：{0} 项关键程序集不低于上一发版基线。" -f $baseline.Count)
