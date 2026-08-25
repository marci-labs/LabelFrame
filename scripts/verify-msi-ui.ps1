# 验证 MSI 内部结构：向导对话框表、UI 序列、品牌二进制、ARP 属性
# 用法：powershell -File scripts/verify-msi-ui.ps1 <msi 路径> [...]
param([Parameter(Mandatory = $true)][string[]]$MsiPaths)

$ErrorActionPreference = 'Stop'

function Invoke-MsiQuery {
    param($Database, [string]$Sql)
    $view = $Database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $Database, @($Sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = @()
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $rec) { break }
        $n = $rec.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $rec, $null)
        $vals = @()
        for ($i = 0; $i -lt $n; $i++) {
            $vals += $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @($i + 1))
        }
        $rows += , $vals
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $rows
}

foreach ($path in $MsiPaths) {
    Write-Host "=== $path ==="
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $path).Path, 0))

    Write-Host '-- Dialog 表（向导 + 自定义）--'
    Invoke-MsiQuery $db 'SELECT Dialog, Width, Height FROM Dialog ORDER BY Dialog' | ForEach-Object { Write-Host ("  {0}  ({1}x{2})" -f $_[0], $_[1], $_[2]) }

    Write-Host '-- InstallUISequence（关键顺序）--'
    # MSI SQL 不支持 IN，用 OR 链
    $seqSql = "SELECT Action, Sequence FROM InstallUISequence WHERE Action = 'RuntimeMissingDlg' OR Action = 'ClearDataDlg' OR Action = 'WelcomeDlg' OR Action = 'LicenseAgreementDlg' OR Action = 'InstallDirDlg' OR Action = 'VerifyReadyDlg' OR Action = 'ExitDialog' OR Action = 'ExecuteAction' ORDER BY Sequence"
    $seq = Invoke-MsiQuery $db $seqSql
    $seq | ForEach-Object { Write-Host ("  {0} = {1}" -f $_[0], $_[1]) }

    Write-Host '-- 品牌二进制（WixUI 位图 / 图标 / 许可）--'
    Invoke-MsiQuery $db 'SELECT Name FROM Binary' | Where-Object { $_[0] -like 'WixUI*' } | ForEach-Object { Write-Host ("  {0}" -f $_[0]) }

    Write-Host '-- ARP 属性（控制面板）--'
    Invoke-MsiQuery $db 'SELECT Property, Value FROM Property' | Where-Object { $_[0] -like 'ARP*' } | ForEach-Object { Write-Host ("  {0} = {1}" -f $_[0], $_[1]) }

    # 关键断言：向导齐全 + 运行时检查在欢迎页之前
    $dialogs = Invoke-MsiQuery $db 'SELECT Dialog FROM Dialog' | ForEach-Object { $_[0] }
    foreach ($required in @('WelcomeDlg', 'LicenseAgreementDlg', 'InstallDirDlg', 'VerifyReadyDlg', 'ExitDialog', 'RuntimeMissingDlg', 'ClearDataDlg')) {
        if ($required -notin $dialogs) { throw "缺少对话框：$required" }
    }
    $runtimeSeq = ($seq | Where-Object { $_[0] -eq 'RuntimeMissingDlg' })[1]
    $welcomeSeq = ($seq | Where-Object { $_[0] -eq 'WelcomeDlg' })[1]
    if ([int]$runtimeSeq -ge [int]$welcomeSeq) { throw "RuntimeMissingDlg($runtimeSeq) 应早于 WelcomeDlg($welcomeSeq)" }
    $clearSeq = ($seq | Where-Object { $_[0] -eq 'ClearDataDlg' })[0][1]
    $execSeq = ($seq | Where-Object { $_[0] -eq 'ExecuteAction' })[0][1]
    if ([int]$clearSeq -ge [int]$execSeq) { throw "ClearDataDlg($clearSeq) 应早于 ExecuteAction($execSeq)，否则勾选属性传不进清理动作" }
    Write-Host "断言通过：向导齐全，运行时检查（$runtimeSeq）先于欢迎页（$welcomeSeq）。"
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
