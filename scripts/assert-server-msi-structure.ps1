# 验证 Server MSI 的安装位置契约：安装目录不可从外部改写、对 INSTALLFOLDER 的拦截、只读的安装目录页、
# 程序文件的挂载目录、清除用户数据的路径；另验卸载时询问清除用户数据的链路、停服务的时机与缺少运行时对话框的下载页按钮。任一条不满足即抛错：
# 安装位置一旦能被改写，不带目录的升级会落到默认目录、用户改过的 appsettings.json 随之失效，卸载时的「清除用户数据」也会删错地方。
# 兼容 Windows PowerShell 5.1 与 PowerShell 7。
# 用法：powershell -File scripts/assert-server-msi-structure.ps1 <msi 路径> [...]
param([Parameter(Mandatory = $true)][string[]]$MsiPaths)

$ErrorActionPreference = 'Stop'

# 整表读取：结果作为「行的数组」整体返回，单行结果的嵌套数组不会被管道展平。
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

# DefaultDir / FileName 形如 "短名|长名"，取长名。
function Get-LongName([string]$MsiName) {
    if ($MsiName -like '*|*') { return ($MsiName -split '\|')[-1] }
    return $MsiName
}

# 把 MSI 条件翻译成 PowerShell 并求值。只认这里用到的语法：NOT / AND / OR（大写）、成对括号、整数常量（0 为假，
# 其余为真）、属性与引号里字符串常量的等值和不等比较（含 ~=）、裸属性名；出现别的写法直接报错，而不是算出一个看似可信的结果。
# 空条件恒真、没有赋值的属性按假处理，都与安装器一致。状态里的值按安装器的写法给出（布尔真为 "1"、假为空）。
function Test-MsiCondition([string]$Condition, [hashtable]$State) {
    if ($null -eq $Condition -or $Condition.Trim() -eq '') { return $true }
    # 小写的关键字在安装器里算关键字还是属性名，这里不替它猜：一律按不会求值处理
    foreach ($keyword in [regex]::Matches($Condition, '(?i)\b(?:not|and|or)\b')) {
        if ($keyword.Value -cnotmatch '^(NOT|AND|OR)$') { throw "条件里的关键字必须大写，这里不求值：$Condition" }
    }
    $open = ($Condition.ToCharArray() | Where-Object { $_ -eq '(' }).Count
    $close = ($Condition.ToCharArray() | Where-Object { $_ -eq ')' }).Count
    if ($open -ne $close) { throw "条件的括号不配对，这里不求值：$Condition" }
    $pattern = '[A-Za-z_][A-Za-z0-9_]*~?(?:\s*(?:<>|=)\s*"[^"]*")?|\d+'
    $translate = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $text = $match.Value
        $name = [regex]::Match($text, '^[A-Za-z_][A-Za-z0-9_]*').Value
        if ($name -eq '') { return '$' + ([int]::Parse($text) -ne 0).ToString().ToLowerInvariant() }
        if ($text -match '^[A-Za-z_][A-Za-z0-9_]*~?\s*(<>|=)\s*"([^"]*)"$') {
            $operator = if ($Matches[1] -ceq '=') { '-ceq' } else { '-cne' }
            return '([string]$' + $name + ' ' + $operator + " '" + $Matches[2].Replace("'", "''") + "')"
        }
        if ($name -ceq 'NOT') { return '-not' }
        if ($name -ceq 'AND') { return '-and' }
        if ($name -ceq 'OR') { return '-or' }
        return '(-not [string]::IsNullOrEmpty([string]$' + $name + '))'
    }
    $expr = [regex]::Replace($Condition, $pattern, $translate)
    $leftover = [regex]::Replace($Condition, $pattern, '') -replace '[()\s]', ''
    if ($leftover.Length -ne 0) { throw "条件用到了本断言不会求值的语法：$Condition" }
    foreach ($key in $State.Keys) {
        $value = $State[$key]
        $text = if ($value -is [bool]) { if ($value) { '1' } else { '' } } else { [string]$value }
        Set-Variable -Name $key -Value $text -Scope Local
    }
    return [bool](Invoke-Expression $expr)
}

# 按钮点击后实际跳到的对话框：同一按钮上登记的多条 NewDialog 里，条件为真的那些中登记顺序最大的一条生效。
# 两条条件为真的登记顺序相同则谁生效不确定，按断言失败处理。
function Get-DialogTarget {
    param([object[]]$Rows, [string]$Dialog, [string]$Control, [hashtable]$State)
    $target = ''
    $best = -1
    $tied = $false
    foreach ($row in $Rows) {
        if ($row[0] -ne $Dialog -or $row[1] -ne $Control -or $row[2] -ne 'NewDialog') { continue }
        if (-not (Test-MsiCondition $row[4] $State)) { continue }
        $order = [int]::Parse($row[5])
        if ($order -gt $best) { $best = $order; $target = $row[3]; $tied = $false }
        elseif ($order -eq $best) { $tied = $true }
    }
    if ($tied) { throw "$Dialog.$Control 上有两条条件同时成立的跳转登记顺序相同（$best），谁生效不确定" }
    return $target
}

foreach ($path in $MsiPaths) {
    Write-Host "=== $path ==="
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $path).Path, 0))

    # 只对 Server 包下结论：拿错包（例如 Client）时直接报错，不按别的包的结构给出误导性的结果
    $service = Get-MsiRows $db "SELECT Name, Component_ FROM ServiceInstall WHERE Name = 'LabelFrameServer'"
    if ($service.Count -ne 1) { throw '这不是 Server MSI：没有注册 LabelFrameServer 服务' }

    # ---- 安装目录：由服务程序所在的组件反查，其余断言都以它为准 ----
    $components = Get-MsiRows $db 'SELECT Component, Directory_, KeyPath FROM Component'
    $serverComponent = @($components | Where-Object { $_[2] -eq 'ServerExe' })
    if ($serverComponent.Count -ne 1) { throw '应有且只有一个组件以 ServerExe 为键路径' }
    if ($service[0][1] -ne $serverComponent[0][0]) { throw 'LabelFrameServer 服务必须注册在服务程序所在的组件上' }
    $installDirId = $serverComponent[0][1]
    $directories = @{}
    foreach ($row in (Get-MsiRows $db 'SELECT Directory, Directory_Parent, DefaultDir FROM Directory')) { $directories[$row[0]] = $row }

    # ---- 安装目录不可从外部改写 ----
    if ((Get-MsiRows $db "SELECT Directory FROM Directory WHERE Directory = 'INSTALLFOLDER'").Count -ne 0) { throw '安装目录标识不得是公开属性 INSTALLFOLDER（命令行可改写安装位置）' }
    if ($installDirId -cnotmatch '[a-z]') { throw "安装目录标识 $installDirId 不含小写字母：全大写的标识是公开属性，命令行能改写安装位置" }
    if (-not $directories.ContainsKey($installDirId)) { throw "Directory 表里没有安装目录 $installDirId" }
    $installDir = $directories[$installDirId]
    if (-not $directories.ContainsKey($installDir[1])) { throw "安装目录 $installDirId 的上级目录 $($installDir[1]) 不在 Directory 表里" }
    $rootDir = $directories[$installDir[1]]
    if ($rootDir[1] -ne 'ProgramFiles64Folder' -or (Get-LongName $rootDir[2]) -ne 'LabelFrame' -or (Get-LongName $installDir[2]) -ne 'Server') {
        throw "安装目录应固定为 Program Files\LabelFrame\Server（64 位），Directory 表里是 $($rootDir[1])\$(Get-LongName $rootDir[2])\$(Get-LongName $installDir[2])"
    }
    $settingsComponent = @($components | Where-Object { $_[0] -eq 'AppSettingsFile' })
    if ($settingsComponent.Count -ne 1 -or $settingsComponent[0][1] -ne $installDirId) { throw '用户配置 appsettings.json 的组件必须与服务程序挂在同一个安装目录下' }
    $wixuiDir = Get-MsiRows $db "SELECT Value FROM Property WHERE Property = 'WIXUI_INSTALLDIR'"
    if ($wixuiDir.Count -ne 1 -or $wixuiDir[0][0] -cne $installDirId) { throw "WIXUI_INSTALLDIR 应指向安装目录标识 $installDirId" }

    # ---- 显式传入 INSTALLFOLDER 时拦下并说明 ----
    # 条件原样比较：写成带放行分支的弱化条件（例如再放行某种界面级别）等于没有拦截
    $intercept = Get-MsiRows $db "SELECT Description FROM LaunchCondition WHERE Condition = 'NOT INSTALLFOLDER'"
    if ($intercept.Count -ne 1) { throw '缺少「安装位置固定、不支持 INSTALLFOLDER」的启动条件（NOT INSTALLFOLDER）' }
    foreach ($word in @('INSTALLFOLDER', '[ProgramFiles64Folder]LabelFrame\Server')) {
        if (-not "$($intercept[0][0])".Contains($word)) { throw "INSTALLFOLDER 拦截条件的说明应提到：$word" }
    }

    # ---- 只读的安装目录页 ----
    $locationControls = Get-MsiRows $db "SELECT Control, Type, Text FROM Control WHERE Dialog_ = 'InstallLocationDlg'"
    if ($locationControls.Count -eq 0) { throw '缺少只读的安装目录页 InstallLocationDlg' }
    if (@($locationControls | Where-Object { $_[0] -eq 'ChangeFolder' -or $_[1] -in 'PathEdit', 'DirectoryList', 'VolumeCostList', 'ListView', 'SelectionTree' }).Count -gt 0) { throw '安装目录页必须只读：不得有可编辑路径框、更改按钮或目录列表' }
    if (@($locationControls | Where-Object { $_[0] -eq 'InstallLocationText' -and $_[2] -ceq ('[' + $installDirId + ']') }).Count -ne 1) { throw "安装目录页应只读展示安装目录 [$installDirId]" }
    # 这一页只写「安装目录」和路径，不加解释：文字控件只允许标题与路径两个
    $locationTexts = @($locationControls | Where-Object { $_[1] -eq 'Text' })
    if ($locationTexts.Count -ne 2 -or @($locationTexts | Where-Object { $_[0] -eq 'Title' -and $_[2] -like '*安装目录' }).Count -ne 1) { throw '安装目录页只保留标题「安装目录」与路径，不加说明文字' }
    $events = Get-MsiRows $db 'SELECT * FROM ControlEvent'
    foreach ($link in @(@('InstallLocationDlg', 'Next', 'VerifyReadyDlg'), @('InstallLocationDlg', 'Back', 'LicenseAgreementDlg'))) {
        if (@($events | Where-Object { $_[0] -eq $link[0] -and $_[1] -eq $link[1] -and $_[2] -eq 'NewDialog' -and $_[3] -eq $link[2] }).Count -lt 1) { throw "向导链路缺失：$($link[0]).$($link[1]) 应跳转 $($link[2])" }
    }
    if ((Get-MsiRows $db "SELECT Action FROM InstallUISequence WHERE Action = 'InstallDirDlg'").Count -ne 0) { throw '可改目录的自带页面不得由安装序列直接显示' }

    # ---- 向导链路：逐个状态求实际跳转 ----
    # 防回归警示：自带向导组在许可页「下一步」与确认页「上一步」上的默认目标仍是可改目录的旧页，
    # 只读页靠「同一按钮上登记顺序更大的跳转生效」旁路它；维护流程的上一步不能被这条旁路带走
    $navigation = @(
        @{ Name = '全新安装 / 向导升级'; State = @{ LicenseAccepted = $true }; FromLicense = 'InstallLocationDlg'; FromVerify = 'InstallLocationDlg' },
        @{ Name = '维护：修复'; State = @{ Installed = $true; WixUI_InstallMode = 'Repair' }; FromLicense = ''; FromVerify = 'MaintenanceTypeDlg' },
        @{ Name = '维护：选了删除、还没到询问页'; State = @{ Installed = $true; REMOVE = $true }; FromLicense = ''; FromVerify = 'MaintenanceTypeDlg' },
        @{ Name = '卸载：确认删除页'; State = @{ Installed = $true; WixUI_InstallMode = 'Remove' }; FromLicense = ''; FromVerify = 'ClearDataDlg' },
        @{ Name = '维护：小版本更新'; State = @{ Installed = $true; PATCH = $true }; FromLicense = ''; FromVerify = 'WelcomeDlg' }
    )
    foreach ($case in $navigation) {
        $expectedLicense = if ($case.FromLicense) { $case.FromLicense } else { '不跳转' }
        $expectedVerify = if ($case.FromVerify) { $case.FromVerify } else { '不跳转' }
        $fromLicense = Get-DialogTarget -Rows $events -Dialog 'LicenseAgreementDlg' -Control 'Next' -State $case.State
        if ($fromLicense -cne $case.FromLicense) { throw "「$($case.Name)」时许可页的下一步应到 $expectedLicense，实际会到 $fromLicense" }
        $fromVerify = Get-DialogTarget -Rows $events -Dialog 'VerifyReadyDlg' -Control 'Back' -State $case.State
        if ($fromVerify -cne $case.FromVerify) { throw "「$($case.Name)」时确认页的上一步应到 $expectedVerify，实际会到 $fromVerify" }
    }
    # 许可页「下一步」的旁路登记不检查许可是否被接受，能否越过许可是由这个按钮的启用状态把关的
    $gates = Get-MsiRows $db "SELECT Action, Condition FROM ControlCondition WHERE Dialog_ = 'LicenseAgreementDlg' AND Control_ = 'Next'"
    if (@($gates | Where-Object { $_[0] -ceq 'Enable' -and $_[1] -ceq 'LicenseAccepted = "1"' }).Count -ne 1 -or @($gates | Where-Object { $_[0] -ceq 'Disable' -and $_[1] -ceq 'LicenseAccepted <> "1"' }).Count -ne 1) {
        throw '许可页的「下一步」必须只在接受许可后可用（安装目录页的跳转登记不检查许可状态）'
    }
    # 可改目录的自带页面只能从上面两个被旁路的按钮到达，出现别的入口就意味着安装位置又能被改
    $legacyEntries = @($events | Where-Object { $_[3] -eq 'InstallDirDlg' })
    if ($legacyEntries.Count -eq 0) { throw '自带向导组的默认跳转形态变了（不再指向可改目录页），请复核只读安装目录页的旁路接法' }
    foreach ($row in $legacyEntries) {
        if (-not (($row[0] -eq 'LicenseAgreementDlg' -and $row[1] -eq 'Next') -or ($row[0] -eq 'VerifyReadyDlg' -and $row[1] -eq 'Back'))) {
            throw "可改目录的自带页面出现了新的入口（$($row[0]).$($row[1])），安装位置可能被修改"
        }
    }
    foreach ($row in @($events | Where-Object { $_[3] -eq 'BrowseDlg' })) {
        if ($row[0] -ne 'InstallDirDlg') { throw "目录浏览页出现了新的入口（$($row[0]).$($row[1])），安装位置可能被修改" }
    }

    # ---- 清除用户数据的路径 ----
    # 卸载时删除的配置必须就是上面的安装目录里的那一份；只改目录名而不改这里，会去删一个不存在的文件
    $settingsPath = '%ProgramFiles%\LabelFrame\Server\appsettings.json'
    $clearSettings = Get-MsiRows $db "SELECT Target FROM CustomAction WHERE Action = 'ClearDataDelAppSettings'"
    if ($clearSettings.Count -ne 1) { throw '缺少清除 appsettings.json 的动作 ClearDataDelAppSettings' }
    if (-not $clearSettings[0][0].Contains('"' + $settingsPath + '"')) { throw "清除用户数据应删除安装目录下的配置（$settingsPath），实际命令：$($clearSettings[0][0])" }
    # 条件原样比较：少任何一段，升级过程中都会删掉用户正在用的配置或数据
    $clearCondition = 'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE AND CLEAR_USER_DATA="1"'
    $sequence = Get-MsiRows $db 'SELECT Action, Condition FROM InstallExecuteSequence'
    foreach ($action in 'ClearDataDelServerDir', 'ClearDataDelServerDb', 'ClearDataDelAppSettings') {
        $row = @($sequence | Where-Object { $_[0] -eq $action })
        if ($row.Count -ne 1) { throw "清除用户数据的动作 $action 应在安装执行序列里出现一次，实际 $($row.Count) 次" }
        if ($row[0][1] -cne $clearCondition) { throw "清除用户数据的动作 $action 应只在手动卸载且勾选清除时执行（$clearCondition），实际条件：$($row[0][1])" }
    }
    # 防回归警示：三条清除动作都不模拟用户（Type 3170）——以普通方式启动安装程序再进维护向导时，模拟用户的延迟动作拿不到管理员权限，
    # 删不掉 ProgramData 与 Program Files 下的文件（动作照常「成功」，什么都没删）；服务端的数据与配置都是机器级的
    foreach ($action in 'ClearDataDelServerDir', 'ClearDataDelServerDb', 'ClearDataDelAppSettings') {
        $typeRow = Get-MsiRows $db "SELECT Type FROM CustomAction WHERE Action = '$action'"
        if ($typeRow.Count -ne 1 -or $typeRow[0][0] -ne '3170') { throw "清除动作 $action 应是不模拟用户的延迟动作，失败不阻断（Type 3170），实际 Type $($typeRow[0][0])" }
    }

    # ---- 卸载时「清除用户数据」的询问：两条入口都接到询问页，询问页再进「确认删除」页 ----
    # 防回归警示：自带向导组的「删除」默认直接跳确认页，不经过界面序列——询问页只排在界面序列里时，
    # 用户走得到的卸载（重跑安装包选删除）永远不会问。
    $removeMode = 'WixUI_InstallMode = "Remove"'
    $removeButton = @($events | Where-Object { $_[0] -ceq 'MaintenanceTypeDlg' -and $_[1] -ceq 'RemoveButton' })
    $toQuestion = @($removeButton | Where-Object { $_[2] -ceq 'NewDialog' -and $_[3] -ceq 'ClearDataDlg' })
    $libraryRows = @($removeButton | Where-Object { ($_[2] -ceq 'NewDialog' -and $_[3] -ceq 'VerifyReadyDlg') -or ($_[2] -ceq '[WixUI_InstallMode]' -and $_[3] -ceq 'Remove') })
    if ($toQuestion.Count -ne 1 -or $toQuestion[0][4] -cne '1' -or $libraryRows.Count -ne 2) { throw '维护向导的「删除」应跳到询问页 ClearDataDlg；自带向导组在它上面的登记（直接跳确认页、置安装模式）变了，请复核询问页的接法' }
    foreach ($row in $libraryRows) {
        if ([int]::Parse($toQuestion[0][5]) -le [int]::Parse($row[5])) { throw "维护向导「删除」上跳到询问页的登记顺序必须大于自带向导组的登记（$($row[5])）：否则要么不显示询问页，要么显示时安装模式还没置好" }
    }
    # 询问页上的事件登记一条不多、一条不少：多出一条「继续」直接关窗的登记，卸载就会跳过确认页立刻开始
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
    # 界面序列：命令行带 REMOVE=ALL 时先显示询问页，之后不再显示界面库的「继续安装」页（它按安装措辞写成）。条件按状态逐一求值
    $questionShow = Get-MsiRows $db "SELECT Condition FROM InstallUISequence WHERE Action = 'ClearDataDlg'"
    $resumeShow = Get-MsiRows $db "SELECT Condition FROM InstallUISequence WHERE Action = 'ResumeDlg'"
    if ($questionShow.Count -ne 1 -or $resumeShow.Count -ne 1) { throw '界面序列里应有询问页 ClearDataDlg 与 ResumeDlg 各一行' }
    $uninstallUiStates = @(
        @{ Name = '手动卸载（命令行 REMOVE=ALL 或 msiexec /x）'; State = @{ Installed = $true; REMOVE = 'ALL'; Preselected = $true }; Question = $true; ResumePage = $false },
        @{ Name = '全新安装、维护运行、修复'; State = @{}; Question = $false; ResumePage = $false },
        @{ Name = '继续被中断的安装'; State = @{ Installed = $true; RESUME = $true }; Question = $false; ResumePage = $true },
        @{ Name = '命令行预选了功能的其他运行'; State = @{ Installed = $true; Preselected = $true }; Question = $false; ResumePage = $true }
    )
    foreach ($state in $uninstallUiStates) {
        if ((Test-MsiCondition $questionShow[0][0] $state.State) -ne $state.Question) { throw "询问页在「$($state.Name)」时应当$(if ($state.Question) { '显示' } else { '不显示' })，当前条件不是这样：$($questionShow[0][0])" }
        if ((Test-MsiCondition $resumeShow[0][0] $state.State) -ne $state.ResumePage) { throw "界面库的「继续安装」页在「$($state.Name)」时应当$(if ($state.ResumePage) { '显示' } else { '不显示' })，当前条件不是这样：$($resumeShow[0][0])" }
    }
    if ((Test-MsiCondition $questionShow[0][0] @{ REMOVE = 'ALL'; UPGRADINGPRODUCTCODE = $true }) -ne $false) { throw "升级过程中旧产品的卸载子会话不得显示询问页：$($questionShow[0][0])" }
    # 命令行卸载的进度页：界面库按「命令行预选了功能」把「正在安装」的两段文字也显示出来，和「正在删除」叠在一起。
    # 询问页之后清掉预选标记，并让维护欢迎页（界面库的条件看的是这个标记的反面）不在这一路径上显示
    $clearPreselected = Get-MsiRows $db "SELECT Type, Source, Target FROM CustomAction WHERE Action = 'ClearPreselectedAfterQuestion'"
    if ($clearPreselected.Count -ne 1 -or $clearPreselected[0][0] -ne '51' -or $clearPreselected[0][1] -cne 'Preselected' -or $clearPreselected[0][2] -cne '') { throw 'ClearPreselectedAfterQuestion 应是只把 Preselected 置空的赋值动作（否则命令行卸载的进度页上「正在安装」与「正在删除」叠在一起）' }
    $questionSeq = Get-MsiRows $db "SELECT Sequence FROM InstallUISequence WHERE Action = 'ClearDataDlg'"
    $clearRow = Get-MsiRows $db "SELECT Condition, Sequence FROM InstallUISequence WHERE Action = 'ClearPreselectedAfterQuestion'"
    if ($clearRow.Count -ne 1 -or $clearRow[0][0] -cne 'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE') { throw "ClearPreselectedAfterQuestion 应只在手动卸载（命令行 REMOVE=ALL）时执行，实际：$($clearRow | ForEach-Object { $_[0] })" }
    if ($questionSeq.Count -ne 1 -or [int]::Parse($clearRow[0][1]) -le [int]::Parse($questionSeq[0][0]) -or [int]::Parse($clearRow[0][1]) -ge [int]::Parse((Get-MsiRows $db "SELECT Sequence FROM InstallUISequence WHERE Action = 'ResumeDlg'")[0][0])) { throw 'ClearPreselectedAfterQuestion 应排在询问页之后、「继续安装」页之前' }
    $maintenanceShow = Get-MsiRows $db "SELECT Condition FROM InstallUISequence WHERE Action = 'MaintenanceWelcomeDlg'"
    if ($maintenanceShow.Count -ne 1) { throw '界面序列里应有维护欢迎页 MaintenanceWelcomeDlg 一行' }
    # 预选标记已清掉之后的状态：维护欢迎页只在重跑安装包时显示
    $maintenanceStates = @(
        @{ Name = '重跑安装包'; State = @{ Installed = $true }; Shows = $true },
        @{ Name = '命令行 REMOVE=ALL 的手动卸载（预选标记已清掉）'; State = @{ Installed = $true; REMOVE = 'ALL' }; Shows = $false },
        @{ Name = '继续被中断的安装'; State = @{ Installed = $true; RESUME = $true }; Shows = $false },
        @{ Name = '小版本更新（补丁）'; State = @{ Installed = $true; PATCH = $true }; Shows = $false },
        @{ Name = '全新安装'; State = @{}; Shows = $false }
    )
    foreach ($state in $maintenanceStates) {
        if ((Test-MsiCondition $maintenanceShow[0][0] $state.State) -ne $state.Shows) { throw "维护欢迎页在「$($state.Name)」时应当$(if ($state.Shows) { '显示' } else { '不显示' })，当前条件不是这样：$($maintenanceShow[0][0])" }
    }

    # ---- 停服务的时机 ----
    # 停掉的服务靠 Windows Installer 的标准启动动作启动，它只启动本次运行里有变更的服务：
    # 什么都不改的维护运行（对已安装的服务端重跑一遍安装包）不能停，否则服务停了没人启动。条件按运行状态逐个求值。
    $stopRows = Get-MsiRows $db "SELECT Condition FROM InstallExecuteSequence WHERE Action = 'StopServerService'"
    if ($stopRows.Count -ne 1) { throw '停服务动作 StopServerService 应在安装执行序列里出现一次' }
    $stopRuns = @(
        @{ Name = '全新安装'; State = @{}; Runs = $true },
        @{ Name = '升级中新产品的会话'; State = @{}; Runs = $true },
        @{ Name = '升级过程中旧产品的卸载子会话'; State = @{ REMOVE = $true }; Runs = $true },
        @{ Name = '手动卸载'; State = @{ Installed = $true; REMOVE = $true }; Runs = $true },
        @{ Name = '修复'; State = @{ Installed = $true; REINSTALL = $true }; Runs = $true },
        @{ Name = '小版本更新（补丁）'; State = @{ Installed = $true; PATCH = $true }; Runs = $true },
        @{ Name = '什么都不改的维护运行'; State = @{ Installed = $true }; Runs = $false }
    )
    foreach ($case in $stopRuns) {
        if ((Test-MsiCondition $stopRows[0][0] $case.State) -ne $case.Runs) {
            throw "停服务动作在「$($case.Name)」时应当$(if ($case.Runs) { '执行' } else { '跳过' })，当前条件不是这样：$($stopRows[0][0])"
        }
    }
    $stopAction = Get-MsiRows $db "SELECT Type, Target FROM CustomAction WHERE Action = 'StopServerService'"
    # Type 3170 = EXE（34）+ 延迟执行（0x400）+ 不模拟用户（0x800）+ 忽略返回值（0x40）：全新安装时服务还不存在，停不到不算失败
    if ($stopAction.Count -ne 1 -or $stopAction[0][0] -ne '3170' -or -not $stopAction[0][1].EndsWith('sc.exe stop LabelFrameServer')) { throw 'StopServerService 应是不模拟用户、失败不阻断的延迟动作，执行 sc.exe stop LabelFrameServer' }

    # ---- 缺少运行时的对话框：网址是文字，打开靠按钮 ----
    # 「打开下载页」是安装器扩展库的「请桌面外壳启动」动作：浏览器由桌面外壳以当前登录用户的不提权身份启动，
    # 安装程序以管理员身份运行时也不跟着提权；打不开不影响向导。Type 65 = Binary 表里的 DLL（1）+ 忽略返回值（64）
    $downloadUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0'
    $openAction = Get-MsiRows $db "SELECT Type, Source, Target FROM CustomAction WHERE Action = 'OpenDownloadPage'"
    if ($openAction.Count -ne 1 -or $openAction[0][0] -ne '65' -or $openAction[0][1] -cne 'Wix4UtilCA_X64' -or $openAction[0][2] -cne 'WixUnelevatedShellExec') {
        throw 'OpenDownloadPage 必须是同步、失败不阻断的 x64「请桌面外壳启动」动作（Wix4UtilCA_X64 / WixUnelevatedShellExec）'
    }
    $setTarget = Get-MsiRows $db "SELECT Type, Source, Target FROM CustomAction WHERE Action = 'SetDownloadPageTarget'"
    if ($setTarget.Count -ne 1 -or $setTarget[0][0] -ne '51' -or $setTarget[0][1] -cne 'WixUnelevatedShellExecTarget' -or $setTarget[0][2] -cne $downloadUrl) {
        throw "SetDownloadPageTarget 应把官方下载页（$downloadUrl）经属性 WixUnelevatedShellExecTarget 交给打开动作"
    }
    # 按钮上的两条事件按登记顺序执行：先赋目标（1），再打开（2）
    foreach ($step in @(@('SetDownloadPageTarget', 1), @('OpenDownloadPage', 2))) {
        $row = @($events | Where-Object { $_[0] -ceq 'RuntimeMissingDlg' -and $_[1] -ceq 'OpenDownloadButton' -and $_[2] -ceq 'DoAction' -and $_[3] -ceq $step[0] })
        if ($row.Count -ne 1 -or $row[0][4] -cne '1' -or [int]::Parse($row[0][5]) -ne $step[1]) {
            throw "RuntimeMissingDlg 的 OpenDownloadButton 应按登记顺序 $($step[1]) 触发 $($step[0])，先赋目标后打开缺一不可"
        }
    }
    # 防回归警示：安装器的 Hyperlink 控件对鼠标点击没有反应，只有焦点在链接上时按回车才打开
    $dialogControls = Get-MsiRows $db "SELECT Dialog_, Control, Type, Text FROM Control"
    if (@($dialogControls | Where-Object { $_[2] -ceq 'Hyperlink' }).Count -ne 0) { throw '包里不得再有 Hyperlink 控件：鼠标点击没有反应。网址用文字显示，打开动作放在「打开下载页」按钮上' }
    $urlText = @($dialogControls | Where-Object { $_[0] -ceq 'RuntimeMissingDlg' -and $_[1] -ceq 'RuntimeDownloadUrl' })
    if ($urlText.Count -ne 1 -or $urlText[0][2] -cne 'Text' -or $urlText[0][3] -cne $downloadUrl) { throw "缺少运行时的对话框应把官方下载页的网址显示为文字（$downloadUrl）" }
    $openButton = @($dialogControls | Where-Object { $_[0] -ceq 'RuntimeMissingDlg' -and $_[1] -ceq 'OpenDownloadButton' })
    if ($openButton.Count -ne 1 -or $openButton[0][2] -cne 'PushButton' -or -not $openButton[0][3].Contains('打开下载页')) { throw '缺少运行时的对话框应有「打开下载页」按钮 OpenDownloadButton' }

    Write-Host "安装目录：$installDirId（非公开属性，固定为 Program Files\LabelFrame\Server）；INSTALLFOLDER 拦截；程序文件与用户配置挂载在该目录：OK"
    Write-Host '安装目录页：只读展示安装目录；向导链路：许可页 -> 安装目录页 -> 确认页；维护流程的上一步不受影响；可改目录的自带页面没有入口：OK'
    Write-Host '卸载询问：维护向导的「删除」先经询问页；询问页的「继续」先置安装模式再进「确认删除」页，上一步回询问页；命令行 REMOVE=ALL 时询问之后不再显示「继续安装」页，进度页只显示「正在删除」：OK'
    Write-Host '清除用户数据：删除的是安装目录下的 appsettings.json，且只在手动卸载并勾选时执行：OK'
    Write-Host '停服务：全新安装、升级、卸载、修复、补丁时先停服务，什么都不改的维护运行不动服务：OK'
    Write-Host '缺少运行时的对话框：网址是文字，「打开下载页」按钮经扩展库「请桌面外壳启动」动作以不提权身份打开浏览器，包里没有 Hyperlink 控件：OK'
    Write-Host '断言通过：Server MSI 安装位置契约。'
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
