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
        Write-Host '运行时检测（ASP.NET Core + Desktop 双项、三段缺失说明）、安装位置固定（只读页旁路 + INSTALLFOLDER 拦截 + 组件挂载）、安装日志与失败提示、更新服务启动动作：OK'
    }

    Write-Host "断言通过：向导齐全，运行时检查（.NET $runtimeSeq / WebView2 $webview2Seq）先于欢迎页（$welcomeSeq）。"
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
