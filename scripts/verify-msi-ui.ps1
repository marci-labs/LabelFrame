# 验证 MSI 内部结构：向导对话框表、UI 序列、品牌二进制、ARP 属性；Client 包另验更新服务、运行中升级、运行时检测、
# 完成页启动与下载页按钮、清除用户数据、卸载时询问清除用户数据的链路。
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

# 与 Invoke-MsiQuery 相同，但把结果作为「行的数组」整体返回：单行结果的嵌套数组不会被展平，可按下标取列。
function Get-MsiRows {
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
    return , $rows
}

foreach ($path in $MsiPaths) {
    Write-Host "=== $path ==="
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $path).Path, 0))

    Write-Host '-- Dialog 表（向导 + 自定义）--'
    Invoke-MsiQuery $db 'SELECT Dialog, Width, Height FROM Dialog ORDER BY Dialog' | ForEach-Object { Write-Host ("  {0}  ({1}x{2})" -f $_[0], $_[1], $_[2]) }

    Write-Host '-- InstallUISequence（关键顺序）--'
    # MSI SQL 不支持 IN，用 OR 链
    $seqSql = "SELECT Action, Sequence FROM InstallUISequence WHERE Action = 'RuntimeMissingDlg' OR Action = 'WebView2MissingDlg' OR Action = 'ClearDataDlg' OR Action = 'WelcomeDlg' OR Action = 'LicenseAgreementDlg' OR Action = 'InstallDirDlg' OR Action = 'InstallLocationDlg' OR Action = 'VerifyReadyDlg' OR Action = 'ExitDialog' OR Action = 'ExecuteAction' ORDER BY Sequence"
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
        $events = Get-MsiRows $db 'SELECT * FROM ControlEvent'
        $locationControls = Get-MsiRows $db "SELECT Control, Type, Text FROM Control WHERE Dialog_ = 'InstallLocationDlg'"
        if ($locationControls.Count -eq 0) { throw '缺少只读的安装位置页 InstallLocationDlg' }
        if (@($locationControls | Where-Object { $_[0] -eq 'ChangeFolder' -or $_[1] -eq 'PathEdit' -or $_[1] -eq 'DirectoryList' }).Count -gt 0) { throw '安装位置页必须只读：不得有可编辑路径框、更改按钮或目录列表' }
        if (@($locationControls | Where-Object { $_[0] -eq 'InstallLocationText' -and $_[2] -eq '[ClientInstallDir]' }).Count -ne 1) { throw '安装位置页应只读展示固定安装目录 [ClientInstallDir]' }
        # 这一页只写「安装目录」和路径，不加解释：文字控件只允许标题与路径两个
        $locationTexts = @($locationControls | Where-Object { $_[1] -eq 'Text' })
        if ($locationTexts.Count -ne 2 -or @($locationTexts | Where-Object { $_[0] -eq 'Title' -and $_[2] -like '*安装目录' }).Count -ne 1) { throw '安装目录页只保留标题「安装目录」与路径，不加说明文字' }
        foreach ($link in @(@('InstallLocationDlg', 'Next', 'OptionsDlg'), @('OptionsDlg', 'Back', 'InstallLocationDlg'), @('InstallLocationDlg', 'Back', 'LicenseAgreementDlg'))) {
            if (@($events | Where-Object { $_[0] -eq $link[0] -and $_[1] -eq $link[1] -and $_[2] -eq 'NewDialog' -and $_[3] -eq $link[2] }).Count -lt 1) { throw "向导链路缺失：$($link[0]).$($link[1]) 应跳转 $($link[2])" }
        }
        # 同一按钮上登记顺序靠后的跳转生效：只读安装位置页这条路必须压过自带向导组指向可改目录页的默认跳转
        foreach ($bypass in @(@('LicenseAgreementDlg', 'Next', 'InstallLocationDlg'), @('VerifyReadyDlg', 'Back', 'OptionsDlg'))) {
            $viaLocation = @($events | Where-Object { $_[0] -eq $bypass[0] -and $_[1] -eq $bypass[1] -and $_[2] -eq 'NewDialog' -and $_[3] -eq $bypass[2] })
            $toLegacy = @($events | Where-Object { $_[0] -eq $bypass[0] -and $_[1] -eq $bypass[1] -and $_[2] -eq 'NewDialog' -and $_[3] -eq 'InstallDirDlg' })
            if ($viaLocation.Count -lt 1) { throw "向导链路缺失：$($bypass[0]).$($bypass[1]) 应跳转 $($bypass[2])" }
            if ($toLegacy.Count -eq 0) { throw "自带向导组的默认跳转形态变了（$($bypass[0]).$($bypass[1]) 不再指向可改目录页），请复核只读安装位置页的旁路接法" }
            foreach ($legacy in $toLegacy) {
                if ([int]::Parse($viaLocation[-1][5]) -le [int]::Parse($legacy[5])) { throw "只读安装位置页的跳转登记顺序必须大于自带向导组：$($bypass[0]).$($bypass[1])（$($viaLocation[-1][5]) <= $($legacy[5])）" }
            }
        }
        # 可改目录的自带页面只能从上面两个被旁路的按钮到达，出现别的入口就意味着安装位置又能被改
        $legacyEntries = @($events | Where-Object { $_[3] -eq 'InstallDirDlg' })
        if (@($legacyEntries | Where-Object { -not (($_[0] -eq 'LicenseAgreementDlg' -and $_[1] -eq 'Next') -or ($_[0] -eq 'VerifyReadyDlg' -and $_[1] -eq 'Back')) }).Count -gt 0) { throw '可改目录的自带页面出现了新的入口，安装位置可能被修改' }
        Write-Host '  安装位置页：只读展示固定目录 OK；向导链路：许可页 -> 安装位置页 -> 选项页 -> 确认页 OK'
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
    foreach ($required in @('WelcomeDlg', 'LicenseAgreementDlg', 'VerifyReadyDlg', 'ExitDialog', 'RuntimeMissingDlg', 'WebView2MissingDlg', 'ClearDataDlg')) {
        if ($required -notin $dialogs) { throw "缺少对话框：$required" }
    }
    if ($null -ne $optRec -and 'InstallLocationDlg' -notin $dialogs) { throw '缺少对话框：InstallLocationDlg' }
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

    # Client MSI 运行中升级协调（迭代 106 / #229）：安全退出必须早于 InstallValidate，且卸载跳过。
    if ($null -ne $optRec) {
        $updater = @(Invoke-MsiQuery $db 'SELECT Name, ServiceType, StartType, StartName, Arguments, Component_ FROM ServiceInstall' | Where-Object { $_[0] -eq 'LabelFrameClientUpdater' })
        if ($updater.Count -ne 1 -or $updater[0][1] -ne '16' -or $updater[0][2] -ne '2' -or $updater[0][3] -ne 'LocalSystem' -or $updater[0][4] -ne '--update-service') {
            throw 'Client MSI 必须安装唯一的 LocalSystem 自动启动升级服务，独立进程参数为 --update-service'
        }
        # 更新服务不由内建启动动作启动：StartServices 在服务启动失败时会按固定间隔反复重试。
        # 控制项只保留停止与卸载移除（不含「安装时启动」位 1）并等待完成。
        $serviceControl = Get-MsiRows $db "SELECT Name, Event, Wait FROM ServiceControl WHERE Name = 'LabelFrameClientUpdater'"
        if ($serviceControl.Count -ne 1 -or $serviceControl[0][1] -ne '162' -or $serviceControl[0][2] -ne '1') {
            throw '升级服务的控制项应只含停止与卸载移除并等待完成，不得再由内建动作在安装时启动'
        }
        $properties = @(Invoke-MsiQuery $db 'SELECT Property, Value FROM Property')
        foreach ($entry in @(@('LABELFRAME_MIN_SERVER_VERSION', '1.0.0'), @('LABELFRAME_MAX_SERVER_VERSION', '2.0.0'), @('LABELFRAME_RUNTIME_MAJOR', '10'))) {
            $property = @($properties | Where-Object { $_[0] -eq $entry[0] })
            if ($property.Count -ne 1 -or $property[0][1] -ne $entry[1]) { throw "升级兼容属性缺失或错误：$($entry[0])" }
        }
        $files = @(Invoke-MsiQuery $db 'SELECT File, FileName, Component_ FROM File')
        $hostFile = @($files | Where-Object { $_[0] -eq 'WinHostExe' })
        if ($hostFile.Count -ne 1 -or $hostFile[0][2] -ne $updater[0][5]) { throw '升级服务必须使用已安装的 WinHost 可执行文件组件' }
        foreach ($requiredFile in @('update-publisher.cer', 'LabelFrame.Updates.dll')) {
            if (@($files | Where-Object { ($_[1] -split '\|')[-1] -eq $requiredFile }).Count -ne 1) { throw "升级文件必须唯一打包：$requiredFile" }
        }
        Write-Host '受控升级：服务身份、启动与卸载行为、兼容属性、发布者证书与组件打包 OK'
        $executeRows = Invoke-MsiQuery $db 'SELECT Action, Condition, Sequence FROM InstallExecuteSequence'
        $shutdownRow = @($executeRows | Where-Object { $_[0] -eq 'ShutdownWinHost' })
        $validateRow = @($executeRows | Where-Object { $_[0] -eq 'InstallValidate' })
        $costRow = @($executeRows | Where-Object { $_[0] -eq 'CostFinalize' })
        if ($shutdownRow.Count -ne 1 -or $validateRow.Count -ne 1 -or $costRow.Count -ne 1) { throw 'Client MSI 缺少唯一的 ShutdownWinHost / CostFinalize / InstallValidate 序列行' }
        $shutdownSeq = [int]::Parse($shutdownRow[0][2])
        if ($shutdownSeq -le [int]::Parse($costRow[0][2]) -or $shutdownSeq -ge [int]::Parse($validateRow[0][2])) {
            throw "ShutdownWinHost($shutdownSeq) 必须位于 CostFinalize 与 InstallValidate 之间"
        }
        # 是否执行不能只看条件里有没有某些字：把条件翻译后对三种安装状态逐一求值。
        # 防回归警示：Restart Manager 关不掉托盘里的客户端，手动卸载必须执行安全退出；
        # 只有升级过程中旧产品的卸载子会话跳过——新产品的会话已经让客户端退出。
        function Test-HostExitState([string]$condition, [bool]$removing, [bool]$upgrading) {
            # 空条件恒真，与安装器一致
            if ([string]::IsNullOrWhiteSpace($condition)) { return $true }
            $expr = $condition.Replace('REMOVE~="ALL"', '($removing)')
            $expr = [regex]::Replace($expr, '(?<![A-Za-z0-9_])UPGRADINGPRODUCTCODE(?![A-Za-z0-9_])', '($upgrading)')
            $expr = $expr -creplace '\bAND\b', '-and' -creplace '\bOR\b', '-or' -creplace '\bNOT\b', '-not'
            $left = $expr -creplace '\$(removing|upgrading)|-and|-or|-not|[()\s]', ''
            if ($left.Length -ne 0) { throw "安全退出动作的执行条件用到了本断言不会求值的语法：$condition" }
            [bool](Invoke-Expression $expr)
        }
        $hostExitStates = @(
            @{ Name = '全新安装、修复、升级中新产品的会话'; Removing = $false; Upgrading = $false; Runs = $true },
            @{ Name = '手动卸载'; Removing = $true; Upgrading = $false; Runs = $true },
            @{ Name = '升级过程中旧产品的卸载子会话'; Removing = $true; Upgrading = $true; Runs = $false }
        )
        foreach ($state in $hostExitStates) {
            if ((Test-HostExitState $shutdownRow[0][1] $state.Removing $state.Upgrading) -ne $state.Runs) {
                throw "安全退出动作在「$($state.Name)」时应当$(if ($state.Runs) { '执行' } else { '跳过' })，当前条件不是这样：$($shutdownRow[0][1])"
            }
        }
        $quietAction = @(Invoke-MsiQuery $db "SELECT Action, Type, Target, Source FROM CustomAction WHERE Action = 'ShutdownWinHost'")
        if ($quietAction.Count -ne 1 -or $quietAction[0][1] -ne '1' -or $quietAction[0][2] -ne 'WixQuietExec' -or $quietAction[0][3] -ne 'Wix4UtilCA_X64') { throw 'ShutdownWinHost 必须是同步、失败阻断的 x64 QuietExec DLL 动作' }
        $shutdownAction = @(Invoke-MsiQuery $db "SELECT Action, Type, Target, Source FROM CustomAction WHERE Action = 'SetShutdownWinHostCommand'")
        if ($shutdownAction.Count -ne 1 -or $shutdownAction[0][1] -ne '51' -or $shutdownAction[0][3] -ne 'WixQuietExecCmdLine') { throw '退出预检命令必须通过专用属性传给 QuietExec' }
        $setCommandRow = @($executeRows | Where-Object { $_[0] -eq 'SetShutdownWinHostCommand' })
        if ($setCommandRow.Count -ne 1 -or [int]$setCommandRow[0][2] -le [int]$costRow[0][2] -or [int]$setCommandRow[0][2] -ge $shutdownSeq -or $setCommandRow[0][1] -ne $shutdownRow[0][1]) { throw '预检命令赋值必须同条件位于 CostFinalize 与退出动作之间' }
        if (-not $shutdownAction[0][2].StartsWith('"[System64Folder]WindowsPowerShell\v1.0\powershell.exe" ')) { throw '预检必须使用固定的系统 PowerShell 路径' }
        foreach ($required in @('/api/jobs?limit=500', '/api/host/shutdown', 'Printing', '15 秒内安全退出')) {
            if ($shutdownAction[0][2] -notlike "*$required*") { throw "ShutdownWinHost MSI 命令缺少安全退出逻辑：$required" }
        }
        if ($customActions -contains 'KillWinHost') { throw 'Client MSI 不得再用 taskkill 强制结束 WinHost' }
        # 手动卸载也走这段脚本：提示不能只提升级
        if ($shutdownAction[0][2] -match '升级') { throw '安全退出预检的提示不能只提升级：手动卸载也会执行这段脚本' }

        # MSI 属性赋值会展开 Formatted 语法，必须验证展开后的脚本仍可解析。
        $caTarget = $shutdownAction[0][2]
        if ($caTarget -match '\{\}') { throw 'ShutdownWinHost 命令含空花括号 {}（Formatted 语义剥除花括号对，破坏 PowerShell 语法）' }
        $pkgSession = $installer.GetType().InvokeMember('OpenPackage', 'InvokeMethod', $null, $installer, @($db, 0))
        $fmtRecord = $installer.GetType().InvokeMember('CreateRecord', 'InvokeMethod', $null, $installer, @(1))
        $fmtRecord.GetType().InvokeMember('StringData', 'SetProperty', $null, $fmtRecord, @(0, $caTarget)) | Out-Null
        $formattedTarget = $pkgSession.GetType().InvokeMember('FormatRecord', 'InvokeMethod', $null, $pkgSession, @($fmtRecord))
        $commandMarker = '-Command "'
        $commandIndex = $formattedTarget.IndexOf($commandMarker)
        if ($commandIndex -lt 0) { throw 'ShutdownWinHost 命令缺少 -Command " 前缀，无法提取内层脚本' }
        $innerScript = $formattedTarget.Substring($commandIndex + $commandMarker.Length)
        if ($innerScript.EndsWith('"')) { $innerScript = $innerScript.Substring(0, $innerScript.Length - 1) }
        $parseTokens = $null; $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseInput($innerScript, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
        if ($parseErrors.Count -gt 0) {
            $firstError = $parseErrors[0]
            throw "ShutdownWinHost 命令经 Formatted 语义转换后不是可解析 PowerShell（首个错误 offset $($firstError.Extent.StartOffset)：$($firstError.Message)）——检查空花括号 / [x] 方括号序列"
        }
        $selectionEnd = $innerScript.IndexOf('; if (!$p)')
        if ($selectionEnd -lt 0) { throw 'ShutdownWinHost 缺少进程选择后的空进程判断' }
        $selection = [scriptblock]::Create($innerScript.Substring(0, $selectionEnd))
        $serviceProcess = [pscustomobject]@{ ProcessId = 101; CommandLine = '"C:\Program Files\LabelFrame\Client\LabelFrame.WinHost.exe" --update-service' }
        $waiterProcess = [pscustomobject]@{ ProcessId = 102; CommandLine = '"C:\Program Files\LabelFrame\Client\LabelFrame.WinHost.exe" --restart-wait 100 123 event args' }
        $clientProcess = [pscustomobject]@{ ProcessId = 103; CommandLine = '"C:\Program Files\LabelFrame\Client\LabelFrame.WinHost.exe" --autostart' }
        $hiddenService = [pscustomobject]@{ ProcessId = 101; CommandLine = $null }
        $emptyService = [pscustomobject]@{ ProcessId = 101; CommandLine = '' }
        $hiddenClient = [pscustomobject]@{ ProcessId = 104; CommandLine = $null }
        foreach ($case in @(
            @{ Processes = @($hiddenService, $clientProcess); Expected = 103 },
            @{ Processes = @($emptyService, $clientProcess); Expected = 103 },
            @{ Processes = @($hiddenService); Expected = $null },
            @{ Processes = @($hiddenService, $hiddenClient); Expected = 104 },
            @{ Processes = @($serviceProcess, $waiterProcess, $clientProcess); Expected = 103 },
            @{ Processes = @($serviceProcess, $waiterProcess); Expected = $null },
            @{ Processes = @($clientProcess); Expected = 103 }
        )) {
            & {
                param($fixture, $script)
                function Get-CimInstance {
                    param($ClassName, $Filter, $ErrorAction)
                    if ($ClassName -eq 'Win32_Service') {
                        if ($Filter -ne "Name='LabelFrameClientUpdater'") { throw '服务查询必须限定更新服务' }
                        return [pscustomobject]@{ ProcessId = 101 }
                    }
                    $fixture.Processes
                }
                function Get-Process { throw '进程选择必须读取命令行角色，不能只按进程名选择' }
                . $script
                if ($p.ProcessId -ne $fixture.Expected) { throw 'ShutdownWinHost 误选更新服务或重启等待器，或漏选打印客户端' }
            } $case $selection
        }
        Write-Host '运行中升级进程角色：真实 MSI 格式化脚本排除更新服务与等待器，保留打印客户端；无客户端时返回空。'
        Write-Host "运行中客户端的安全退出：同步动作 ShutdownWinHost($shutdownSeq) 位于 CostFinalize 后、InstallValidate 前；全新安装、修复、升级与手动卸载都执行，只有升级过程中旧产品的卸载子会话跳过；命令含作业预检与 15 秒退出等待，经引擎 Formatted 转换后仍可解析。"

        # 安装包自己启动更新服务并等待就绪：延迟动作以 SYSTEM 运行刚装好的程序文件，排在服务注册之后、提交之前，非卸载才执行
        $startAction = Get-MsiRows $db "SELECT Action, Type, Source, Target FROM CustomAction WHERE Action = 'StartUpdaterService'"
        if ($startAction.Count -ne 1 -or $startAction[0][1] -ne '3073' -or $startAction[0][2] -ne 'Wix4UtilCA_X64' -or $startAction[0][3] -ne 'WixQuietExec') { throw 'StartUpdaterService 必须是同步、失败阻断、不模拟用户的 x64 QuietExec 延迟动作' }
        $setStart = Get-MsiRows $db "SELECT Action, Type, Source, Target FROM CustomAction WHERE Action = 'SetStartUpdaterService'"
        if ($setStart.Count -ne 1 -or $setStart[0][1] -ne '51' -or $setStart[0][2] -ne 'StartUpdaterService' -or $setStart[0][3] -ne '"[#WinHostExe]" --start-update-service --log "[MsiLogFileLocation].updater-service.log"') { throw '启动更新服务的命令必须经专用属性交给延迟动作，运行已安装的 WinHost 可执行文件，并把明细日志指到安装日志旁边' }
        $startRow = (Get-MsiRows $db "SELECT Condition, Sequence FROM InstallExecuteSequence WHERE Action = 'StartUpdaterService'")[0]
        $setStartSeq = [int]::Parse((Get-MsiRows $db "SELECT Sequence FROM InstallExecuteSequence WHERE Action = 'SetStartUpdaterService'")[0][0])
        $registerSeq = [int]::Parse((Get-MsiRows $db "SELECT Sequence FROM InstallExecuteSequence WHERE Action = 'InstallServices'")[0][0])
        $finalizeSeq = [int]::Parse((Get-MsiRows $db "SELECT Sequence FROM InstallExecuteSequence WHERE Action = 'InstallFinalize'")[0][0])
        $startSeq = [int]::Parse($startRow[1])
        if ($startRow[0] -ne 'NOT REMOVE') { throw '启动更新服务应在非卸载时执行' }
        if ($startSeq -le $registerSeq -or $startSeq -ge $finalizeSeq) { throw "StartUpdaterService($startSeq) 必须位于 InstallServices($registerSeq) 与 InstallFinalize($finalizeSeq) 之间" }
        if ($setStartSeq -ne $startSeq - 1) { throw '启动命令赋值必须紧邻启动动作之前' }
        # 服务起来之后安装若回滚：回滚动作先把服务停下并等到停止，再轮到删文件；失败不影响回滚继续
        $rollbackAction = Get-MsiRows $db "SELECT Action, Type, Source, Target FROM CustomAction WHERE Action = 'StopUpdaterServiceOnRollback'"
        if ($rollbackAction.Count -ne 1 -or $rollbackAction[0][1] -ne '3393' -or $rollbackAction[0][2] -ne 'Wix4UtilCA_X64' -or $rollbackAction[0][3] -ne 'WixQuietExec') { throw 'StopUpdaterServiceOnRollback 必须是回滚阶段执行、失败不阻断、不模拟用户的 x64 QuietExec 动作' }
        $setRollback = Get-MsiRows $db "SELECT Type, Target FROM CustomAction WHERE Action = 'SetStopUpdaterServiceOnRollback'"
        if ($setRollback.Count -ne 1 -or $setRollback[0][0] -ne '51' -or $setRollback[0][1] -notlike '*Stop-Service -Name LabelFrameClientUpdater -Force*' -or -not $setRollback[0][1].StartsWith('"[System64Folder]WindowsPowerShell\v1.0\powershell.exe"')) { throw '回滚停服务的命令必须经专用属性传递，并用固定的系统 PowerShell 等到服务停止' }
        $rollbackSeq = [int]::Parse((Get-MsiRows $db "SELECT Sequence FROM InstallExecuteSequence WHERE Action = 'StopUpdaterServiceOnRollback'")[0][0])
        if ($rollbackSeq -le $registerSeq -or $rollbackSeq -ge $startSeq) { throw "StopUpdaterServiceOnRollback($rollbackSeq) 必须位于 InstallServices 与 StartUpdaterService($startSeq) 之间" }

        # 运行时检测：ASP.NET Core 与 Desktop 各一项；三段启动条件分别覆盖三种缺失
        $dotnetChecks = Get-MsiRows $db 'SELECT * FROM Wix4NetFxDotNetCheck'
        foreach ($check in @(@('Microsoft.AspNetCore.App', 'DOTNET10_PRESENT'), @('Microsoft.WindowsDesktop.App', 'DOTNET10_DESKTOP_PRESENT'))) {
            if (@($dotnetChecks | Where-Object { $_[1] -eq $check[0] -and $_[2] -eq 'x64' -and $_[3] -eq '10.0.0' -and $_[5] -eq $check[1] }).Count -ne 1) { throw "缺少 $($check[0]) 10.0.0（x64）的运行时检测" }
        }
        # 启动条件是「为真才放行」：第一条必须在至少一项可用时为真（否则缺任何一项都先弹笼统提示，精确提示永远轮不到）；
        # 后两条逐项把关；顺序不能换
        $launchConditions = Get-MsiRows $db 'SELECT Condition, Description FROM LaunchCondition'
        foreach ($expect in @(
            @('(DOTNET10_PRESENT = "0" OR DOTNET10_DESKTOP_PRESENT = "0") OR UILevel = 5 OR REMOVE', @('.NET 10 运行时', 'x64', '.NET Desktop Runtime 10', 'ASP.NET Core Runtime 10')),
            @('DOTNET10_DESKTOP_PRESENT = "0" OR UILevel = 5 OR REMOVE', @('.NET Desktop Runtime 10（x64）', 'x64 版本')),
            @('DOTNET10_PRESENT = "0" OR UILevel = 5 OR REMOVE', @('ASP.NET Core Runtime 10（x64）', 'x64 版本')))) {
            $row = @($launchConditions | Where-Object { $_[0] -eq $expect[0] })
            if ($row.Count -ne 1) { throw "缺少运行时启动条件：$($expect[0])" }
            foreach ($word in $expect[1]) { if ("$($row[0][1])" -notlike "*$word*") { throw "运行时启动条件「$($expect[0])」的说明应提到：$word" } }
        }
        $launchOrder = @($launchConditions | ForEach-Object { $_[0] })
        $firstRuntime = [array]::IndexOf($launchOrder, '(DOTNET10_PRESENT = "0" OR DOTNET10_DESKTOP_PRESENT = "0") OR UILevel = 5 OR REMOVE')
        if ($firstRuntime -lt 0 -or $firstRuntime -gt [array]::IndexOf($launchOrder, 'DOTNET10_DESKTOP_PRESENT = "0" OR UILevel = 5 OR REMOVE') -or $firstRuntime -gt [array]::IndexOf($launchOrder, 'DOTNET10_PRESENT = "0" OR UILevel = 5 OR REMOVE')) { throw '运行时启动条件的顺序应为：至少一项可用 -> Desktop -> ASP.NET Core' }
        if (@($launchConditions | Where-Object { "$($_[1])" -like '*未检测到 ASP.NET Core Runtime*' }).Count -ne 0) { throw '运行时缺失说明不得再笼统地说 ASP.NET Core 未安装（只装了 ASP.NET Core Runtime 的机器同样不满足）' }
        $missingDialogs = Get-MsiRows $db "SELECT Action, Condition FROM InstallUISequence WHERE Action = 'RuntimeMissingDlg' OR Action = 'WebView2MissingDlg'"
        if ($missingDialogs.Count -ne 2) { throw '缺少运行时 / WebView2 缺失对话框的显示序列' }
        foreach ($row in $missingDialogs) { if ($row[1] -notlike '*DOTNET10_PRESENT*' -or $row[1] -notlike '*DOTNET10_DESKTOP_PRESENT*') { throw "$($row[0]) 的显示条件应同时依据两项运行时检测结果" } }
        # 缺失说明是否「择一」光看表不行：把条件翻译后对四种运行时状态逐一求值，向导与静默两条路径共用这张真值表
        function Test-RuntimeState([string]$condition, [bool]$aspOk, [bool]$desktopOk, [bool]$fullUi, [bool]$removing) {
            $expr = $condition.Replace('DOTNET10_DESKTOP_PRESENT = "0"', '($desktopOk)').Replace('DOTNET10_PRESENT = "0"', '($aspOk)')
            $expr = $expr.Replace('UILevel = 5', '($fullUi)') -replace '(?<![A-Za-z0-9_])REMOVE(?![A-Za-z0-9_])', '($removing)'
            $expr = $expr -replace '\bAND\b', '-and' -replace '\bOR\b', '-or' -replace '\bNOT\b', '-not'
            $left = $expr -replace '\$(aspOk|desktopOk|fullUi|removing)|-and|-or|-not|[()\s]', ''
            if ($left.Length -ne 0) { throw "运行时条件用到了本断言不会求值的语法：$condition" }
            [bool](Invoke-Expression $expr)
        }
        $runtimeStates = @(
            @{ Name = '无 .NET 10（含只装了 ASP.NET Core Runtime）'; Asp = $false; Desktop = $false; Text = 'NoRuntimeText' },
            @{ Name = '有 ASP.NET Core、无 .NET Desktop Runtime'; Asp = $true; Desktop = $false; Text = 'DesktopMissingText' },
            @{ Name = '有 .NET Desktop Runtime、无 ASP.NET Core'; Asp = $false; Desktop = $true; Text = 'AspNetMissingText' },
            @{ Name = '两项齐全'; Asp = $true; Desktop = $true; Text = '' }
        )
        $expectedTextConditions = @{
            NoRuntimeText = 'NOT DOTNET10_PRESENT = "0" AND NOT DOTNET10_DESKTOP_PRESENT = "0"'
            DesktopMissingText = 'DOTNET10_PRESENT = "0" AND NOT DOTNET10_DESKTOP_PRESENT = "0"'
            AspNetMissingText = 'DOTNET10_DESKTOP_PRESENT = "0" AND NOT DOTNET10_PRESENT = "0"'
        }
        $missingTexts = Get-MsiRows $db "SELECT Control_, Action, Condition FROM ControlCondition WHERE Dialog_ = 'RuntimeMissingDlg'"
        $missingControls = Get-MsiRows $db "SELECT Control, Attributes FROM Control WHERE Dialog_ = 'RuntimeMissingDlg'"
        if ($missingTexts.Count -ne $expectedTextConditions.Count) { throw '运行时缺失对话框应只有三段说明各带一条显示条件' }
        foreach ($control in $expectedTextConditions.Keys) {
            $rows = @($missingTexts | Where-Object { $_[0] -eq $control })
            if ($rows.Count -ne 1 -or $rows[0][1] -ne 'Show' -or $rows[0][2] -ne $expectedTextConditions[$control]) {
                throw "运行时缺失对话框的「$control」应只有一条显示条件：$($expectedTextConditions[$control])"
            }
            $controlRows = @($missingControls | Where-Object { $_[0] -eq $control })
            if ($controlRows.Count -ne 1) { throw "运行时缺失对话框应恰有一个「$control」控件" }
            $attributes = [int]::Parse("$($controlRows[0][1])", [Globalization.CultureInfo]::InvariantCulture)
            if (($attributes -band 1) -ne 0) { throw "「$control」必须默认隐藏：显示条件为假时不会隐藏，默认可见会让三段说明叠在一起" }
        }
        $dialogCondition = (Get-MsiRows $db "SELECT Condition FROM InstallUISequence WHERE Action = 'RuntimeMissingDlg'")[0][0]
        $runtimeLaunch = @($launchConditions | Where-Object { $_[0] -like '*DOTNET10_*' })
        if ($runtimeLaunch.Count -ne 3) { throw '应恰有三条运行时启动条件' }
        $expectedLaunchIndex = @{ NoRuntimeText = 0; DesktopMissingText = 1; AspNetMissingText = 2; '' = -1 }
        foreach ($state in $runtimeStates) {
            $shown = @($missingTexts | Where-Object { Test-RuntimeState $_[2] $state.Asp $state.Desktop $true $false } | ForEach-Object { $_[0] })
            if (($shown -join ',') -ne $state.Text) { throw "向导的缺失说明在「$($state.Name)」时应只显示「$($state.Text)」，按当前条件会显示：$($shown -join ',')" }
            if ((Test-RuntimeState $dialogCondition $state.Asp $state.Desktop $true $false) -ne ($state.Text -ne '')) { throw "运行时缺失对话框在「$($state.Name)」时是否显示不正确" }
            $hit = -1
            for ($i = 0; $i -lt $runtimeLaunch.Count; $i++) {
                if (-not (Test-RuntimeState $runtimeLaunch[$i][0] $state.Asp $state.Desktop $false $false)) { $hit = $i; break }
            }
            if ($hit -ne $expectedLaunchIndex[$state.Text]) { throw "静默安装在「$($state.Name)」时应被第 $($expectedLaunchIndex[$state.Text] + 1) 条运行时启动条件拦截，实际是第 $($hit + 1) 条" }
            foreach ($row in $runtimeLaunch) { if (-not (Test-RuntimeState $row[0] $state.Asp $state.Desktop $true $false)) { throw '完整向导下运行时启动条件必须放行，由缺失对话框引导' } }
        }

        # 安装位置固定：目录标识不是公开属性，显式传入 INSTALLFOLDER 时拦下并说明
        $fixedLocation = Get-MsiRows $db "SELECT Description FROM LaunchCondition WHERE Condition = 'NOT INSTALLFOLDER'"
        if ($fixedLocation.Count -ne 1 -or $fixedLocation[0][0] -notlike '*INSTALLFOLDER*' -or $fixedLocation[0][0] -notlike '*[[]ProgramFiles64Folder]*') { throw '缺少「安装位置固定、不支持 INSTALLFOLDER」的启动条件' }
        if ((Get-MsiRows $db "SELECT Directory FROM Directory WHERE Directory = 'INSTALLFOLDER'").Count -ne 0) { throw '安装目录标识不得是公开属性 INSTALLFOLDER（命令行可改写安装位置）' }
        $allComponents = Get-MsiRows $db 'SELECT Component, Directory_, KeyPath FROM Component'
        if (@($allComponents | Where-Object { $_[1] -eq 'INSTALLFOLDER' }).Count -ne 0) { throw '程序文件组件不得挂在 WiX 的默认目录 INSTALLFOLDER 上' }
        $hostComponent = @($allComponents | Where-Object { $_[2] -eq 'WinHostExe' })
        if ($hostComponent.Count -ne 1 -or $hostComponent[0][1] -ne 'ClientInstallDir') { throw '程序文件组件必须显式挂在固定安装目录 ClientInstallDir 下' }

        # 安装日志：每次安装自动生成于用户临时目录；动作失败的提示说明已撤回并给出日志位置
        $logging = Get-MsiRows $db "SELECT Value FROM Property WHERE Property = 'MsiLogging'"
        if ($logging.Count -ne 1 -or $logging[0][0] -notlike 'v*' -or $logging[0][0] -notlike '*p*') { throw 'Client MSI 应启用自动安装日志（MsiLogging：详细记录 + 属性转储）' }
        # 向导以失败收场时没有错误对话框，只有界面库的标准「提前结束」页：安装日志的位置必须写在这一页的说明里
        $earlyEnd = Get-MsiRows $db "SELECT Text FROM Control WHERE Dialog_ = 'FatalError' AND Control = 'Description'"
        if ($earlyEnd.Count -ne 1 -or $earlyEnd[0][0] -notlike '*安装日志*' -or $earlyEnd[0][0] -notlike '*[[]MsiLogFileLocation]*') { throw '向导的「提前结束」页应写明安装日志位置（界面库本地化覆盖 FatalErrorDescription2）' }

        # 完成页「立即打开」与各对话框的「打开下载页」：安装器扩展库的「请桌面外壳启动」动作。
        # 客户端和浏览器由桌面外壳以当前登录用户的不提权身份启动，安装程序以管理员身份运行时也不跟着提权；
        # 启动不了不影响已经完成的安装。Type 65 = Binary 表里的 DLL（1）+ 忽略返回值（64）
        $shellActions = Get-MsiRows $db "SELECT Action, Type, Source, Target FROM CustomAction WHERE Action = 'LaunchClientOnExit' OR Action = 'OpenDownloadPage'"
        foreach ($name in 'LaunchClientOnExit', 'OpenDownloadPage') {
            $row = @($shellActions | Where-Object { $_[0] -ceq $name })
            if ($row.Count -ne 1 -or $row[0][1] -ne '65' -or $row[0][2] -cne 'Wix4UtilCA_X64' -or $row[0][3] -cne 'WixUnelevatedShellExec') {
                throw "$name 必须是同步、失败不阻断的 x64「请桌面外壳启动」动作（Wix4UtilCA_X64 / WixUnelevatedShellExec）"
            }
        }
        # 防回归警示：向导最后一步触发的动作不能是「不等结果」的异步 EXE 动作——安装会话随即结束，动作起不来（返回 1631）。
        # EXE 类型是 34（目录 + 命令行）或 18（属性 + 命令行）；异步且不等待是 0x80 + 0x40
        foreach ($row in (Get-MsiRows $db 'SELECT Action, Type FROM CustomAction')) {
            $type = [int]::Parse($row[1])
            $base = $type -band 0x3F
            if ((($base -eq 34) -or ($base -eq 18)) -and (($type -band 0xC0) -eq 0xC0)) { throw "动作 $($row[0]) 是「不等结果」的异步 EXE 动作，在安装会话结束前起不来" }
        }
        # 目标经属性交给动作，每个目标一条赋值动作
        $shellTargets = @{
            SetLaunchClientTarget = '[#WinHostExe]'
            SetDownloadPageTarget = 'https://dotnet.microsoft.com/download/dotnet/10.0'
            SetWebView2DownloadPageTarget = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
        }
        foreach ($name in $shellTargets.Keys) {
            $row = Get-MsiRows $db "SELECT Type, Source, Target FROM CustomAction WHERE Action = '$name'"
            if ($row.Count -ne 1 -or $row[0][0] -ne '51' -or $row[0][1] -cne 'WixUnelevatedShellExecTarget' -or $row[0][2] -cne $shellTargets[$name]) {
                throw "$name 应把目标（$($shellTargets[$name])）经属性 WixUnelevatedShellExecTarget 交给启动动作"
            }
        }
        # 按钮上的两条事件按登记顺序执行：先赋目标（1），再启动（2）；完成页上界面库的关窗事件登记顺序是 999，排在它们之后
        $launchCondition = 'WIXUI_EXITDIALOGOPTIONALCHECKBOX = "1" AND NOT Installed'
        $shellButtons = @(
            @{ Dialog = 'ExitDialog'; Control = 'Finish'; Set = 'SetLaunchClientTarget'; Run = 'LaunchClientOnExit'; Condition = $launchCondition; EndAfter = 2 },
            @{ Dialog = 'RuntimeMissingDlg'; Control = 'OpenDownloadButton'; Set = 'SetDownloadPageTarget'; Run = 'OpenDownloadPage'; Condition = '1'; EndAfter = 0 },
            @{ Dialog = 'WebView2MissingDlg'; Control = 'OpenDownloadButton'; Set = 'SetWebView2DownloadPageTarget'; Run = 'OpenDownloadPage'; Condition = '1'; EndAfter = 0 }
        )
        foreach ($button in $shellButtons) {
            foreach ($step in @(@($button.Set, 1), @($button.Run, 2))) {
                $row = @($events | Where-Object { $_[0] -ceq $button.Dialog -and $_[1] -ceq $button.Control -and $_[2] -ceq 'DoAction' -and $_[3] -ceq $step[0] })
                if ($row.Count -ne 1 -or $row[0][4] -cne $button.Condition -or [int]::Parse($row[0][5]) -ne $step[1]) {
                    throw "$($button.Dialog) 的 $($button.Control) 应按登记顺序 $($step[1]) 触发 $($step[0])（条件 $($button.Condition)），先赋目标后启动缺一不可"
                }
            }
            if ($button.EndAfter -gt 0) {
                $end = @($events | Where-Object { $_[0] -ceq $button.Dialog -and $_[1] -ceq $button.Control -and $_[2] -ceq 'EndDialog' })
                if ($end.Count -ne 1 -or [int]::Parse($end[0][5]) -le $button.EndAfter) { throw "$($button.Dialog) 的关窗事件必须排在启动动作之后（登记顺序大于 $($button.EndAfter)），否则启动动作来不及执行" }
            }
        }
        # 对话框上的网址是文字，打开靠按钮。
        # 防回归警示：安装器的 Hyperlink 控件对鼠标点击没有反应，只有焦点在链接上时按回车才打开
        $controls = Get-MsiRows $db 'SELECT Dialog_, Control, Type, Text FROM Control'
        if (@($controls | Where-Object { $_[2] -ceq 'Hyperlink' }).Count -ne 0) { throw '包里不得再有 Hyperlink 控件：鼠标点击没有反应。网址用文字显示，打开动作放在「打开下载页」按钮上' }
        $downloadDialogs = @(
            @{ Dialog = 'RuntimeMissingDlg'; UrlControl = 'RuntimeDownloadUrl'; Url = 'https://dotnet.microsoft.com/download/dotnet/10.0' },
            @{ Dialog = 'WebView2MissingDlg'; UrlControl = 'WebView2DownloadUrl'; Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' }
        )
        foreach ($dialog in $downloadDialogs) {
            $url = @($controls | Where-Object { $_[0] -ceq $dialog.Dialog -and $_[1] -ceq $dialog.UrlControl })
            if ($url.Count -ne 1 -or $url[0][2] -cne 'Text' -or $url[0][3] -cne $dialog.Url) { throw "「$($dialog.Dialog)」应把官方下载页的网址显示为文字（$($dialog.Url)）" }
            $open = @($controls | Where-Object { $_[0] -ceq $dialog.Dialog -and $_[1] -ceq 'OpenDownloadButton' })
            if ($open.Count -ne 1 -or $open[0][2] -cne 'PushButton' -or -not $open[0][3].Contains('打开下载页')) { throw "「$($dialog.Dialog)」应有「打开下载页」按钮 OpenDownloadButton" }
        }
        Write-Host '完成页启动与下载页：扩展库「请桌面外壳启动」动作（同步、失败不阻断）、目标赋值与按钮事件的先后顺序、对话框上不再有 Hyperlink 控件：OK'

        # 清除用户数据：删除整个用户数据目录（作业、模板、日志、打印出图、连接与打印设置、界面缓存都在里面）；
        # 机器级设置删文件后目录空了才删——不带 /s，同一目录下的插件目录不是用户数据。
        # Type 1122 = EXE（34）+ 延迟执行（0x400）+ 忽略返回值（0x40），模拟用户；3170 再加「不模拟用户」位（0x800）。
        # 防回归警示：机器级的两条必须不模拟用户——以普通方式启动安装程序再进维护向导时，模拟用户的延迟动作拿不到管理员权限，
        # 删不掉 ProgramData 与 Program Files 下的文件；用户数据目录必须模拟用户——%LOCALAPPDATA% 要按当前用户的目录展开
        $clearCondition = 'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE AND CLEAR_USER_DATA="1"'
        $clearActions = @{
            ClearDataDelUserDir = @(1122, 'rmdir /s /q "%LOCALAPPDATA%\LabelFrame"')
            ClearDataDelMachineConfig = @(3170, 'del /q "%ProgramData%\LabelFrame\Client\settings.json" & rmdir /q "%ProgramData%\LabelFrame\Client"')
            ClearDataDelAppSettings = @(3170, 'del /q "%ProgramFiles%\LabelFrame\Client\appsettings.json"')
        }
        $allActions = Get-MsiRows $db 'SELECT Action, Type, Target FROM CustomAction'
        $clearRows = Get-MsiRows $db 'SELECT Action, Condition FROM InstallExecuteSequence'
        foreach ($name in $clearActions.Keys) {
            $action = @($allActions | Where-Object { $_[0] -ceq $name })
            if ($action.Count -ne 1) { throw "缺少清除用户数据的动作 $name" }
            $type = [int]::Parse($action[0][1])
            $expectedType = $clearActions[$name][0]
            if ($type -ne $expectedType -or (($type -band 0x400) -eq 0) -or (($type -band 0x40) -eq 0)) {
                throw "清除动作 $name 应是失败不阻断的延迟动作（Type $expectedType：$(if ($expectedType -eq 3170) { '不模拟用户' } else { '模拟用户' })），实际 Type $($action[0][1])"
            }
            if (-not $action[0][2].Contains($clearActions[$name][1])) { throw "清除动作 $name 的命令应包含：$($clearActions[$name][1])；实际：$($action[0][2])" }
            if ($name -ceq 'ClearDataDelMachineConfig' -and $action[0][2].Contains('rmdir /s')) { throw '机器级设置目录里还有插件目录，不能用 /s 整个删除' }
            $row = @($clearRows | Where-Object { $_[0] -ceq $name })
            if ($row.Count -ne 1) { throw "清除动作 $name 应在安装执行序列里出现一次，实际 $($row.Count) 次" }
            if ($row[0][1] -cne $clearCondition) { throw "清除动作 $name 应只在手动卸载且勾选清除时执行（$clearCondition），实际条件：$($row[0][1])" }
        }
        # 按文件名逐个删的两个旧动作被删除整个目录的动作取代，不得回来
        foreach ($old in 'ClearDataDelFiles', 'ClearDataDelPrintDir') {
            if (@($allActions | Where-Object { $_[0] -ceq $old }).Count -ne 0) { throw "清除动作 $old 已由删除整个用户数据目录的 ClearDataDelUserDir 取代，不得保留" }
        }
        Write-Host '清除用户数据：删除整个用户数据目录，机器级设置与安装目录下的客户端配置一并删除，只在手动卸载并勾选时执行：OK'

        # 卸载时「清除用户数据」的询问：两条入口都接到询问页，询问页再进「确认删除」页。
        # 防回归警示：自带向导组的「删除」默认直接跳确认页，不经过界面序列——询问页只排在界面序列里时，
        # 用户走得到的卸载（重跑安装包选删除）永远不会问
        $removeMode = 'WixUI_InstallMode = "Remove"'
        $removeButton = @($events | Where-Object { $_[0] -ceq 'MaintenanceTypeDlg' -and $_[1] -ceq 'RemoveButton' })
        $toQuestion = @($removeButton | Where-Object { $_[2] -ceq 'NewDialog' -and $_[3] -ceq 'ClearDataDlg' })
        $libraryRows = @($removeButton | Where-Object { ($_[2] -ceq 'NewDialog' -and $_[3] -ceq 'VerifyReadyDlg') -or ($_[2] -ceq '[WixUI_InstallMode]' -and $_[3] -ceq 'Remove') })
        if ($toQuestion.Count -ne 1 -or $toQuestion[0][4] -cne '1' -or $libraryRows.Count -ne 2) { throw '维护向导的「删除」应跳到询问页 ClearDataDlg；自带向导组在它上面的登记（直接跳确认页、置安装模式）变了，请复核询问页的接法' }
        foreach ($row in $libraryRows) {
            if ([int]::Parse($toQuestion[0][5]) -le [int]::Parse($row[5])) { throw "维护向导「删除」上跳到询问页的登记顺序必须大于自带向导组的登记（$($row[5])）：否则要么不显示询问页，要么显示时安装模式还没置好" }
        }
        # 询问页上的事件登记一条不多、一条不少：「继续」上多出一条直接关窗的登记，卸载就会跳过确认页立刻开始
        $questionEvents = @($events | Where-Object { $_[0] -ceq 'ClearDataDlg' })
        $expectedEvents = @(
            @{ Control = 'NextButton'; Event = 'DoAction'; Argument = 'SetRemoveInstallMode'; Condition = 'NOT WixUI_InstallMode'; Order = 1 },
            @{ Control = 'NextButton'; Event = 'NewDialog'; Argument = 'VerifyReadyDlg'; Condition = $removeMode; Order = 2 },
            @{ Control = 'BackButton'; Event = 'NewDialog'; Argument = 'MaintenanceTypeDlg'; Condition = '1'; Order = 1 },
            @{ Control = 'CancelButton'; Event = 'SpawnDialog'; Argument = 'CancelDlg'; Condition = $removeMode; Order = 1 },
            @{ Control = 'CancelButton'; Event = 'EndDialog'; Argument = 'Exit'; Condition = 'NOT WixUI_InstallMode'; Order = 2 }
        )
        foreach ($expected in $expectedEvents) {
            $row = @($questionEvents | Where-Object { $_[1] -ceq $expected.Control -and $_[2] -ceq $expected.Event -and $_[3] -ceq $expected.Argument })
            if ($row.Count -ne 1 -or $row[0][4] -cne $expected.Condition -or [int]::Parse($row[0][5]) -ne $expected.Order) {
                throw "询问页的「$($expected.Control)」应以登记顺序 $($expected.Order) 触发 $($expected.Event) $($expected.Argument)（条件 $($expected.Condition)）"
            }
        }
        if ($questionEvents.Count -ne $expectedEvents.Count) { throw "询问页上出现了预期之外的事件登记（共 $($questionEvents.Count) 条，应为 $($expectedEvents.Count) 条）：$(($questionEvents | ForEach-Object { $_[1] + '.' + $_[2] + ' ' + $_[3] }) -join '；')" }
        $setRemoveMode = Get-MsiRows $db "SELECT Type, Source, Target FROM CustomAction WHERE Action = 'SetRemoveInstallMode'"
        if ($setRemoveMode.Count -ne 1 -or $setRemoveMode[0][0] -ne '51' -or $setRemoveMode[0][1] -cne 'WixUI_InstallMode' -or $setRemoveMode[0][2] -cne 'Remove') { throw 'SetRemoveInstallMode 应把界面库的安装模式 WixUI_InstallMode 置为 Remove：确认页与进度页据此显示卸载的文字' }
        # 防回归警示：勾选属性必须声明为安全属性——以普通方式启动安装程序再进维护向导时，提权的执行会话忽略不是安全属性的属性，勾选等于没勾。
        # 构建工具把没有取值的安全属性列在 SecureCustomProperties 里
        $secureProperties = @((Get-MsiRows $db "SELECT Value FROM Property WHERE Property = 'SecureCustomProperties'") | ForEach-Object { $_[0] })
        if (@($secureProperties | Where-Object { (';' + $_ + ';') -like '*;CLEAR_USER_DATA;*' }).Count -ne 1) { throw 'CLEAR_USER_DATA 必须声明为安全属性（出现在 SecureCustomProperties 里），否则维护向导里的勾选传不进提权的执行会话' }
        # 「上一步」只在维护向导入口显示：默认隐藏，显示条件同时要求安装模式为删除、且功能不是命令行预选的
        $backShow = Get-MsiRows $db "SELECT Control_, Action, Condition FROM ControlCondition WHERE Dialog_ = 'ClearDataDlg'"
        $backControl = Get-MsiRows $db "SELECT Control, Type, Attributes FROM Control WHERE Dialog_ = 'ClearDataDlg' AND Control = 'BackButton'"
        if ($backShow.Count -ne 1 -or $backShow[0][0] -cne 'BackButton' -or $backShow[0][1] -cne 'Show' -or $backShow[0][2] -cne ($removeMode + ' AND NOT Preselected')) { throw "询问页的「上一步」应只有一条显示条件：$removeMode AND NOT Preselected" }
        if ($backControl.Count -ne 1 -or $backControl[0][1] -cne 'PushButton' -or (([int]::Parse($backControl[0][2]) -band 1) -ne 0)) { throw '询问页的「上一步」必须默认隐藏：显示条件为假时不会隐藏，默认可见会让命令行卸载多出一个回到维护向导的入口' }
        $backFromVerify = @($events | Where-Object { $_[0] -ceq 'VerifyReadyDlg' -and $_[1] -ceq 'Back' -and $_[2] -ceq 'NewDialog' -and $_[3] -ceq 'ClearDataDlg' })
        if ($backFromVerify.Count -ne 1 -or $backFromVerify[0][4] -cne $removeMode) { throw "「确认删除」页的上一步应回询问页（$removeMode）" }
        foreach ($row in @($events | Where-Object { $_[0] -ceq 'VerifyReadyDlg' -and $_[1] -ceq 'Back' -and $_[2] -ceq 'NewDialog' -and $_[3] -cin 'MaintenanceTypeDlg', 'InstallDirDlg', 'WelcomeDlg' })) {
            if ([int]::Parse($backFromVerify[0][5]) -le [int]::Parse($row[5])) { throw "「确认删除」页上一步回询问页的登记顺序必须大于自带向导组的登记（$($row[3])，$($row[5])），否则卸载时回的是维护向导的类型页" }
        }
        # 界面序列：命令行带 REMOVE=ALL 时先显示询问页，之后不再显示界面库的「继续安装」页（它按安装措辞写成）。
        # 两行的条件翻译后按状态逐一求值
        function Test-UninstallUiState([string]$condition, [bool]$removingAll, [bool]$upgrading, [bool]$resume, [bool]$preselected, [bool]$patch = $false) {
            # Integer literals mean false (0) or true (anything else), as they do for the installer.
            $expr = [regex]::Replace($condition, '(?<![A-Za-z0-9_])0(?![A-Za-z0-9_])', '($false)')
            $expr = $expr.Replace('REMOVE~="ALL"', '($removingAll)')
            $expr = [regex]::Replace($expr, '(?<![A-Za-z0-9_])UPGRADINGPRODUCTCODE(?![A-Za-z0-9_])', '($upgrading)')
            $expr = [regex]::Replace($expr, '(?<![A-Za-z0-9_])Installed(?![A-Za-z0-9_])', '($true)')
            $expr = [regex]::Replace($expr, '(?<![A-Za-z0-9_])RESUME(?![A-Za-z0-9_])', '($resume)')
            $expr = [regex]::Replace($expr, '(?<![A-Za-z0-9_])Preselected(?![A-Za-z0-9_])', '($preselected)')
            $expr = [regex]::Replace($expr, '(?<![A-Za-z0-9_])PATCH(?![A-Za-z0-9_])', '($patch)')
            $expr = $expr -creplace '\bAND\b', '-and' -creplace '\bOR\b', '-or' -creplace '\bNOT\b', '-not'
            $left = $expr -creplace '\$(removingAll|upgrading|resume|preselected|patch|true|false)|-and|-or|-not|[()\s]', ''
            if ($left.Length -ne 0) { throw "卸载询问链路的显示条件用到了本断言不会求值的语法：$condition" }
            [bool](Invoke-Expression $expr)
        }
        $uiSequence = Get-MsiRows $db "SELECT Action, Condition FROM InstallUISequence WHERE Action = 'ClearDataDlg' OR Action = 'ResumeDlg'"
        $questionRow = @($uiSequence | Where-Object { $_[0] -ceq 'ClearDataDlg' })
        $resumeRow = @($uiSequence | Where-Object { $_[0] -ceq 'ResumeDlg' })
        if ($questionRow.Count -ne 1 -or $resumeRow.Count -ne 1) { throw '界面序列里应有询问页 ClearDataDlg 与 ResumeDlg 各一行' }
        $uninstallUiStates = @(
            @{ Name = '手动卸载（命令行 REMOVE=ALL 或 msiexec /x）'; RemovingAll = $true; Upgrading = $false; Resume = $false; Preselected = $true; Question = $true; ResumePage = $false },
            @{ Name = '升级过程中旧产品的卸载子会话'; RemovingAll = $true; Upgrading = $true; Resume = $false; Preselected = $true; Question = $false; ResumePage = $null },
            @{ Name = '全新安装、维护运行、修复'; RemovingAll = $false; Upgrading = $false; Resume = $false; Preselected = $false; Question = $false; ResumePage = $false },
            @{ Name = '继续被中断的安装'; RemovingAll = $false; Upgrading = $false; Resume = $true; Preselected = $false; Question = $false; ResumePage = $true },
            @{ Name = '命令行预选了功能的其他运行'; RemovingAll = $false; Upgrading = $false; Resume = $false; Preselected = $true; Question = $false; ResumePage = $true }
        )
        foreach ($state in $uninstallUiStates) {
            if ((Test-UninstallUiState $questionRow[0][1] $state.RemovingAll $state.Upgrading $state.Resume $state.Preselected) -ne $state.Question) {
                throw "询问页在「$($state.Name)」时应当$(if ($state.Question) { '显示' } else { '不显示' })，当前条件不是这样：$($questionRow[0][1])"
            }
            # 升级的卸载子会话不带界面，「继续安装」页在那里显示与否不作要求
            if ($null -ne $state.ResumePage -and (Test-UninstallUiState $resumeRow[0][1] $state.RemovingAll $state.Upgrading $state.Resume $state.Preselected) -ne $state.ResumePage) {
                throw "界面库的「继续安装」页在「$($state.Name)」时应当$(if ($state.ResumePage) { '显示' } else { '不显示' })，当前条件不是这样：$($resumeRow[0][1])"
            }
        }
        # 命令行卸载的进度页：界面库按「命令行预选了功能」把「正在安装」的两段文字也显示出来，和「正在删除」叠在一起。
        # 询问页之后清掉预选标记，并让维护欢迎页（界面库的条件看的是这个标记的反面）不在这一路径上显示
        $clearPreselected = Get-MsiRows $db "SELECT Type, Source, Target FROM CustomAction WHERE Action = 'ClearPreselectedAfterQuestion'"
        if ($clearPreselected.Count -ne 1 -or $clearPreselected[0][0] -ne '51' -or $clearPreselected[0][1] -cne 'Preselected' -or $clearPreselected[0][2] -cne '') { throw 'ClearPreselectedAfterQuestion 应是只把 Preselected 置空的赋值动作（否则命令行卸载的进度页上「正在安装」与「正在删除」叠在一起）' }
        $questionSeq = (Get-MsiRows $db "SELECT Sequence FROM InstallUISequence WHERE Action = 'ClearDataDlg'")[0][0]
        $resumeSeq = (Get-MsiRows $db "SELECT Sequence FROM InstallUISequence WHERE Action = 'ResumeDlg'")[0][0]
        $clearRow = Get-MsiRows $db "SELECT Condition, Sequence FROM InstallUISequence WHERE Action = 'ClearPreselectedAfterQuestion'"
        if ($clearRow.Count -ne 1 -or $clearRow[0][0] -cne 'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE') { throw "ClearPreselectedAfterQuestion 应只在手动卸载（命令行 REMOVE=ALL）时执行，实际：$($clearRow | ForEach-Object { $_[0] })" }
        if ([int]::Parse($clearRow[0][1]) -le [int]::Parse($questionSeq) -or [int]::Parse($clearRow[0][1]) -ge [int]::Parse($resumeSeq)) { throw 'ClearPreselectedAfterQuestion 应排在询问页之后、「继续安装」页之前' }
        # Get-MsiRows returns the rows as one array: it must not be wrapped in @() again (the indexing would shift by one level)
        $maintenanceRow = Get-MsiRows $db "SELECT Action, Condition FROM InstallUISequence WHERE Action = 'MaintenanceWelcomeDlg'"
        if ($maintenanceRow.Count -ne 1) { throw '界面序列里应有维护欢迎页 MaintenanceWelcomeDlg 一行' }
        # 预选标记已清掉之后的状态（均已安装）：维护欢迎页只在重跑安装包时显示
        $maintenanceStates = @(
            @{ Name = '重跑安装包'; RemovingAll = $false; Upgrading = $false; Resume = $false; Preselected = $false; Shows = $true },
            @{ Name = '命令行 REMOVE=ALL 的手动卸载（预选标记已清掉）'; RemovingAll = $true; Upgrading = $false; Resume = $false; Preselected = $false; Shows = $false },
            @{ Name = '继续被中断的安装'; RemovingAll = $false; Upgrading = $false; Resume = $true; Preselected = $false; Shows = $false }
        )
        foreach ($state in $maintenanceStates) {
            $shows = Test-UninstallUiState $maintenanceRow[0][1] $state.RemovingAll $state.Upgrading $state.Resume $state.Preselected
            if ($shows -ne $state.Shows) { throw "维护欢迎页在「$($state.Name)」时应当$(if ($state.Shows) { '显示' } else { '不显示' })，当前条件不是这样：$($maintenanceRow[0][1])" }
        }
        Write-Host '卸载询问：维护向导的「删除」先经询问页；询问页的「继续」先置安装模式再进「确认删除」页，上一步回询问页；命令行 REMOVE=ALL 时询问之后不再显示「继续安装」页，进度页只显示「正在删除」：OK'
        Write-Host '运行时检测（ASP.NET Core + Desktop 双项、三段缺失说明）、安装位置固定（只读页旁路 + INSTALLFOLDER 拦截 + 组件挂载）、安装日志与失败提示、更新服务启动动作：OK'
    }

    Write-Host "断言通过：向导齐全，运行时检查（.NET $runtimeSeq / WebView2 $webview2Seq）先于欢迎页（$welcomeSeq）。"
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
