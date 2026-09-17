# LabelFrame 引导卸载清理走查（迭代 69 / Issue #93 · DESIGN §6.8「卸载清理」/ 决策 #133）
#
# 目的：卸载链清理对称化的本地自证（per-user，全程免提权）——
#   S1 装（选 Zebra + 管理界面）：真实 BA 七页向导（Win32 UI 自动化）→ 真实 PayloadTool 落位
#      （-plugin / -place 复刻生产落位包机制，目标目录改指测试根，规避 ProgramData 提权）；
#   S2 ARP 卸载：`bundle.exe /uninstall`（BA 非交互路径，复刻「程序与功能」卸载形态）——
#      断言 web-ui 与 zebra 落位目录清理干净、用户手动放置的第三方 DLL 保留（不误伤）、MSI 组件正常卸载；
#   S3 卸载 → 重装不选 Zebra：再走向导（品牌页确保不勾 Zebra）——断言 zebra 目录不存在、webui 正常落位
#      （残留缺陷消除的对照实证：卸载前选 Zebra、重装不选，组件集合与问卷一致）。
#
# 载体与安全边界：
#   - 测试 Bundle = wix 现建 per-user 包（双极小 MSI（随机固定 UpgradeCode，不触生产族）+ 双落位 ExePackage + 双成对清理包
#     （真实 PayloadTool：落位包 Permanent + -version 写落位凭据；清理包 install 空操作 / UninstallArguments -clean——复刻
#     Bundle.wxs 成对清理包形态（决策 #133），目标目录为测试根字面量——生产为 BA 写入的 [变量]；清理包 DetectCondition 以
#     Burn 内建 WixBundleInstalled 替代生产 BA 凭据探测变量——测试落位根与 BA 探测的生产 ProgramData 路径不同源，属口径差异））；
#   - 全 per-user（MsiPackage Scope=perUser + ExePackage PerMachine=no）：不触发 UAC、不写 HKLM、不动 ProgramData；
#   - 第三方插件资产（平铺 DLL + 手动子目录）由脚本预置于测试根 plugins 下，断言卸载清理不误伤。
#
# 用法（仓库根目录，Windows PowerShell 5.1+）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-uninstall-walkthrough.ps1
#   可选：-SkipBuild（复用已构建产物）/ -Scenarios 'S1,S2,S3'
# 证据：artifacts\bootstrapper-tests\uninstall\*.log（Burn 日志）+ summary.txt。
# 临时产物不进仓（artifacts/ 已 gitignore）；本脚本随仓版本化（走查可复现）。
param(
    [string]$WixPath = '',
    [string]$Scenarios = 'S1,S2,S3',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'artifacts\bootstrapper-tests\uninstall'
$bundleName = 'LabelFrame 卸载走查测试'
$baProcessName = 'LabelFrame.Bootstrapper.Ba'
$placementRoot = Join-Path $staging 'placement-root' # ProgramData 等价替身（server\plugins\web-ui / Client\plugins\<pluginId>）
$testVersion = '0.91.1' # 末位非 0：MSI VersionString 归一化规避（0.x.0 可能被截短）

# 固定 GUID（可重复运行 + 确定性清理；Bundle / 测试 MSI 均为测试专用 UpgradeCode（带花括号——MSI API 口径），不触生产族探测）
$bundleUpgradeCode = '8F2D4E71-3B96-4C58-9D02-71A4E9B6C317'
$serverUpgradeCode = '{A61C5F2E-7D84-4B3A-B5E6-2C0F8D9A4E17}'
$clientUpgradeCode = '{B72D6E3F-8E95-4C4B-C6F7-3D1A9E0B5F28}'
$cacheIds = @('LfUninstallWalkServerMsi', 'LfUninstallWalkClientMsi', 'LfUninstallWalkWebUi', 'LfUninstallWalkZebra', 'LfUninstallWalkWebUiClean', 'LfUninstallWalkZebraClean')

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

# ---------- Win32 UI 驱动（复刻 test-bundle-upgrade-walkthrough.ps1：注册类名子串 + 标题通配）----------
Add-Type -Namespace UninstallWalk -Name Native -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
"@

function Get-DescendantWindows([IntPtr]$parent) {
    $items = New-Object System.Collections.Generic.List[object]
    $callback = [UninstallWalk.Native+EnumProc]{
        param($hwnd, $lParam)
        $textBuilder = New-Object System.Text.StringBuilder 512
        [void][UninstallWalk.Native]::GetWindowText($hwnd, $textBuilder, 512)
        $classBuilder = New-Object System.Text.StringBuilder 128
        [void][UninstallWalk.Native]::GetClassName($hwnd, $classBuilder, 128)
        $items.Add([pscustomobject]@{ Handle = $hwnd; Class = $classBuilder.ToString(); Caption = $textBuilder.ToString() })
        return $true
    }
    [void][UninstallWalk.Native]::EnumChildWindows($parent, $callback, [IntPtr]::Zero)
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
    [void][UninstallWalk.Native]::SendMessage($hwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
}

function Set-EditText([IntPtr]$hwnd, [string]$text) {
    [void][UninstallWalk.Native]::SendMessage($hwnd, 0x000C, [IntPtr]::Zero, $text) # WM_SETTEXT
}

# 复选框状态驱动：WinForms CheckBox 勾选态在托管侧，BM_GETCHECK 恒返 0（实测），不能读态——改「推算初始态 + BM_CLICK
# 按需翻转」：品牌页初始态 = ZDesigner 预选（#128 ④，与 BA PrinterBrandDetector 同口径：打印机名含 zdesigner 即预勾选 zebra）；
# 管理界面页初始态 = standalone 预设不勾（ManagementUiPage OnEnter：Checked = preset != Standalone）
function Get-ZebraPreselected {
    try {
        $printers = Get-Printer -ErrorAction Stop
        return [bool]($printers | Where-Object { $_.Name -like '*zdesigner*' })
    }
    catch {
        return $false # 打印服务不可用（InstalledPrinters 同口径按空集）
    }
}

function Set-CheckBoxByToggle([IntPtr]$hwnd, [bool]$initialChecked, [bool]$checked) {
    if ($checked -ne $initialChecked) {
        Click-Button $hwnd
        Start-Sleep -Milliseconds 200
    }
}

# ---------- MSI 族残留卸载（版本 ≥ 0.90.0 才卸；HKCU 扫描扫不到 Burn 装的 ARPSYSTEMCOMPONENT=1 包，走 MSI API 族枚举）----------
Add-Type -Namespace UninstallWalk -Name Msi -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, System.Text.StringBuilder productCode, ref int length);
[System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int MsiGetProductInfo(string productCode, string property, System.Text.StringBuilder value, ref int length);
"@
function Get-InstalledFamilyVersion([string]$upgradeCode) {
    $best = $null
    for ($index = 0; ; $index++) {
        $buffer = New-Object System.Text.StringBuilder 64
        $length = 64
        $enumResult = [UninstallWalk.Msi]::MsiEnumRelatedProducts($upgradeCode, 0, $index, $buffer, [ref]$length)
        if ($enumResult -ne 0) { break }
        $productCode = $buffer.ToString()
        $versionBuffer = New-Object System.Text.StringBuilder 64
        $versionLength = 64
        if ([UninstallWalk.Msi]::MsiGetProductInfo($productCode, 'VersionString', $versionBuffer, [ref]$versionLength) -eq 0) {
            $version = $versionBuffer.ToString().TrimEnd([char]0)
            if ($null -eq $best -or [version]$version -gt [version]$best) { $best = $version }
        }
    }
    return $best
}

function Remove-TestFamilyProducts {
    foreach ($upgradeCode in @($serverUpgradeCode, $clientUpgradeCode)) {
        for ($index = 0; ; $index++) {
            $buffer = New-Object System.Text.StringBuilder 64
            $length = 64
            $enumResult = [UninstallWalk.Msi]::MsiEnumRelatedProducts($upgradeCode, 0, $index, $buffer, [ref]$length)
            if ($enumResult -ne 0) { break }
            $productCode = $buffer.ToString()
            $versionBuffer = New-Object System.Text.StringBuilder 64
            $versionLength = 64
            if ([UninstallWalk.Msi]::MsiGetProductInfo($productCode, 'VersionString', $versionBuffer, [ref]$versionLength) -ne 0) { continue }
            $version = $versionBuffer.ToString().TrimEnd([char]0)
            if ([version]$version -ge [version]'0.90.0') {
                $proc = Start-Process -FilePath msiexec.exe -ArgumentList @('/x', $productCode, '/qn') -PassThru -Wait -ErrorAction SilentlyContinue
                Write-Host "  清理测试族产品：$productCode（$version）→ exit $($proc.ExitCode)"
            }
        }
    }
}

# ---------- 清理（开工前 + 收尾：Bundle 注册按名称移除 + 测试 MSI 族卸载 + per-user 包缓存 + 测试根）----------
function Clear-TestState {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (Test-Path $uninstallRoot) {
        foreach ($key in (Get-ChildItem $uninstallRoot -ErrorAction SilentlyContinue)) {
            $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
            if ($displayName -eq $bundleName) {
                Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  已清理测试 Bundle 注册：$($key.PSChildName)"
            }
        }
    }
    Remove-TestFamilyProducts
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'Package Cache'
    foreach ($cacheId in $cacheIds) {
        $dir = Join-Path $cacheRoot $cacheId
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  已清理包缓存：$cacheId"
        }
    }
    if (Test-Path $placementRoot) {
        Remove-Item -LiteralPath $placementRoot -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  已清理测试落位根：$placementRoot"
    }
}

# ---------- 构建测试 MSI（极小 per-user 包：一个 HKCU 注册表组件）----------
function New-TestMsi([string]$Role, [string]$UpgradeCode, [string]$Name) {
    $wxs = Join-Path $staging "$Role.wxs"
    $msi = Join-Path $staging "$Role.msi"
    $content = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 卸载走查测试 MSI（自动生成，勿手工编辑；迭代 69 / #93）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Package Name=`"$Name`" Manufacturer=`"LabelFrame`" Version=`"$testVersion`" UpgradeCode=`"$UpgradeCode`" Scope=`"perUser`" Codepage=`"65001`">"
        '    <StandardDirectory Id="ProgramFilesFolder">'
        '      <Directory Id="LabelFrameUninstallWalk" Name="LabelFrameUninstallWalk">'
        '        <Component Id="Marker" Guid="*">'
        "          <RegistryValue Root=`"HKCU`" Key=`"Software\LabelFrame\UninstallWalkthrough`" Name=`"$Role`" Value=`"$testVersion`" Type=`"string`" KeyPath=`"yes`" />"
        '        </Component>'
        '      </Directory>'
        '    </StandardDirectory>'
        '  </Package>'
        '</Wix>'
    )
    [System.IO.File]::WriteAllLines($wxs, $content, (New-Object System.Text.UTF8Encoding($false)))
    & $wix build $wxs -o $msi -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build failed（测试 MSI $Role）" }
    return $msi
}

# ---------- 构建测试载荷（webui zip / .lfplugin：真实 PayloadTool 落位引擎的真实输入）----------
function New-TestPayloads([string]$Dir) {
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

    $webUiZip = Join-Path $Dir 'labelframe-server-webui-test.zip'
    if (Test-Path $webUiZip) { Remove-Item $webUiZip -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($webUiZip, 'Create')
    try {
        foreach ($name in @('index.html', 'assets/app.js')) {
            $entry = $archive.CreateEntry($name)
            $stream = $entry.Open()
            try {
                $bytes = [System.Text.Encoding]::UTF8.GetBytes("uninstall-walk webui $name")
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $pluginPackage = Join-Path $Dir 'labelframe-transport-zebra-test.lfplugin'
    if (Test-Path $pluginPackage) { Remove-Item $pluginPackage -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($pluginPackage, 'Create')
    try {
        foreach ($item in @(
                @{ Name = 'manifest.json'; Body = '{"pluginId":"labelframe-transport-zebra","name":"Zebra","version":"' + $testVersion + '"}' },
                @{ Name = 'LabelFrame.TransportPlugin.Zebra.dll'; Body = 'plugin-bin' },
                @{ Name = 'SdkApi.Core.dll'; Body = 'sdk-bin-1' },
                @{ Name = 'SdkApi.dmm.dll'; Body = 'sdk-bin-2' } # 伴生依赖（复刻 29 DLL 形态的最小集）
            )) {
            $entry = $archive.CreateEntry($item.Name)
            $stream = $entry.Open()
            try {
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($item.Body)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    return [pscustomobject]@{ WebUiZip = $webUiZip; PluginPackage = $pluginPackage }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------- 测试清单（schema 严格：sha256 / sizeBytes 按实测载荷；urls 不消费——载荷全部内嵌压缩，零下载）----------
function New-TestManifest([string]$Dir, [string]$ServerMsi, [string]$ClientMsi, [string]$WebUiZip, [string]$PluginPackage) {
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    $entry = {
        param($id, $type, $path, $notes)
        $sha = Get-Sha256 $path
        $size = (Get-Item $path).Length
        "    {`n      `"id`": `"$id`", `"type`": `"$type`", `"version`": `"$testVersion`", `"dependsOn`": [],`n      `"urls`": [`"http://127.0.0.1:1/$id`"],`n      `"sha256`": `"$sha`", `"sizeBytes`": $size,`n      `"silentArgs`": `"`", `"topologies`": [`"standalone`"], `"notes`": `"$notes`"`n    }"
    }
    $manifestJson = "{`n  `"schemaVersion`": 1,`n  `"labelframeVersion`": `"$testVersion`",`n  `"generatedAt`": `"2026-09-17T00:00:00Z`",`n  `"components`": [`n$(& $entry 'server-msi' 'msi' $ServerMsi '卸载走查测试 · 服务端'),`n$(& $entry 'client-msi' 'msi' $ClientMsi '卸载走查测试 · 客户端'),`n$(& $entry 'webui' 'webui-zip' $WebUiZip '卸载走查测试 · 管理界面'),`n$(& $entry 'plugin-zebra' 'lfplugin' $PluginPackage '卸载走查测试 · Zebra 插件')`n  ]`n}`n"
    $path = Join-Path $Dir 'install-manifest.json'
    [System.IO.File]::WriteAllText($path, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
    return $path
}

# ---------- 构建测试 Bundle（双 MsiPackage + 双落位 ExePackage 内嵌压缩链 + 真实 BA / PayloadTool；per-user）----------
function New-TestBundle([string]$ServerMsi, [string]$ClientMsi, [string]$WebUiZip, [string]$PluginPackage, [string]$OutputPath) {
    $baDir = Join-Path (Split-Path $staging -Parent) 'ba' # 与既有走查脚本共用 BA 发布目录（artifacts\bootstrapper-tests\ba）
    $toolDir = Join-Path (Split-Path $staging -Parent) 'payloadtool'
    $webUiTarget = Join-Path $placementRoot 'server\plugins\web-ui'
    $pluginTarget = Join-Path $placementRoot 'Client\plugins\labelframe-transport-zebra'

    # PayloadTool 为 framework-dependent：伴生 DLL 以 PayloadGroup 内嵌（复刻生产落位包机制，§6.9）
    $payloadGroupLines = @('    <PayloadGroup Id="PayloadToolDependencies">')
    foreach ($dep in (Get-ChildItem $toolDir -File | Where-Object { $_.Name -ne 'LabelFrame.Bootstrapper.PayloadTool.exe' } | Sort-Object Name)) {
        $payloadGroupLines += "      <Payload SourceFile=`"`$(var.PayloadToolDir)$($dep.Name)`" Compressed=`"yes`" />"
    }
    $payloadGroupLines += '    </PayloadGroup>'

    $wxsLines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 卸载走查测试 Bundle（自动生成，勿手工编辑；迭代 69 / #93）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Bundle Name=`"$bundleName`" Version=`"$testVersion`" Manufacturer=`"LabelFrame`" UpgradeCode=`"$bundleUpgradeCode`">"
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
    ) + $payloadGroupLines + @(
        '    <Chain>'
        "      <MsiPackage Id=`"ServerMsi`" CacheId=`"$($cacheIds[0])`" SourceFile=`"$ServerMsi`" Compressed=`"yes`" InstallCondition=`"InstallServer`" />"
        "      <MsiPackage Id=`"ClientMsi`" CacheId=`"$($cacheIds[1])`" SourceFile=`"$ClientMsi`" Compressed=`"yes`" InstallCondition=`"InstallClient`" />"
        # 落位包复刻生产形态（§6.9 / 决策 #133）：Permanent 维持（安装路径零改动）+ -version 写落位凭据；载荷内嵌压缩（走查零下载）
        "      <ExePackage Id=`"WebUiPlacement`" CacheId=`"$($cacheIds[2])`" SourceFile=`"`$(var.PayloadToolDir)LabelFrame.Bootstrapper.PayloadTool.exe`""
        "                  Name=`"LabelFrame.PayloadTool.exe`" Compressed=`"yes`" PerMachine=`"no`" Permanent=`"yes`" InstallCondition=`"InstallWebUi`""
        "                  InstallArguments=`"-place -archive labelframe-server-webui-test.zip -version $testVersion -target &quot;$webUiTarget&quot;`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        "        <Payload SourceFile=`"$WebUiZip`" Name=`"labelframe-server-webui-test.zip`" Compressed=`"yes`" />"
        '      </ExePackage>'
        # 成对清理包复刻生产形态（决策 #133）：install = 无参空操作（登记已装），UninstallArguments -clean。
        # 测试口径差异：生产 DetectCondition = BA 启动期凭据探测变量（探测生产 ProgramData 路径，与测试落位根不同源）——
        # 走查以 Burn 内建 WixBundleInstalled 替代（已装 Bundle = 1）：装（未注册 = 0 → 安装登记）/ 卸载（已注册 = 1 → Present 清理）
        "      <ExePackage Id=`"WebUiPlacementCleanup`" CacheId=`"$($cacheIds[4])`" SourceFile=`"`$(var.PayloadToolDir)LabelFrame.Bootstrapper.PayloadTool.exe`""
        "                  Name=`"LabelFrame.PayloadTool.exe`" Compressed=`"yes`" PerMachine=`"no`" DetectCondition=`"WixBundleInstalled`""
        "                  InstallCondition=`"InstallWebUi`" UninstallArguments=`"-clean -target &quot;$webUiTarget&quot;`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        "      <ExePackage Id=`"ZebraPluginPlacement`" CacheId=`"$($cacheIds[3])`" SourceFile=`"`$(var.PayloadToolDir)LabelFrame.Bootstrapper.PayloadTool.exe`""
        "                  Name=`"LabelFrame.PayloadTool.exe`" Compressed=`"yes`" PerMachine=`"no`" Permanent=`"yes`" InstallCondition=`"InstallPluginZebra`""
        "                  InstallArguments=`"-plugin -archive labelframe-transport-zebra-test.lfplugin -version $testVersion -target &quot;$pluginTarget&quot;`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        "        <Payload SourceFile=`"$PluginPackage`" Name=`"labelframe-transport-zebra-test.lfplugin`" Compressed=`"yes`" />"
        '      </ExePackage>'
        "      <ExePackage Id=`"ZebraPluginPlacementCleanup`" CacheId=`"$($cacheIds[5])`" SourceFile=`"`$(var.PayloadToolDir)LabelFrame.Bootstrapper.PayloadTool.exe`""
        "                  Name=`"LabelFrame.PayloadTool.exe`" Compressed=`"yes`" PerMachine=`"no`" DetectCondition=`"WixBundleInstalled`""
        "                  InstallCondition=`"InstallPluginZebra`" UninstallArguments=`"-clean -target &quot;$pluginTarget&quot;`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        '    </Chain>'
        '  </Bundle>'
        '</Wix>'
    )
    $wxs = Join-Path $staging 'Bundle-UninstallWalk.wxs'
    [System.IO.File]::WriteAllLines($wxs, $wxsLines, (New-Object System.Text.UTF8Encoding($false)))
    & $wix build $wxs -d "BaDir=$baDir\" -d "PayloadToolDir=$toolDir\" -o $OutputPath -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed（测试 Bundle）' }
    return $OutputPath
}

# ---------- 驱动向导安装（复刻既有走查脚本；品牌 / 管理界面复选框按需勾选 / 取消）----------
function Invoke-WizardInstall([string]$BundleExe, [string]$ManifestPath, [string]$LogPath, [int]$TimeoutSeconds, [bool]$SelectZebra, [bool]$SelectWebUi) {
    $proc = Start-Process -FilePath $BundleExe -ArgumentList @('-l', $LogPath) -PassThru
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
        [void][UninstallWalk.Native]::SetForegroundWindow($mainHwnd)

        # 欢迎页：清单来源输入本地测试清单 → 加载（成功自动进下一页）
        $sourceEdit = Find-ChildByCaption $mainHwnd 'EDIT' '*'
        if (-not $sourceEdit) { throw '未找到清单来源输入框' }
        Set-EditText $sourceEdit.Handle $ManifestPath
        $loadButton = Find-ChildByCaption $mainHwnd 'BUTTON' '加载清单'
        if (-not $loadButton) { throw '未找到「加载清单」按钮' }
        Click-Button $loadButton.Handle

        # 拓扑页：选「单机一体」（四包条件变量全部就位：双 MSI + webui 开关 + 品牌开关）
        $standalone = Wait-ChildByCaption $mainHwnd 'BUTTON' '单机一体' 30
        if (-not $standalone) { throw '30 秒内未进入拓扑页（清单加载失败？）' }
        Start-Sleep -Milliseconds 400
        Click-Button $standalone.Handle
        Start-Sleep -Milliseconds 300

        # 下一步 → 品牌页：Zebra 勾选态按场景驱动（ZDesigner 驱动名预选可能已勾选，须读态后按需点击）
        $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
        if (-not $nextButton) { throw '未找到拓扑页「下一步」按钮' }
        Click-Button $nextButton.Handle
        $zebraCheck = Wait-ChildByCaption $mainHwnd 'BUTTON' 'Zebra*' 10
        if (-not $zebraCheck) { throw '未找到品牌页 Zebra 复选框' }
        Set-CheckBoxByToggle $zebraCheck.Handle (Get-ZebraPreselected) $SelectZebra

        # 下一步 → 管理界面页：开关按场景驱动
        $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
        if (-not $nextButton) { throw '未找到品牌页「下一步」按钮' }
        Click-Button $nextButton.Handle
        $webUiCheck = Wait-ChildByCaption $mainHwnd 'BUTTON' '安装管理界面*' 10
        if (-not $webUiCheck) { throw '未找到管理界面页开关' }
        Set-CheckBoxByToggle $webUiCheck.Handle $false $SelectWebUi # standalone 预设初始不勾（确定性）

        # 下一步 → 确认页 → 下一步 = 开始安装；等终态（完成页 / 失败报告）
        for ($click = 0; $click -lt 2; $click++) {
            $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
            if (-not $nextButton) { throw "未找到「下一步」按钮（第 $($click + 1) 次）" }
            Click-Button $nextButton.Handle
            Start-Sleep -Milliseconds 500
        }

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
            [void][UninstallWalk.Native]::SendMessage($mainHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
            if (-not $proc.WaitForExit(60000)) { throw '向导关闭超时' }
        }

        return [pscustomobject]@{ Terminal = $terminal; ExitCode = $proc.ExitCode }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        $proc.WaitForExit(10000) | Out-Null
        Get-Process -Name $baProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

# ---------- ARP 形态卸载（bundle.exe /uninstall：BA 非交互路径——复刻「程序与功能」卸载）----------
function Invoke-BundleUninstall([string]$BundleExe, [string]$LogPath, [int]$TimeoutSeconds) {
    $proc = Start-Process -FilePath $BundleExe -ArgumentList @('/uninstall', '-l', $LogPath) -PassThru
    if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Get-Process -Name $baProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        throw "卸载超时（${TimeoutSeconds}s）"
    }
    return $proc.ExitCode
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
$bundleExe = Join-Path $staging 'LabelFrame-UninstallWalk.exe'

if (-not $SkipBuild) {
    foreach ($dir in @($staging, (Join-Path $staging 'manifest'), (Join-Path (Split-Path $staging -Parent) 'ba'), (Join-Path (Split-Path $staging -Parent) 'payloadtool'))) {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    Write-Host '发布 BA（net48 win-x64）与 PayloadTool（真实落位 / 清理引擎）…'
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path (Split-Path $staging -Parent) 'ba') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.PayloadTool\LabelFrame.Bootstrapper.PayloadTool.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path (Split-Path $staging -Parent) 'payloadtool') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'PayloadTool publish failed' }

    & $wix eula accept wix7 2>$null | Out-Null
    $global:LASTEXITCODE = 0

    Write-Host '构建测试 MSI 与测试载荷（webui zip / .lfplugin）…'
    $serverMsi = New-TestMsi 'Server' $serverUpgradeCode 'LabelFrame 卸载走查·服务端'
    $clientMsi = New-TestMsi 'Client' $clientUpgradeCode 'LabelFrame 卸载走查·客户端'
    $payloads = New-TestPayloads $staging

    Write-Host "构建测试 Bundle（UpgradeCode $bundleUpgradeCode；落位目标根 $placementRoot）…"
    New-TestBundle $serverMsi $clientMsi $payloads.WebUiZip $payloads.PluginPackage $bundleExe | Out-Null
    $script:manifestPath = New-TestManifest (Join-Path $staging 'manifest') $serverMsi $clientMsi $payloads.WebUiZip $payloads.PluginPackage
}
else {
    if (-not (Test-Path $bundleExe)) { throw "-SkipBuild 但未找到 $bundleExe，请先完整运行一次。" }
    $script:manifestPath = Join-Path $staging 'manifest\install-manifest.json'
    if (-not (Test-Path $script:manifestPath)) { throw "-SkipBuild 但未找到测试清单 $script:manifestPath，请先完整运行一次。" }
}

$webUiDir = Join-Path $placementRoot 'server\plugins\web-ui'
$zebraDir = Join-Path $placementRoot 'Client\plugins\labelframe-transport-zebra'
$clientPluginsDir = Join-Path $placementRoot 'Client\plugins'
$thirdPartyDll = Join-Path $clientPluginsDir 'ThirdParty.Transport.dll'
$thirdPartyDir = Join-Path $clientPluginsDir 'my-manual-plugin'

# ---------- 走查 ----------
Clear-TestState
$selected = $Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }

try {
    foreach ($scenario in $selected) {
        Write-Host "`n========== 场景 $scenario ==========" -ForegroundColor Cyan
        $logPath = Join-Path $staging "$scenario.log"
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        Get-ChildItem $staging -Filter "$scenario`_*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue # 陈旧包级子日志

        switch ($scenario) {
            'S1' {
                # 装（选 Zebra + 管理界面）：四包全链——MSI × 2 + webui 落位 + zebra 插件落位
                $run = Invoke-WizardInstall $bundleExe $script:manifestPath $logPath 180 $true $true
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Add-Result $scenario (Test-Path (Join-Path $webUiDir 'index.html')) "webui 已落位（$webUiDir）"
                Add-Result $scenario (Test-Path (Join-Path $zebraDir 'manifest.json')) "zebra 插件已落位（$zebraDir）"
                Add-Result $scenario (Test-Path (Join-Path $zebraDir 'SdkApi.Core.dll')) "zebra 伴生依赖随包落位（SDK 依赖形态）"
                $markerPath = Join-Path $zebraDir '.labelframe-bundle-placement'
                $markerHit = (Test-Path $markerPath) -and ((Get-Content -LiteralPath $markerPath -Raw).Trim() -eq $testVersion)
                Add-Result $scenario $markerHit "落位凭据已写入（.labelframe-bundle-placement = $testVersion，决策 #133）"
                Assert-LogContains $logPath '-plugin -archive' $scenario '插件落位命令（PayloadTool -plugin）'
                $serverVersion = Get-InstalledFamilyVersion $serverUpgradeCode
                Add-Result $scenario ($serverVersion -eq $testVersion) "测试服务端 MSI 已装（族内最高 $serverVersion）"

                # 用户手动放置的第三方插件资产（AC-03 素材）：平铺 DLL + 手动子目录
                New-Item -ItemType Directory -Force -Path $thirdPartyDir | Out-Null
                Set-Content -LiteralPath $thirdPartyDll -Value 'user-manual-third-party-dll' -Encoding UTF8
                Set-Content -LiteralPath (Join-Path $thirdPartyDir 'keep.dll') -Value 'user-manual-plugin-dir' -Encoding UTF8
                Add-Result $scenario ((Test-Path $thirdPartyDll) -and (Test-Path (Join-Path $thirdPartyDir 'keep.dll'))) '第三方插件资产已预置（平铺 DLL + 手动子目录）'
            }
            'S2' {
                # ARP 卸载：Bundle 落位目录清理干净 + 第三方资产保留 + MSI 卸载正常
                $exit = Invoke-BundleUninstall $bundleExe $logPath 240
                Add-Result $scenario ($exit -eq 0) "卸载退出码 $exit（期望 0）"
                Assert-LogContains $logPath '非交互启动' $scenario 'BA 非交互卸载路径'
                Assert-LogContains $logPath '-clean -target' $scenario '落位包卸载清理命令（PayloadTool -clean）'
                Add-Result $scenario (-not (Test-Path $webUiDir)) "web-ui 落位目录已清理（AC-01）"
                Add-Result $scenario (-not (Test-Path $zebraDir)) "zebra 插件落位目录已清理（AC-01）"
                Add-Result $scenario (Test-Path $thirdPartyDll) "平铺第三方 DLL 保留（AC-03 不误伤）"
                Add-Result $scenario (Test-Path (Join-Path $thirdPartyDir 'keep.dll')) "第三方插件子目录保留（AC-03 不误伤）"
                Add-Result $scenario (Test-Path $clientPluginsDir) "plugins 父目录非空（含用户资产）保留（清理边界）"
                Add-Result $scenario (-not (Test-Path (Join-Path $placementRoot 'server\plugins'))) "server plugins 父目录已空则移除（与装前等价）"
                $serverVersion = Get-InstalledFamilyVersion $serverUpgradeCode
                $clientVersion = Get-InstalledFamilyVersion $clientUpgradeCode
                Add-Result $scenario ($null -eq $serverVersion -and $null -eq $clientVersion) "MSI 组件已卸载（服务端 $serverVersion / 客户端 $clientVersion，期望均为空）"
            }
            'S3' {
                # 卸载 → 重装不选 Zebra（选管理界面）：组件集合与问卷一致——zebra 目录不存在、webui 正常落位
                $run = Invoke-WizardInstall $bundleExe $script:manifestPath $logPath 240 $false $true
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Add-Result $scenario (-not (Test-Path $zebraDir)) "重装不选 Zebra 后插件目录不存在（AC-02 残留缺陷消除对照）"
                Add-Result $scenario (Test-Path (Join-Path $webUiDir 'index.html')) "重装所选 webui 正常落位（AC-02 组件集合口径）"
                $pluginExecution = Select-String -LiteralPath $logPath -Pattern '-plugin -archive' | Select-Object -First 1
                Add-Result $scenario (-not $pluginExecution) '重装链未执行插件落位命令（品牌包条件假即不执行）'
                Assert-LogContains $logPath '-place -archive' $scenario '重装链 webui 落位命令（PayloadTool -place）'
                $serverVersion = Get-InstalledFamilyVersion $serverUpgradeCode
                Add-Result $scenario ($serverVersion -eq $testVersion) "重装后 MSI 就位（族内最高 $serverVersion）"
            }
            default {
                Add-Result $scenario $false '未知场景标识'
            }
        }
    }
}
finally {
    # 收尾：Bundle 注册 + 测试 MSI 族 + 包缓存清理（证据日志与落位目录保留供走查复核；-SkipBuild 重跑自会重置）
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (Test-Path $uninstallRoot) {
        foreach ($key in (Get-ChildItem $uninstallRoot -ErrorAction SilentlyContinue)) {
            $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
            if ($displayName -eq $bundleName) {
                Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  收尾清理测试 Bundle 注册：$($key.PSChildName)"
            }
        }
    }
    Remove-TestFamilyProducts
}

# ---------- 汇总 ----------
$summary = @(
    "LabelFrame 引导卸载清理走查 · $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Bundle：$bundleExe（UpgradeCode $bundleUpgradeCode）；落位目标根：$placementRoot；WiX $wixVersion",
    "链 = 双测试 MSI（随机固定 UpgradeCode，不触生产族）+ 双落位包（真实 PayloadTool，-place/-plugin 落位 + -clean 卸载清理，决策 #133）",
    ''
) + $script:results + @(
    '',
    "结论：$($script:results.Count) 项断言，失败 $($script:failed) 项。"
)
$summary | Set-Content -LiteralPath (Join-Path $staging 'summary.txt') -Encoding UTF8
Write-Host "`n========== 走查汇总（$($script:results.Count) 项断言，失败 $($script:failed)）==========" -ForegroundColor Cyan
$script:results | ForEach-Object { Write-Host "  $_" }
Write-Host "证据目录：$staging"
if ($script:failed -gt 0) { exit 1 }
exit 0
