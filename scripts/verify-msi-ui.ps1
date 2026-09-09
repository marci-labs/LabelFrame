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
    $seqSql = "SELECT Action, Sequence FROM InstallUISequence WHERE Action = 'RuntimeMissingDlg' OR Action = 'WebView2MissingDlg' OR Action = 'ClearDataDlg' OR Action = 'WelcomeDlg' OR Action = 'LicenseAgreementDlg' OR Action = 'InstallDirDlg' OR Action = 'VerifyReadyDlg' OR Action = 'ExitDialog' OR Action = 'ExecuteAction' ORDER BY Sequence"
    $seq = Invoke-MsiQuery $db $seqSql
    $seq | ForEach-Object { Write-Host ("  {0} = {1}" -f $_[0], $_[1]) }

    Write-Host '-- 品牌二进制（WixUI 位图 / 图标 / 许可）--'
    Invoke-MsiQuery $db 'SELECT Name FROM Binary' | Where-Object { $_[0] -like 'WixUI*' } | ForEach-Object { Write-Host ("  {0}" -f $_[0]) }

    Write-Host '-- ARP 属性（控制面板）--'
    Invoke-MsiQuery $db 'SELECT Property, Value FROM Property' | Where-Object { $_[0] -like 'ARP*' } | ForEach-Object { Write-Host ("  {0} = {1}" -f $_[0], $_[1]) }

    # 安装选项页 / 中文文案 / 开机自启注册表（仅 Client）
    Write-Host '-- 选项页与链路（Client）--'
    # 直读记录循环（Invoke-MsiQuery 对单行结果的嵌套数组会被 PS 展平，索引会错位）
    $optView = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Control, Text FROM Control WHERE Dialog_ = 'OptionsDlg' AND Control = 'AutoStartCheck'"))
    $optView.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $optView, $null) | Out-Null
    $optRec = $optView.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $optView, $null)
    $optView.GetType().InvokeMember('Close', 'InvokeMethod', $null, $optView, $null) | Out-Null
    if ($null -ne $optRec) {
        $checkText = $optRec.GetType().InvokeMember('StringData', 'GetProperty', $null, $optRec, @(2))
        if ($checkText -notlike '*LabelFrame*') { throw '选项页复选框文本异常' }
        Write-Host '  选项页复选框：OK（开机自动启动 LabelFrame）'
        # Event 为 MSI SQL 保留字：全表拉取后客户端过滤
        $chain = Invoke-MsiQuery $db 'SELECT * FROM ControlEvent' |
            Where-Object { $_[0] -eq 'InstallDirDlg' -and $_[1] -eq 'Next' -and $_[3] -eq 'OptionsDlg' }
        if ($chain.Count -eq 0) { throw '向导链路缺失：InstallDirDlg.Next 应跳转 OptionsDlg' }
        Write-Host '  向导链路：目录页 Next -> 选项页 -> 确认页 OK'
        $reg = Invoke-MsiQuery $db 'SELECT * FROM Registry' | Where-Object { $_[2] -like '*CurrentVersion*Run*' }
        if ($reg.Count -gt 0) { Write-Host ('  Run 键：' + $reg[0][3] + ' = ' + $reg[0][4]) } else { throw '开机自启 Run 键缺失' }
        $autoStart = Invoke-MsiQuery $db "SELECT Value FROM Property WHERE Property = 'AUTO_START'"
        Write-Host ('  AUTO_START 默认：' + $autoStart[0][0])
    }
    else { Write-Host '  （无选项页——Server 包跳过本节）' }

    Write-Host '-- 中文文案抽查（WelcomeDlg）--'
    $wv = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Text FROM Control WHERE Dialog_ = 'WelcomeDlg' AND Control = 'Next'"))
    $wv.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $wv, $null) | Out-Null
    $wr = $wv.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $wv, $null)
    $wv.GetType().InvokeMember('Close', 'InvokeMethod', $null, $wv, $null) | Out-Null
    $nextText = $wr.GetType().InvokeMember('StringData', 'GetProperty', $null, $wr, @(1))
    Write-Host ('  欢迎页下一步按钮：' + $nextText)
    if ($nextText -notlike '*下一步*') { throw '向导文案未中文化（-culture zh-cn 未生效）' }

    # 关键断言：向导齐全 + 运行时检查在欢迎页之前
    # 取行模式说明：管道过滤后的单行结果是裸行数组，[1] = 该行第 2 列（序号）；
    # 转数值用 [int]::Parse——非数值直接抛错（PS 的 [int] 比较转换失败会静默回退字符串比较，形成空转断言）
    $dialogs = Invoke-MsiQuery $db 'SELECT Dialog FROM Dialog' | ForEach-Object { $_[0] }
    foreach ($required in @('WelcomeDlg', 'LicenseAgreementDlg', 'InstallDirDlg', 'VerifyReadyDlg', 'ExitDialog', 'RuntimeMissingDlg', 'WebView2MissingDlg', 'ClearDataDlg')) {
        if ($required -notin $dialogs) { throw "缺少对话框：$required" }
    }
    $runtimeSeq = ($seq | Where-Object { $_[0] -eq 'RuntimeMissingDlg' })[1]
    $welcomeSeq = ($seq | Where-Object { $_[0] -eq 'WelcomeDlg' })[1]
    if ([int]::Parse($runtimeSeq) -ge [int]::Parse($welcomeSeq)) { throw "RuntimeMissingDlg($runtimeSeq) 应早于 WelcomeDlg($welcomeSeq)" }
    $webview2Seq = ($seq | Where-Object { $_[0] -eq 'WebView2MissingDlg' })[1]
    if ([int]::Parse($webview2Seq) -ge [int]::Parse($welcomeSeq)) { throw "WebView2MissingDlg($webview2Seq) 应早于 WelcomeDlg($welcomeSeq)" }
    if ([int]::Parse($webview2Seq) -le [int]::Parse($runtimeSeq)) { throw "WebView2MissingDlg($webview2Seq) 应晚于 RuntimeMissingDlg($runtimeSeq)（两者都缺时先引导 .NET）" }
    $clearSeq = ($seq | Where-Object { $_[0] -eq 'ClearDataDlg' })[1]
    $execSeq = ($seq | Where-Object { $_[0] -eq 'ExecuteAction' })[1]
    if ([int]::Parse($clearSeq) -ge [int]::Parse($execSeq)) { throw "ClearDataDlg($clearSeq) 应早于 ExecuteAction($execSeq)，否则勾选属性传不进清理动作" }

    # WebView2 运行时检测契约（迭代 44）：双注册表视图 + LaunchCondition 拦截 + 向导内重新检测
    $regLocators = Invoke-MsiQuery $db 'SELECT * FROM RegLocator' | Where-Object { ($_ | Where-Object { "$_" -like '*EdgeUpdate*' }).Count -gt 0 }
    if ($regLocators.Count -lt 2) { throw "WebView2 注册表检测应含 per-machine / per-user 双视图（RegLocator EdgeUpdate 行数 $($regLocators.Count)）" }
    Write-Host "WebView2 注册表检测：$($regLocators.Count) 个 EdgeUpdate 视图 OK"
    $webview2Launch = Invoke-MsiQuery $db 'SELECT Condition, Description FROM LaunchCondition' |
        Where-Object { "$($_[0])" -like '*WEBVIEW2*' }
    if ($webview2Launch.Count -lt 1) { throw '缺少 WebView2 LaunchCondition（静默 / 基础 UI 拦截提示）' }
    Write-Host 'WebView2 LaunchCondition：OK'
    $customActions = Invoke-MsiQuery $db 'SELECT Action FROM CustomAction' | ForEach-Object { $_[0] }
    if ('RecheckWebView2' -notin $customActions) { throw '缺少 RecheckWebView2 自定义动作（重新检测）' }
    $retryChain = Invoke-MsiQuery $db 'SELECT * FROM ControlEvent' |
        Where-Object { $_[0] -eq 'WebView2MissingDlg' -and $_[1] -eq 'RetryButton' -and $_[3] -eq 'RecheckWebView2' }
    if ($retryChain.Count -lt 1) { throw 'WebView2MissingDlg.RetryButton 应触发 RecheckWebView2（装完运行时无需重启安装程序）' }
    Write-Host 'WebView2 缺失对话框：重新检测链路 OK'

    Write-Host "断言通过：向导齐全，运行时检查（.NET $runtimeSeq / WebView2 $webview2Seq）先于欢迎页（$welcomeSeq）。"
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
