# LabelFrame 引导升级走查（迭代 64 / Issue #57「范围修订 v2」·DESIGN §6.11 决策 #126）
#
# 目的：BA 升级模式「看」的部分 + Burn 升级执行链的本地自证（per-user，全程免提权）——
#   S1 装旧：全新安装 v0.90.1（真实 per-user MSI + Burn MsiPackage 链）；
#   S2 升新：重跑 v0.90.2 Bundle——欢迎页「可升级清单（组件、现版本 → 新版本）」→ 确认 →
#            Burn RelatedBundle 升级（移除旧 Bundle）+ MSI MajorUpgrade 覆盖（0.90.1 → 0.90.2）；
#   S3 已最新：再跑 v0.90.2——欢迎页明确「已是最新」→ 继续执行按幂等口径跳过（MSI Present）。
#
# 载体与安全边界：
#   - 测试 MSI 为 wix 现建的极小 per-user 包（一个 HKCU 注册表组件），UpgradeCode 使用生产 Server / Client
#     MSI 的 UpgradeCode（探测按生产口径真实命中）；**不带 MajorUpgrade / Upgrade 表**——0.90.1 → 0.90.2 的
#     替换由 Burn RelatedBundle 升级驱动（新 Bundle 计划卸载旧 Bundle → 旧 Bundle 卸载其自有 MSI，走 Burn
#     自有注册，绝不触碰本机真实安装；生产 MSI 的 MajorUpgrade 覆盖链为 MSI 既有语义，不在 per-user 载体重复）；
#   - 探测断言取「同 UpgradeCode 族内最高版本」（§6.11）：测试版本 0.90.x > 本机真实安装版本（如 0.25.0），
#     装旧后族内最高即测试版本，断言确定性不受本机既有安装影响；
#   - 全 per-user（MSI Scope=perUser + Bundle MsiPackage PerMachine=no）：不触发 UAC、不写 HKLM。
#
# 用法（仓库根目录，Windows PowerShell 5.1+）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-upgrade-walkthrough.ps1
#   可选：-SkipBuild（复用已构建产物）/ -Scenarios 'S1,S2,S3'
# 证据：artifacts\bootstrapper-tests\upgrade\*.log（Burn 日志）+ summary.txt。
# 临时产物不进仓（artifacts/ 已 gitignore）；本脚本随仓版本化（走查可复现）。
param(
    [string]$WixPath = '',
    [string]$Scenarios = 'S1,S2,S3',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'artifacts\bootstrapper-tests\upgrade'
$bundleName = 'LabelFrame 升级走查测试'
$baProcessName = 'LabelFrame.Bootstrapper.Ba'

# 固定 GUID（可重复运行 + 确定性清理；Bundle）；测试 MSI 的 ProductCode 由 wix 每次构建生成（构建后 COM 提取，供清理卸载）
$bundleUpgradeCode = '7E9C1D42-6A50-4E1B-9C33-64AB9505C201'
$serverUpgradeCode = '{10BDDF3E-BD37-4C3A-A19E-56CA9EFE5B74}' # = packaging/main-server.wxs（生产口径探测）
$clientUpgradeCode = '{EE3F2357-56CE-4C0F-AEC1-4C04B6E6BCDA}' # = packaging/main.wxs
$script:liveProductCodes = New-Object System.Collections.Generic.List[string] # 构建期提取的 MSI ProductCode（清理卸载用）
$oldVersion = '0.90.1'
$newVersion = '0.90.2'

# ---------- MSI ProductCode 提取（WindowsInstaller COM 读 Property 表）----------
function Get-MsiProductCode([string]$MsiPath) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    try {
        $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($MsiPath, 0))
        $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property='ProductCode'"))
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
    }
    finally {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
    }
}

# ---------- 前置：wix ----------
$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（wix.exe），请安装 WiX Toolset v7 或以 -WixPath 指定。" }
$wixVersion = & $wix --version
Write-Host "WiX：$wixVersion"

# ---------- Win32 UI 驱动（复刻 test-bundle-download-matrix.ps1：注册类名子串 + 标题通配）----------
Add-Type -Namespace UpgradeWalk -Name Native -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
"@

function Get-DescendantWindows([IntPtr]$parent) {
    $items = New-Object System.Collections.Generic.List[object]
    $callback = [UpgradeWalk.Native+EnumProc]{
        param($hwnd, $lParam)
        $textBuilder = New-Object System.Text.StringBuilder 512
        [void][UpgradeWalk.Native]::GetWindowText($hwnd, $textBuilder, 512)
        $classBuilder = New-Object System.Text.StringBuilder 128
        [void][UpgradeWalk.Native]::GetClassName($hwnd, $classBuilder, 128)
        $items.Add([pscustomobject]@{ Handle = $hwnd; Class = $classBuilder.ToString(); Caption = $textBuilder.ToString() })
        return $true
    }
    [void][UpgradeWalk.Native]::EnumChildWindows($parent, $callback, [IntPtr]::Zero)
    return $items
}

function Find-ChildByCaption([IntPtr]$parent, [string]$class, [string]$captionLike) {
    foreach ($window in (Get-DescendantWindows $parent)) {
        if ($window.Class.Contains($class) -and $window.Caption -like $captionLike) { return $window }
    }
    return $null
}

function Wait-ChildByCaption([IntPtr]$parent, [string]$class, [string]$captionLike, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $found = Find-ChildByCaption $parent $class $captionLike
        if ($found) { return $found }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Click-Button([IntPtr]$hwnd) {
    [void][UpgradeWalk.Native]::SendMessage($hwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
}

function Set-EditText([IntPtr]$hwnd, [string]$text) {
    [void][UpgradeWalk.Native]::SendMessage($hwnd, 0x000C, [IntPtr]::Zero, $text) # WM_SETTEXT
}

# ---------- MSI 版本探测断言（与生产 LocalInstallProbe 同口径：UpgradeCode 族内取最高）----------
Add-Type -Namespace UpgradeWalk -Name Msi -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, System.Text.StringBuilder productCode, ref int length);
[System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int MsiGetProductInfo(string productCode, string property, System.Text.StringBuilder value, ref int length);
"@
function Get-InstalledFamilyVersion([string]$upgradeCode) {
    $best = $null
    for ($index = 0; ; $index++) {
        $buffer = New-Object System.Text.StringBuilder 64
        $length = 64
        $enumResult = [UpgradeWalk.Msi]::MsiEnumRelatedProducts($upgradeCode, 0, $index, $buffer, [ref]$length)
        if ($enumResult -ne 0) { break }
        $productCode = $buffer.ToString()
        $versionBuffer = New-Object System.Text.StringBuilder 64
        $versionLength = 64
        if ([UpgradeWalk.Msi]::MsiGetProductInfo($productCode, 'VersionString', $versionBuffer, [ref]$versionLength) -eq 0) {
            $version = $versionBuffer.ToString().TrimEnd([char]0)
            if ($null -eq $best -or [version]$version -gt [version]$best) { $best = $version }
        }
    }
    return $best
}

# 族内测试残留卸载（版本 ≥ 0.90.0 才卸——本机真实安装（如 0.25.0）永不动）；Burn 装的 MSI 带
# ARPSYSTEMCOMPONENT=1 不进 ARP，HKCU 扫描扫不到，必须走 MSI API 族枚举兜底
function Remove-TestFamilyProducts {
    foreach ($upgradeCode in @($serverUpgradeCode, $clientUpgradeCode)) {
        for ($index = 0; ; $index++) {
            $buffer = New-Object System.Text.StringBuilder 64
            $length = 64
            $enumResult = [UpgradeWalk.Msi]::MsiEnumRelatedProducts($upgradeCode, 0, $index, $buffer, [ref]$length)
            if ($enumResult -ne 0) { break }
            $productCode = $buffer.ToString()
            $versionBuffer = New-Object System.Text.StringBuilder 64
            $versionLength = 64
            if ([UpgradeWalk.Msi]::MsiGetProductInfo($productCode, 'VersionString', $versionBuffer, [ref]$versionLength) -ne 0) { continue }
            $version = $versionBuffer.ToString().TrimEnd([char]0)
            if ([version]$version -ge [version]'0.90.0') {
                $proc = Start-Process -FilePath msiexec.exe -ArgumentList @('/x', $productCode, '/qn') -PassThru -Wait -ErrorAction SilentlyContinue
                Write-Host "  清理测试族产品：$productCode（$version）→ exit $($proc.ExitCode)"
            }
        }
    }
}

# ---------- 构建测试 MSI（极小 per-user 包：一个 HKCU 注册表组件；ProductCode 构建后提取）----------
function New-TestMsi([string]$Role, [string]$Version, [string]$UpgradeCode, [string]$Name) {
    $wxs = Join-Path $staging "$Role-$Version.wxs"
    $msi = Join-Path $staging "$Role-$Version.msi"
    $content = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 升级走查测试 MSI（自动生成，勿手工编辑；迭代 64 / #57）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Package Name=`"$Name`" Manufacturer=`"LabelFrame`" Version=`"$Version`" UpgradeCode=`"$UpgradeCode`" Scope=`"perUser`" Codepage=`"65001`">"
        '    <StandardDirectory Id="ProgramFilesFolder">'
        '      <Directory Id="LabelFrameUpgradeWalk" Name="LabelFrameUpgradeWalk">'
        '        <Component Id="Marker" Guid="*">'
        "          <RegistryValue Root=`"HKCU`" Key=`"Software\LabelFrame\UpgradeWalkthrough`" Name=`"$Role`" Value=`"$Version`" Type=`"string`" KeyPath=`"yes`" />"
        '        </Component>'
        '      </Directory>'
        '    </StandardDirectory>'
        '  </Package>'
        '</Wix>'
    )
    [System.IO.File]::WriteAllLines($wxs, $content, (New-Object System.Text.UTF8Encoding($false)))
    & $wix build $wxs -o $msi -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build failed（测试 MSI $Role $Version）" }
    $script:liveProductCodes.Add((Get-MsiProductCode $msi)) | Out-Null
    return $msi
}

# ---------- 构建测试 Bundle（双 MsiPackage 内嵌压缩链 + 真实 BA；per-user）----------
function New-TestBundle([string]$Version, [string]$ServerMsi, [string]$ClientMsi, [string]$OutputPath) {
    $baDir = Join-Path (Split-Path $staging -Parent) 'ba' # 与下载矩阵脚本共用 BA 发布目录（artifacts\bootstrapper-tests\ba）
    $wxsLines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 升级走查测试 Bundle（自动生成，勿手工编辑；迭代 64 / #57）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Bundle Name=`"$bundleName`" Version=`"$Version`" Manufacturer=`"LabelFrame`" UpgradeCode=`"$bundleUpgradeCode`">"
        '    <BootstrapperApplication SourceFile="$(var.BaDir)LabelFrame.Bootstrapper.Ba.exe">'
        '      <Payloads Include="$(var.BaDir)**">'
        '        <Exclude Files="$(var.BaDir)LabelFrame.Bootstrapper.Ba.exe" />'
        '        <Exclude Files="$(var.BaDir)*.pdb" />'
        '      </Payloads>'
        '    </BootstrapperApplication>'
        '    <Variable Name="InstallPreset" Type="string" Value="" />'
        '    <Variable Name="InstallServer" Type="numeric" Value="0" />'
        '    <Variable Name="InstallClient" Type="numeric" Value="0" />'
        '    <Variable Name="InstallWebUi" Type="numeric" Value="0" />'
        '    <Variable Name="InstallPluginZebra" Type="numeric" Value="0" />'
        '    <Variable Name="DesktopRuntimeInstalled" Type="numeric" Value="1" />'
        '    <Variable Name="AspNetCoreRuntimeInstalled" Type="numeric" Value="1" />'
        '    <Variable Name="WebView2Installed" Type="numeric" Value="1" />'
        '    <Variable Name="WebUiTargetDir" Type="string" Value="" />'
        '    <Variable Name="PluginZebraTargetDir" Type="string" Value="" />'
        '    <Chain>'
        "      <MsiPackage Id=`"ServerMsi`" SourceFile=`"$ServerMsi`" Compressed=`"yes`" InstallCondition=`"InstallServer`" />"
        "      <MsiPackage Id=`"ClientMsi`" SourceFile=`"$ClientMsi`" Compressed=`"yes`" InstallCondition=`"InstallClient`" />"
        '    </Chain>'
        '  </Bundle>'
        '</Wix>'
    )
    $wxs = Join-Path $staging "Bundle-$Version.wxs"
    [System.IO.File]::WriteAllLines($wxs, $wxsLines, (New-Object System.Text.UTF8Encoding($false)))
    & $wix build $wxs -d "BaDir=$baDir\" -o $OutputPath -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build failed（测试 Bundle $Version）" }
    return $OutputPath
}

# ---------- 测试清单（schema 严格：sha256 / sizeBytes 按测试 MSI 实测）----------
function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-TestManifest([string]$Dir, [string]$Version, [string]$ServerMsi, [string]$ClientMsi, [string]$LatestVersion) {
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    $entry = {
        param($id, $path)
        $sha = Get-Sha256 $path
        $size = (Get-Item $path).Length
        "    {`n      `"id`": `"$id`", `"type`": `"msi`", `"version`": `"$Version`", `"dependsOn`": [],`n      `"urls`": [`"http://127.0.0.1:1/$id.msi`"],`n      `"sha256`": `"$sha`",`n      `"sizeBytes`": $size,`n      `"silentArgs`": `"`", `"topologies`": [`"standalone`"], `"notes`": `"升级走查测试`"`n    }"
    }
    $manifestJson = "{`n  `"schemaVersion`": 1,`n  `"labelframeVersion`": `"$Version`",`n  `"generatedAt`": `"2026-09-14T00:00:00Z`",`n  `"components`": [`n$(& $entry 'server-msi' $ServerMsi),`n$(& $entry 'client-msi' $ClientMsi)`n  ]`n}`n"
    [System.IO.File]::WriteAllText((Join-Path $Dir 'install-manifest.json'), $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
    # latest.json 同目录内嵌（§6.11 清单新鲜度消费）：S1/S2 旧清单场景给出「清单非最新」提示素材
    $latestJson = "{`n  `"labelframeVersion`": `"$LatestVersion`",`n  `"manifestUrl`": `"http://127.0.0.1:1/install-manifest.json`"`n}`n"
    [System.IO.File]::WriteAllText((Join-Path $Dir 'latest.json'), $latestJson, (New-Object System.Text.UTF8Encoding($false)))
    return (Join-Path $Dir 'install-manifest.json')
}

# ---------- 清理（开工前 + 收尾；按 HKCU 卸载键 DisplayName 扫描测试 MSI（-SkipBuild 重跑也能清）+ Bundle 注册按名称移除）----------
function Clear-TestState {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    $codes = @()
    if (Test-Path $uninstallRoot) {
        foreach ($key in (Get-ChildItem $uninstallRoot -ErrorAction SilentlyContinue)) {
            $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
            if ($displayName -like 'LabelFrame 升级走查·*') { $codes += $key.PSChildName }
            if ($displayName -eq $bundleName) {
                Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  已清理测试 Bundle 注册：$($key.PSChildName)"
            }
        }
    }
    $codes = @($codes) + @($script:liveProductCodes) | Select-Object -Unique
    foreach ($code in $codes) {
        if ($code -notlike '{*}') { continue }
        $proc = Start-Process -FilePath msiexec.exe -ArgumentList @('/x', $code, '/qn') -PassThru -Wait -ErrorAction SilentlyContinue
        Write-Host "  清理测试 MSI：$code → exit $($proc.ExitCode)（1605 = 未装，忽略）"
    }
    Remove-TestFamilyProducts
}

# ---------- 驱动向导 + 抓确认页横幅（清单加载成功会自动进步，欢迎页摘要瞬时即逝——
# 同一评估文本由确认页 OnEnter 写入 Burn 日志（精确断言）+ 横幅 STATIC 驻留可抓（粗粒度状态断言））----------
function Invoke-WizardAndCaptureBanner([string]$BundleExe, [string]$ManifestPath, [string]$LogPath, [int]$TimeoutSeconds) {
    # 清单来源由命令行 --manifest 显式覆写（本地测试清单），向导启动即自动加载（迭代 95 / #151）
    $proc = Start-Process -FilePath $BundleExe -ArgumentList @('--manifest', $ManifestPath, '-l', $LogPath) -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(40)
        $mainHwnd = [IntPtr]::Zero
        while ((Get-Date) -lt $deadline -and $mainHwnd -eq [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 300
            foreach ($candidate in (Get-Process -Name $baProcessName -ErrorAction SilentlyContinue)) {
                if ($candidate.MainWindowTitle -like '*LabelFrame 安装引导*') { $mainHwnd = $candidate.MainWindowHandle; break }
            }
            if ($mainHwnd -eq [IntPtr]::Zero -and $proc.HasExited) { throw 'BA 向导窗口未出现且 Bundle 进程已退出' }
        }
        if ($mainHwnd -eq [IntPtr]::Zero) { throw '40 秒内未找到 BA 向导主窗口' }
        [void][UpgradeWalk.Native]::SetForegroundWindow($mainHwnd)

        # 就绪页：清单自动加载成功自动进打印机页（基础模式默认「仅打印客户端」，#151）
        $brandTitle = Wait-ChildByCaption $mainHwnd 'STATIC' '需要哪些打印机品牌*' 30
        if (-not $brandTitle) { throw '30 秒内未自动进入问卷（清单加载失败？）' }

        # 高级路径选服务端角色（#151 两层问卷）：上一步回就绪页 → 高级选项 → 下一步进角色页
        $backButton = Find-ChildByCaption $mainHwnd 'BUTTON' '上一步*'
        if (-not $backButton) { throw '未找到「上一步」按钮' }
        Click-Button $backButton.Handle
        Start-Sleep -Milliseconds 500
        $advancedButton = Find-ChildByCaption $mainHwnd 'BUTTON' '高级选项'
        if (-not $advancedButton) { throw '未找到「高级选项」按钮' }
        Click-Button $advancedButton.Handle
        Start-Sleep -Milliseconds 300
        $readyNext = Find-ChildByCaption $mainHwnd 'BUTTON' '下一步*'
        if (-not $readyNext) { throw '未找到就绪页「下一步」按钮' }
        Click-Button $readyNext.Handle

        # 角色页：选「本机作为服务端，并安装打印客户端」（双包都纳入：InstallServer + InstallClient）
        $standalone = Wait-ChildByCaption $mainHwnd 'BUTTON' '本机作为服务端，并安装打印客户端' 10
        if (-not $standalone) { throw '10 秒内未进入角色页' }
        Start-Sleep -Milliseconds 400
        Click-Button $standalone.Handle
        Start-Sleep -Milliseconds 300

        # 「下一步」×4：打印机 → 服务端地址（预填本机默认）→ 管理界面 → 确认页（升级状态行驻留，抓取 §6.11 状态）
        for ($click = 0; $click -lt 4; $click++) {
            $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
            if (-not $nextButton) { throw "未找到「下一步」按钮（第 $($click + 1) 次）" }
            Click-Button $nextButton.Handle
            Start-Sleep -Milliseconds 500
        }
        # 锚点集 = UpgradePresentation.Summarize 三态（可升级 / 已最新 / 全新安装），与确认页升级状态行 1:1；
        # 执行边界横幅（含「不会下载」字样）与清单新鲜度行是常驻 / 场景性行，不参与抓取——#181 教训：*不会下载* 曾抢走首命中致断言错位
        $banner = $null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            $hit = Get-DescendantWindows $mainHwnd | Where-Object {
                $_.Class.Contains('STATIC') -and (
                    $_.Caption -like '*检测到可用更新*' -or $_.Caption -like '*已是最新版本*' -or $_.Caption -like '*全新安装*')
            } | Select-Object -First 1
            if ($hit) { $banner = $hit.Caption; break }
            Start-Sleep -Milliseconds 100
        }
        if (-not $banner) { throw '10 秒内未抓到确认页状态行（STATIC 文本）' }

        # 第 5 次「下一步」= 开始安装 → 等终态（完成页 / 失败报告）
        $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
        if (-not $nextButton) { throw '未找到确认页「下一步（安装）」按钮' }
        Click-Button $nextButton.Handle

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $terminal = 'timeout'
        while ((Get-Date) -lt $deadline) {
            if ($proc.HasExited) { $terminal = 'exited'; break }
            if ((Find-ChildByCaption $mainHwnd 'STATIC' '安装完成')) { $terminal = 'complete'; break }
            if ((Find-ChildByCaption $mainHwnd 'BUTTON' '重试*')) { $terminal = 'failed'; break }
            Start-Sleep -Milliseconds 500
        }
        if ($terminal -eq 'timeout') { throw "场景超时（${TimeoutSeconds}s）未到终态" }

        if (-not $proc.HasExited) {
            [void][UpgradeWalk.Native]::SendMessage($mainHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
            if (-not $proc.WaitForExit(60000)) { throw '向导关闭超时' }
        }

        return [pscustomobject]@{ Terminal = $terminal; ExitCode = $proc.ExitCode; Banner = $banner }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        $proc.WaitForExit(10000) | Out-Null
        Get-Process -Name $baProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

# ---------- 断言与结果收集 ----------
$script:results = New-Object System.Collections.Generic.List[string]
$script:failed = 0

function Add-Result([string]$Scenario, [bool]$Pass, [string]$Detail) {
    $mark = if ($Pass) { '通过' } else { '失败' }
    $script:results.Add("$Scenario $mark — $Detail")
    if (-not $Pass) { $script:failed++ }
    $color = if ($Pass) { 'Green' } else { 'Red' }
    Write-Host "[$mark] $Scenario — $Detail" -ForegroundColor $color
}

function Assert-LogContains([string]$LogPath, [string]$Pattern, [string]$Scenario, [string]$What) {
    $hit = Select-String -LiteralPath $LogPath -Pattern $Pattern | Select-Object -First 1
    Add-Result $Scenario ([bool]$hit) "$What（日志$(if ($hit) { '命中' } else { '未命中' })「$Pattern」）"
}

# ---------- 构建 ----------
$bundleOld = Join-Path $staging "LabelFrame-UpgradeWalk-$oldVersion.exe"
$bundleNew = Join-Path $staging "LabelFrame-UpgradeWalk-$newVersion.exe"

if (-not $SkipBuild) {
    foreach ($dir in @($staging, (Join-Path $staging 'manifest-old'), (Join-Path $staging 'manifest-new'), (Join-Path (Split-Path $staging -Parent) 'ba'))) {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    Write-Host '发布 BA（net48 win-x64）…'
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path (Split-Path $staging -Parent) 'ba') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }

    & $wix eula accept wix7 2>$null | Out-Null
    $global:LASTEXITCODE = 0

    Write-Host "构建测试 MSI（$oldVersion / $newVersion × 服务端 / 客户端）…"
    $serverOldMsi = New-TestMsi 'Server' $oldVersion $serverUpgradeCode 'LabelFrame 升级走查·服务端'
    $serverNewMsi = New-TestMsi 'Server' $newVersion $serverUpgradeCode 'LabelFrame 升级走查·服务端'
    $clientOldMsi = New-TestMsi 'Client' $oldVersion $clientUpgradeCode 'LabelFrame 升级走查·客户端'
    $clientNewMsi = New-TestMsi 'Client' $newVersion $clientUpgradeCode 'LabelFrame 升级走查·客户端'

    Write-Host "构建测试 Bundle（$oldVersion / $newVersion，UpgradeCode $bundleUpgradeCode）…"
    New-TestBundle $oldVersion $serverOldMsi $clientOldMsi $bundleOld | Out-Null
    New-TestBundle $newVersion $serverNewMsi $clientNewMsi $bundleNew | Out-Null

    New-TestManifest (Join-Path $staging 'manifest-old') $oldVersion $serverOldMsi $clientOldMsi $newVersion | Out-Null
    New-TestManifest (Join-Path $staging 'manifest-new') $newVersion $serverNewMsi $clientNewMsi $newVersion | Out-Null
}
else {
    foreach ($path in @($bundleOld, $bundleNew)) {
        if (-not (Test-Path $path)) { throw "-SkipBuild 但未找到 $path，请先完整运行一次。" }
    }
}

$manifestOld = Join-Path $staging 'manifest-old\install-manifest.json'
$manifestNew = Join-Path $staging 'manifest-new\install-manifest.json'

# ---------- 走查 ----------
Clear-TestState
$selected = $Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }

try {
    foreach ($scenario in $selected) {
        Write-Host "`n========== 场景 $scenario ==========" -ForegroundColor Cyan
        $logPath = Join-Path $staging "$scenario.log"
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        Get-ChildItem $staging -Filter "$scenario`_*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue # 陈旧编号子日志（上一轮运行的包 / 旧 Bundle 日志）

        switch ($scenario) {
            'S1' {
                # 装旧：本机无 0.90.x（刚清理）→ 安装 0.90.1 后族内最高 = 0.90.1（真实安装若在，低于 0.90.x 不影响最高值）
                $run = Invoke-WizardAndCaptureBanner $bundleOld $manifestOld $logPath 180
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal) 确认页横幅=$($run.Banner)"
                $after = @{ Server = Get-InstalledFamilyVersion $serverUpgradeCode; Client = Get-InstalledFamilyVersion $clientUpgradeCode }
                Add-Result $scenario ($after.Server -eq $oldVersion -and $after.Client -eq $oldVersion) "装旧后族内最高版本：服务端 $($after.Server) / 客户端 $($after.Client)（期望 $oldVersion）"
                Assert-LogContains $logPath '确认安装计划' $scenario '确认计划日志'
            }
            'S2' {
                # 升新：重跑 v$newVersion Bundle——确认页「可升级清单」→ RelatedBundle 升级 + MSI MajorUpgrade 覆盖
                $run = Invoke-WizardAndCaptureBanner $bundleNew $manifestNew $logPath 240
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Add-Result $scenario ($run.Banner -like "*检测到可用更新：服务端 $oldVersion → $newVersion*") "确认页升级横幅：$($run.Banner)"
                Assert-LogContains $logPath "升级评估（§6.11）：检测到可用更新：服务端 $oldVersion → $newVersion" $scenario 'BA 可升级清单（服务端）'
                Assert-LogContains $logPath "打印客户端 $oldVersion → $newVersion" $scenario 'BA 可升级清单（客户端）'
                $after = @{ Server = Get-InstalledFamilyVersion $serverUpgradeCode; Client = Get-InstalledFamilyVersion $clientUpgradeCode }
                Add-Result $scenario ($after.Server -eq $newVersion -and $after.Client -eq $newVersion) "升新后族内最高版本：服务端 $($after.Server) / 客户端 $($after.Client)（期望 $newVersion）"
                Assert-LogContains $logPath 'Detected related bundle' $scenario 'Burn 相关 Bundle 检测（包级口径）'
                Assert-LogContains $logPath "version: $oldVersion" $scenario "相关 Bundle 版本（$oldVersion）"
                Assert-LogContains $logPath '检测到相关引导程序' $scenario 'BA 包级口径日志'
                # 旧 Bundle 卸载走 BA 非交互路径（升级链真实依赖：无人值守完成，不弹第二向导）
                $oldBundleLog = Get-ChildItem $staging -Filter "$scenario`_0*_*.log" -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\S+_\d{3}_\{[0-9A-Fa-f-]+\}\.log$' } | Select-Object -First 1
                if ($oldBundleLog) {
                    Assert-LogContains $oldBundleLog.FullName '非交互启动' $scenario '旧 Bundle 卸载非交互执行'
                }
                else {
                    Add-Result $scenario $false '未找到旧 Bundle 子日志（S2_00N_{guid}.log）'
                }
            }
            'S3' {
                # 已最新：再跑 v$newVersion——确认页明确「已是最新」→ 执行按幂等口径跳过（MSI Present）
                $before = @{ Server = Get-InstalledFamilyVersion $serverUpgradeCode; Client = Get-InstalledFamilyVersion $clientUpgradeCode }
                Add-Result "$scenario.pre" ($before.Server -eq $newVersion -and $before.Client -eq $newVersion) "前置：族内最高 = $newVersion（服务端 $($before.Server) / 客户端 $($before.Client)）"
                $run = Invoke-WizardAndCaptureBanner $bundleNew $manifestNew $logPath 240
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Add-Result $scenario ($run.Banner -like '*已是最新版本，无需重复安装*') "确认页「已是最新」横幅：$($run.Banner)"
                Assert-LogContains $logPath "升级评估（§6.11）：检测到已安装：服务端 $newVersion、打印客户端 $newVersion" $scenario 'BA 已是最新评估日志'
                $after = @{ Server = Get-InstalledFamilyVersion $serverUpgradeCode; Client = Get-InstalledFamilyVersion $clientUpgradeCode }
                Add-Result $scenario ($after.Server -eq $newVersion -and $after.Client -eq $newVersion) "重跑后版本不变（无重复安装）：$($after.Server) / $($after.Client)"
                Assert-LogContains $logPath "state: Present" $scenario 'Burn 检测已装（Present，跳过）'
            }
            default {
                Add-Result $scenario $false '未知场景标识'
            }
        }
    }
}
finally {
    # 收尾：卸载测试 MSI + 清 Bundle 注册（证据日志保留）
    Clear-TestState
}

# ---------- 汇总 ----------
$summary = @(
    "LabelFrame 引导升级走查 · $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Bundle：$bundleOld / $bundleNew（UpgradeCode $bundleUpgradeCode）；WiX $wixVersion",
    "测试 MSI UpgradeCode = 生产 Server / Client（探测生产口径命中）；MajorUpgrade 版本下限 0.90.0 隔离真实安装",
    ''
) + $script:results + @(
    '',
    "结论：$($script:results.Count) 项断言，失败 $script:failed 项。"
)
$summary | Set-Content -LiteralPath (Join-Path $staging 'summary.txt') -Encoding UTF8
Write-Host "`n========== 走查汇总（$($script:results.Count) 项断言，失败 $script:failed）==========" -ForegroundColor Cyan
$script:results | ForEach-Object { Write-Host "  $_" }
Write-Host "证据目录：$staging"
if ($script:failed -gt 0) { exit 1 }
exit 0
