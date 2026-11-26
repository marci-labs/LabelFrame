# 验证 Server MSI 的安装位置契约：安装目录不可从外部改写、对 INSTALLFOLDER 的拦截、只读的安装目录页、
# 程序文件的挂载目录、清除用户数据的路径。任一条不满足即抛错：安装位置一旦能被改写，不带目录的升级会落到
# 默认目录、用户改过的 appsettings.json 随之失效，卸载时的「清除用户数据」也会删错地方。
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
# 其余为真）、与 "1" 的等值和不等比较、裸属性名；出现别的写法直接报错，而不是算出一个看似可信的结果。
# 空条件恒真、没有赋值的属性按假处理，都与安装器一致。
function Test-MsiCondition([string]$Condition, [hashtable]$State) {
    if ($null -eq $Condition -or $Condition.Trim() -eq '') { return $true }
    # 小写的关键字在安装器里算关键字还是属性名，这里不替它猜：一律按不会求值处理
    foreach ($keyword in [regex]::Matches($Condition, '(?i)\b(?:not|and|or)\b')) {
        if ($keyword.Value -cnotmatch '^(NOT|AND|OR)$') { throw "条件里的关键字必须大写，这里不求值：$Condition" }
    }
    $open = ($Condition.ToCharArray() | Where-Object { $_ -eq '(' }).Count
    $close = ($Condition.ToCharArray() | Where-Object { $_ -eq ')' }).Count
    if ($open -ne $close) { throw "条件的括号不配对，这里不求值：$Condition" }
    $pattern = '[A-Za-z_][A-Za-z0-9_]*(?:\s*(?:<>|=)\s*"1")?|\d+'
    $translate = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $text = $match.Value
        $name = [regex]::Match($text, '^[A-Za-z_][A-Za-z0-9_]*').Value
        if ($name -eq '') { return '$' + ([int]::Parse($text) -ne 0).ToString().ToLowerInvariant() }
        if ($text -match '<>\s*"1"$') { return '(-not [bool]$' + $name + ')' }
        if ($text -match '=\s*"1"$') { return '([bool]$' + $name + ')' }
        if ($name -ceq 'NOT') { return '-not' }
        if ($name -ceq 'AND') { return '-and' }
        if ($name -ceq 'OR') { return '-or' }
        return '([bool]$' + $name + ')'
    }
    $expr = [regex]::Replace($Condition, $pattern, $translate)
    $leftover = [regex]::Replace($Condition, $pattern, '') -replace '[()\s]', ''
    if ($leftover.Length -ne 0) { throw "条件用到了本断言不会求值的语法：$Condition" }
    foreach ($key in $State.Keys) { Set-Variable -Name $key -Value ([bool]$State[$key]) -Scope Local }
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
        @{ Name = '维护：修复'; State = @{ Installed = $true }; FromLicense = ''; FromVerify = 'MaintenanceTypeDlg' },
        @{ Name = '维护：卸载'; State = @{ Installed = $true; REMOVE = $true }; FromLicense = ''; FromVerify = 'MaintenanceTypeDlg' },
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

    Write-Host "安装目录：$installDirId（非公开属性，固定为 Program Files\LabelFrame\Server）；INSTALLFOLDER 拦截；程序文件与用户配置挂载在该目录：OK"
    Write-Host '安装目录页：只读展示安装目录；向导链路：许可页 -> 安装目录页 -> 确认页；维护流程的上一步不受影响；可改目录的自带页面没有入口：OK'
    Write-Host '清除用户数据：删除的是安装目录下的 appsettings.json，且只在手动卸载并勾选时执行：OK'
    Write-Host '断言通过：Server MSI 安装位置契约。'
    Write-Host ''
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
}
