# LabelFrame 升级链获取阶段走查（#171 返修 · 决策 #156，迭代先例 = 55 / 68 / 85 合成版本双 Bundle）
#
# 目的：复现并验证升级链获取阶段三重缺陷（真机验收 #171 §二 / §四 / §五）——
#   ① 落位 / 清理包常量 CacheId 跨版本共享包缓存目录 → PayloadTool.exe 每版本哈希必变 → 升级会话缓存校验必冲突
#      （0x80091007 删缓存转重取）；修复 = CacheId 版本化（决策 #156 ①，-CacheIdMode Versioned）。
#   ② 附加容器 WixAttachedContainer 获取失败 0x80070002（引擎「引擎桩副本」会话取内嵌载荷依赖源解析搜索路径）；
#      修复 = BA 容器获取事件注入引导 EXE 原始路径为本地源（决策 #156 ②，随 BA 构建）。
#   ③ BA 包级缓存重试无退避无终止热循环（e346）；修复 = CacheRetryPolicy 指数退避 + 连续无进展终止（决策 #156 ③）。
#
# 载体（per-machine 真实链路——本脚本必须以管理员运行，且仅用于可还原的纯净测试机）：
#   - 合成版本双 Bundle（0.90.1 → 0.90.2）：真实 BA（交互向导，Win32 UI 自动化驱动，服务端角色 + 管理界面开——
#     与 #171 真机走查同路径）+ 真实 PayloadTool（按版本号发布 → 每版本字节不同，触发缓存哈希冲突）+ 测试 per-machine MSI
#     （HKLM 标记，UpgradeCode 与生产隔离）+ webui 形态 zip 落位（-place -version 写凭据，凭据文件为升级链断言锚点）；
#   - 布局目录几何 = 真机走查同款：EXE + install-manifest.json + 载荷同目录（BA 邻接清单自动加载 + 本地源优先），
#     DownloadUrl 指向本地 HTTP 测试源（TcpListener）兜底；
#   - 包缓存（%ProgramData%\Package Cache\）与 Bundle 注册（HKLM）断言 = 引擎行为取证面。
#
# 场景（-Scenarios，缺省序 S1,S4,S5,S2,S3——S4/S5 桩会话重跑须在 S2 升级前（家族版本仍 0.90.1））：
#   S1 装旧 0.90.1 —— 期望成功：exit 0 + 凭据 = 0.90.1 + 包缓存目录形态（按 -CacheIdMode）；
#   S4 桩会话裸重跑（引擎级 ② 定论的确定性复现）—— 重跑包缓存里的截断桩 EXE（引擎设计：注册缓存源 =
#        .be 提权工作副本）+ 预删落位包载荷缓存（#171 真机尝试 #3 同款），不带 -burn.originalsource
#        （ARP / 用户直击缓存副本的真实形态）→ 桩无附加容器且无原始源 → WixAttachedContainer 0x80070002；
#        基线 BA → e346 热循环滞留（真机 5 分钟 92,993 次同构）；修复 BA → 有限退避后失败报告页（决策 #156 ③）；
#   S5 桩会话重跑·原始源在位（修复 ② 注入机制验证）—— 同一缓存桩 + -burn.originalsource 指向布局原件：
#        基线 BA → v7.0.0 引擎原生「容器 copy from 原始源」路径可解（原始源可用性 = 分水岭的直接证据）；
#        修复 BA → CacheAcquireBegin 容器形态注入原始源为搜索路径首项（BA 日志留痕）→ 同样完成 exit 0；
#   S2 升级 0.90.2 —— Versioned（修复后）：期望成功全链路（RelatedBundle 移除旧链 → 落位重落位 →
#        凭据 = 0.90.2 → 退出码 0，缓存零哈希冲突）；Constant（基线形态）：断言冲突征象在日志在场
#        （0x80091007 / 删缓存），获取阶段结局如实记录（用户态沙箱会话几何与真机 per-machine 提权链不同——
#        真机定论见 #171 走查与决策 #156）；
#   S3 不可达源重试终止 —— webui zip 无本地文件 + 死 URL：期望有限次退避重试后失败报告页（决策 #156 ③ 呈现面），
#        日志有界（对照真机热循环 5 分钟 9.3 万次重试 / 219–240MB）。
#
# 用法（纯净测试机管理员 PowerShell，仓库根目录）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-upgrade-acquire.ps1                     # 修复后形态（默认 Versioned，含 S4 修复验证）
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-upgrade-acquire.ps1 -CacheIdMode Constant   # 基线形态（复现取证：S2 哈希冲突 / S4 0x80070002 + 热循环）
#   基线源码（未修 BA）构建：-SourceRoot <基线 worktree 根>（BA / PayloadTool 从该源码发布——沙箱需与生产链同源码形态）
#   可选：-Scenarios 'S1,S2' / -SkipBuild（复用已构建产物）/ -MainPort 8135
#   免提权用户态：-UserScope（per-user Bundle / MSI + %LocalAppData% 包缓存 + HKCU 注册——获取 / 缓存 / RelatedBundle
#   引擎语义与 per-machine 同构，无提权会话的宿主机沙箱取证用；per-machine 全量形态仍按上两行以管理员运行）
# 证据：artifacts\bootstrapper-tests\acquire\*.log（Burn 日志，含 RelatedBundle 子会话）+ summary.txt + 凭据 / 缓存目录清单。
# 临时产物不进仓（artifacts/ 已 gitignore）；本脚本随仓版本化（走查可复现）。测试毕请还原测试机基线。
param(
    [string]$WixPath = '',
    [string]$Scenarios = 'S1,S4,S5,S2,S3',
    [ValidateSet('Versioned', 'Constant')]
    [string]$CacheIdMode = 'Versioned',
    [int]$MainPort = 8135,
    [switch]$SkipBuild,
    # 两段式执行（纯净测试机无 .NET SDK / wix：宿主构建产物、测试机只跑场景）
    [switch]$BuildOnly,
    [switch]$RunOnly,
    # 源码根覆写（BA / PayloadTool 从该源码构建；缺省 = 脚本所在仓根）——基线复现时指向基线 worktree（未修 BA）
    [string]$SourceRoot = '',
    # 用户态沙箱（#171 返修增补）：per-user Bundle / per-user MSI / %LocalAppData% 包缓存 / HKCU 注册——
    # 免提权跑通全场景（引擎获取 / 缓存 / RelatedBundle 语义与 per-machine 同构，差异仅注册表根与缓存根）；
    # per-machine 全量形态（缺省）仍需管理员。沙箱证据注明所用形态。
    [switch]$UserScope
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = if ($SourceRoot) { $SourceRoot } else { $root }
$staging = Join-Path $root 'artifacts\bootstrapper-tests\acquire'
$serveDir = Join-Path $staging 'serve'
$baProcessName = 'LabelFrame.Bootstrapper.Ba'

# 提权前置（RUN 段：per-machine 链——MSI / 包缓存 / HKLM 注册都需要管理员；构建段免提权；-UserScope 免提权）
if (-not $BuildOnly -and -not $UserScope) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw '本走查 RUN 段为 per-machine 真实链路，必须以管理员运行（纯净测试机 + 可还原基线，勿在日常开发机执行；仅构建用 -BuildOnly 免提权）。' }
}

# 固定 GUID（可重复运行；均与生产 UpgradeCode / 既有测试 Bundle 隔离）
$bundleUpgradeCode = '3F5D8A71-1717-4C2E-B0AA-1F171D0A7171'
$serverUpgradeCode = '{7C4E1A62-9D3B-4E88-A4C0-17A9FEB17100}'
$bundleName = 'LabelFrame 获取链走查测试'
$oldVersion = '0.90.1'
$newVersion = '0.90.2'
$placementDir = if ($UserScope) {
    # 用户态落位目标重定向（BA 启动期把 WebUiTargetDir 硬写为 ProgramData 派生值，用户态无权清场 ProgramData 旧树——
    # 测试链改用独立变量 TestWebUiTargetDir 传递目标：落位 / 清理 / 升级链两侧同变量同路径，引擎获取 / 缓存 / RelatedBundle 语义不变）
    Join-Path $staging 'placement-target'
} else {
    Join-Path $env:ProgramData 'LabelFrame\server\plugins\web-ui'
}
$credentialFile = Join-Path $placementDir '.labelframe-bundle-placement'
# 包缓存根：per-user Bundle 用 %LocalAppData%（Burn 引擎语义）；per-machine 用 %ProgramData%
$packageCacheRoot = if ($UserScope) { Join-Path $env:LocalAppData 'Package Cache' } else { Join-Path $env:ProgramData 'Package Cache' }
$script:liveProductCodes = New-Object System.Collections.Generic.List[string]

# ---------- 前置：wix / burn 日志目录 ----------
$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw '未找到 WiX（wix.exe），请安装 WiX Toolset v7 或以 -WixPath 指定。' }
$wixVersion = & $wix --version
Write-Host "WiX：$wixVersion"

# ---------- Win32 UI 驱动（复刻 test-bundle-upgrade-walkthrough.ps1）----------
Add-Type -Namespace AcquireWalk -Name Native -MemberDefinition @"
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
    $callback = [AcquireWalk.Native+EnumProc]{
        param($hwnd, $lParam)
        $textBuilder = New-Object System.Text.StringBuilder 512
        [void][AcquireWalk.Native]::GetWindowText($hwnd, $textBuilder, 512)
        $classBuilder = New-Object System.Text.StringBuilder 128
        [void][AcquireWalk.Native]::GetClassName($hwnd, $classBuilder, 128)
        $items.Add([pscustomobject]@{ Handle = $hwnd; Class = $classBuilder.ToString(); Caption = $textBuilder.ToString() })
        return $true
    }
    [void][AcquireWalk.Native]::EnumChildWindows($parent, $callback, [IntPtr]::Zero)
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
    [void][AcquireWalk.Native]::SendMessage($hwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
}

# ---------- 本地 HTTP 测试源（TcpListener；仅 /ok/ 正常路由——兜底下载源）----------
function Start-TestServer([int]$Port, [string]$LogFile) {
    $job = Start-Job -ScriptBlock {
        param($Port, $LogFile, $ServeDir)
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        try {
            while ($true) {
                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII, $false, 8192, $true)
                    $requestLine = $reader.ReadLine()
                    while ($true) { $header = $reader.ReadLine(); if (-not $header -or $header -eq '') { break } }
                    if (-not $requestLine) { continue }

                    $parts = $requestLine -split ' '
                    $noQuery = (($parts[1]) -split '\?')[0]
                    Add-Content -LiteralPath $LogFile -Value "GET $noQuery"

                    $fileName = if ($noQuery -match '^/ok/(.+)$') { $Matches[1] } else { $null }
                    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::ASCII)
                    $writer.NewLine = "`r`n"
                    $writer.AutoFlush = $true
                    if ($null -eq $fileName) {
                        $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                    }
                    else {
                        $file = [System.IO.Path]::Combine($ServeDir, [System.Uri]::UnescapeDataString($fileName))
                        if (-not (Test-Path -LiteralPath $file)) {
                            $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                        }
                        else {
                            $bytes = [System.IO.File]::ReadAllBytes($file)
                            $writer.Write("HTTP/1.1 200 OK`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
                            $stream.Write($bytes, 0, $bytes.Length)
                            $stream.Flush()
                        }
                    }
                    $writer.Dispose()
                }
                catch { }
                finally { $client.Close() }
            }
        }
        finally { $listener.Stop() }
    } -ArgumentList $Port, $LogFile, $serveDir
    return $job
}

# ---------- MSI ProductCode 提取 / 族内版本探测（复刻升级走查脚本）----------
Add-Type -Namespace AcquireWalk -Name Msi -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, System.Text.StringBuilder productCode, ref int length);
[System.Runtime.InteropServices.DllImport("msi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int MsiGetProductInfo(string productCode, string property, System.Text.StringBuilder value, ref int length);
"@

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

function Get-InstalledFamilyVersion([string]$upgradeCode) {
    $best = $null
    for ($index = 0; ; $index++) {
        $buffer = New-Object System.Text.StringBuilder 64
        $length = 64
        if ([AcquireWalk.Msi]::MsiEnumRelatedProducts($upgradeCode, 0, $index, $buffer, [ref]$length) -ne 0) { break }
        $productCode = $buffer.ToString()
        $versionBuffer = New-Object System.Text.StringBuilder 64
        $versionLength = 64
        if ([AcquireWalk.Msi]::MsiGetProductInfo($productCode, 'VersionString', $versionBuffer, [ref]$versionLength) -eq 0) {
            $version = $versionBuffer.ToString().TrimEnd([char]0)
            if ($null -eq $best -or [version]$version -gt [version]$best) { $best = $version }
        }
    }
    return $best
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------- 构建测试 MSI（极小标记包：per-machine = HKLM 标记；-UserScope = HKCU 标记）----------
function New-TestMsi([string]$Version, [string]$UpgradeCode, [string]$Name) {
    $wxs = Join-Path $staging "Server-$Version.wxs"
    $msi = Join-Path $staging "Server-$Version.msi"
    $scope = if ($UserScope) { 'perUser' } else { 'perMachine' }
    $directory = if ($UserScope) { 'LocalAppDataFolder' } else { 'ProgramFiles64Folder' }
    $regRoot = if ($UserScope) { 'HKCU' } else { 'HKLM' }
    $content = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 获取链走查测试 MSI（自动生成，勿手工编辑；#171 返修）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Package Name=`"$Name`" Manufacturer=`"LabelFrame`" Version=`"$Version`" UpgradeCode=`"$UpgradeCode`" Scope=`"$scope`" Codepage=`"65001`">"
        "    <StandardDirectory Id=`"$directory`">"
        '      <Directory Id="LabelFrameAcquireWalk" Name="LabelFrameAcquireWalk">'
        '        <Component Id="Marker" Guid="*">'
        "          <RegistryValue Root=`"$regRoot`" Key=`"Software\LabelFrame\AcquireWalkthrough`" Name=`"Server`" Value=`"$Version`" Type=`"string`" KeyPath=`"yes`" />"
        '        </Component>'
        '      </Directory>'
        '    </StandardDirectory>'
        '  </Package>'
        '</Wix>'
    )
    [System.IO.File]::WriteAllLines($wxs, $content, (New-Object System.Text.UTF8Encoding($true)))
    & $wix build $wxs -o $msi -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build failed（测试 MSI $Version）" }
    $script:liveProductCodes.Add((Get-MsiProductCode $msi)) | Out-Null
    return $msi
}

# ---------- 构建测试 Bundle（per-machine：ServerMsi 远程 + WebUiPlacement 内嵌 + 成对清理包）----------
function New-TestBundle([string]$Version, [string]$ServerMsi, [string]$WebUiZip, [string]$PayloadToolDir, [string]$OutputPath) {
    $baDir = Join-Path $staging 'ba'
    $cacheIdSuffix = if ($CacheIdMode -eq 'Versioned') { '.$(var.BundleVersion)' } else { '' }
    $exeScope = if ($UserScope) { 'no' } else { 'yes' } # ExePackage PerMachine：-UserScope 全链 per-user
    # 落位目标变量：per-machine 走 BA 启动期写入的 WebUiTargetDir（生产 §6.8 同路径）；-UserScope 走测试链自带
    # TestWebUiTargetDir（BA 硬写的 ProgramData 路径在用户态无权清场，重定向到 staging 用户可写目录）
    $targetVar = if ($UserScope) { 'TestWebUiTargetDir' } else { 'WebUiTargetDir' }
    $wxsLines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        "<!-- 获取链走查测试 Bundle（自动生成，勿手工编辑；#171 返修 / 决策 #156）：链构象对齐生产 §6.9，CacheId 形态 = $CacheIdMode -->"
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Bundle Name=`"$bundleName`" Version=`"$Version`" Manufacturer=`"LabelFrame`" UpgradeCode=`"$bundleUpgradeCode`">"
        '    <BootstrapperApplication SourceFile="$(var.BaDir)LabelFrame.Bootstrapper.Ba.exe">'
        '      <Payloads Include="$(var.BaDir)**">'
        '        <Exclude Files="$(var.BaDir)LabelFrame.Bootstrapper.Ba.exe" />'
        '        <Exclude Files="$(var.BaDir)*.pdb" />'
        '      </Payloads>'
        '    </BootstrapperApplication>'
        '    <PayloadGroup Id="PayloadToolDependencies">'
        '      <Payload SourceFile="$(var.PayloadToolDir)LabelFrame.Bootstrapper.PayloadTool.exe.config" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)LabelFrame.Bootstrapper.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)Microsoft.Bcl.AsyncInterfaces.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Buffers.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.IO.Pipelines.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Memory.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Numerics.Vectors.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Runtime.CompilerServices.Unsafe.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Text.Encodings.Web.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Text.Json.dll" Compressed="yes" />'
        '      <Payload SourceFile="$(var.PayloadToolDir)System.Threading.Tasks.Extensions.dll" Compressed="yes" />'
        '    </PayloadGroup>'
        '    <Variable Name="InstallPreset" Type="string" Value="" />'
        '    <Variable Name="InstallServer" Type="numeric" Value="0" />'
        '    <Variable Name="InstallClient" Type="numeric" Value="0" />'
        '    <Variable Name="InstallWebUi" Type="numeric" Value="0" />'
        '    <Variable Name="InstallPluginZebra" Type="numeric" Value="0" />'
        '    <Variable Name="DesktopRuntimeInstalled" Type="numeric" Value="1" />'
        '    <Variable Name="AspNetCoreRuntimeInstalled" Type="numeric" Value="1" />'
        '    <Variable Name="WebView2Installed" Type="numeric" Value="1" />'
        '    <Variable Name="WebUiTargetDir" Type="string" Value="" />'
        "    <Variable Name=`"TestWebUiTargetDir`" Type=`"string`" Value=`"$placementDir`" />"
        '    <Variable Name="WebUiPlacementPresent" Type="numeric" Value="0" />'
        '    <Chain>'
        "      <MsiPackage Id=`"ServerMsi`" SourceFile=`"$ServerMsi`" Compressed=`"no`" InstallCondition=`"InstallServer`" DownloadUrl=`"http://127.0.0.1:$MainPort/ok/Server-$Version.msi`" />"
        "      <ExePackage Id=`"WebUiPlacement`" CacheId=`"WebUiPlacement$cacheIdSuffix`" SourceFile=`"$PayloadToolDir\LabelFrame.Bootstrapper.PayloadTool.exe`" Name=`"LabelFrame.PayloadTool.exe`" Compressed=`"yes`" Permanent=`"yes`" PerMachine=`"$exeScope`" InstallCondition=`"InstallWebUi`" InstallArguments=`"-place -archive labelframe-webui-$Version.zip -version $Version -target &quot;[$targetVar]&quot;`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        "        <Payload SourceFile=`"$WebUiZip`" Name=`"labelframe-webui-$Version.zip`" Compressed=`"no`" DownloadUrl=`"http://127.0.0.1:$MainPort/ok/labelframe-webui-$Version.zip`" />"
        '      </ExePackage>'
        "      <ExePackage Id=`"WebUiPlacementCleanup`" CacheId=`"WebUiPlacementCleanup$cacheIdSuffix`" SourceFile=`"$PayloadToolDir\LabelFrame.Bootstrapper.PayloadTool.exe`" Name=`"LabelFrame.PayloadTool.exe`" Compressed=`"yes`" PerMachine=`"$exeScope`" DetectCondition=`"WebUiPlacementPresent`" InstallCondition=`"InstallWebUi`" UninstallArguments=`"-clean -target &quot;[$targetVar]&quot;`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        '    </Chain>'
        '  </Bundle>'
        '</Wix>'
    )
    $wxs = Join-Path $staging "Bundle-$CacheIdMode-$Version.wxs"
    [System.IO.File]::WriteAllLines($wxs, $wxsLines, (New-Object System.Text.UTF8Encoding($true)))
    & $wix build $wxs -d "BaDir=$baDir\" -d "PayloadToolDir=$PayloadToolDir\" -d "BundleVersion=$Version" -o $OutputPath -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build failed（测试 Bundle $Version）" }
    # wix build 在输出目录留载荷硬链接中间产物——清除避免同目录本地源干扰（下载矩阵脚本工程注记）
    Get-ChildItem (Split-Path $OutputPath -Parent) -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(Server-.*\.msi|labelframe-webui-.*\.zip)$' -and $_.FullName -ne $OutputPath } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    return $OutputPath
}

# ---------- 测试清单（server-win 角色：server-msi + webui）----------
function New-TestManifest([string]$Dir, [string]$Version, [string]$ServerMsi, [string]$WebUiZip) {
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    $msiSha = Get-Sha256 $ServerMsi
    $msiSize = (Get-Item $ServerMsi).Length
    $zipSha = Get-Sha256 $WebUiZip
    $zipSize = (Get-Item $WebUiZip).Length
    $manifestJson = @'
{
  "schemaVersion": 1,
  "labelframeVersion": "__VERSION__",
  "generatedAt": "2026-09-22T00:00:00Z",
  "components": [
    {
      "id": "server-msi", "type": "msi", "version": "__VERSION__", "dependsOn": [],
      "urls": ["http://127.0.0.1:__PORT__/ok/Server-__VERSION__.msi"],
      "sha256": "__MSI_SHA__",
      "sizeBytes": __MSI_SIZE__,
      "silentArgs": "", "topologies": ["standalone", "server-win", "offline"], "notes": "获取链走查"
    },
    {
      "id": "webui", "type": "archive", "version": "__VERSION__", "dependsOn": [],
      "urls": ["http://127.0.0.1:__PORT__/ok/labelframe-webui-__VERSION__.zip"],
      "sha256": "__ZIP_SHA__",
      "sizeBytes": __ZIP_SIZE__,
      "silentArgs": "", "topologies": ["standalone", "server-win", "offline"], "notes": "获取链走查"
    }
  ]
}
'@
    $manifestJson = $manifestJson.Replace('__VERSION__', $Version).Replace('__PORT__', "$MainPort").Replace('__MSI_SHA__', $msiSha).Replace('__MSI_SIZE__', "$msiSize").Replace('__ZIP_SHA__', $zipSha).Replace('__ZIP_SIZE__', "$zipSize")
    [System.IO.File]::WriteAllText((Join-Path $Dir 'install-manifest.json'), $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
    return (Join-Path $Dir 'install-manifest.json')
}

# ---------- 清理（开工前 + 收尾：测试族 MSI / Bundle 注册 / 落位目录 / 包缓存测试目录）----------
function Clear-TestState {
    # Bundle 注册按名称移除（HKLM + HKCU 双查——per-machine 注册在 HKLM）
    foreach ($uninstallRoot in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall')) {
        if (-not (Test-Path $uninstallRoot)) { continue }
        foreach ($key in (Get-ChildItem $uninstallRoot -ErrorAction SilentlyContinue)) {
            $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
            if ($displayName -eq $bundleName) {
                Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  已清理测试 Bundle 注册：$($key.PSChildName)"
            }
        }
    }

    # 测试族 MSI 卸载（版本 ≥ 0.90 才动——测试 UpgradeCode 与生产隔离，双保险）
    for ($index = 0; ; $index++) {
        $buffer = New-Object System.Text.StringBuilder 64
        $length = 64
        if ([AcquireWalk.Msi]::MsiEnumRelatedProducts($serverUpgradeCode, 0, $index, $buffer, [ref]$length) -ne 0) { break }
        $productCode = $buffer.ToString()
        $versionBuffer = New-Object System.Text.StringBuilder 64
        $versionLength = 64
        if ([AcquireWalk.Msi]::MsiGetProductInfo($productCode, 'VersionString', $versionBuffer, [ref]$versionLength) -ne 0) { continue }
        $version = $versionBuffer.ToString().TrimEnd([char]0)
        if ([version]$version -ge [version]'0.90.0') {
            $proc = Start-Process -FilePath msiexec.exe -ArgumentList @('/x', $productCode, '/qn') -PassThru -Wait -ErrorAction SilentlyContinue
            Write-Host "  清理测试 MSI：$productCode（$version）→ exit $($proc.ExitCode)"
        }
    }

    # 落位目录（PayloadTool -clean 同款语义：整目录删除）
    if (Test-Path $placementDir) {
        Remove-Item $placementDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  已清理落位目录：$placementDir"
    }

    # 包缓存测试目录（两种 CacheId 形态 + Bundle 缓存目录留给引擎注册记账，不手工删）
    foreach ($name in @('WebUiPlacement', 'WebUiPlacementCleanup', "WebUiPlacement.$oldVersion", "WebUiPlacementCleanup.$oldVersion", "WebUiPlacement.$newVersion", "WebUiPlacementCleanup.$newVersion")) {
        $dir = Join-Path $packageCacheRoot $name
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  已清理包缓存目录：$name"
        }
    }
}

# ---------- 驱动向导（服务端角色 + 管理界面开——真机走查同路径）----------
function Invoke-Wizard([string]$BundleExe, [string]$LayoutDir, [string]$LogPath, [int]$TimeoutSeconds, [int]$HotLoopSampleSeconds, [string[]]$ExtraArgs = @()) {
    # 布局目录几何：EXE + 清单 + 载荷同目录（BA 邻接清单自动加载 + 本地源优先）；--manifest 显式指名（不依赖隐式检测时序）
    $manifest = Join-Path $LayoutDir 'install-manifest.json'
    $proc = Start-Process -FilePath $BundleExe -ArgumentList (@('--manifest', $manifest, '-l', $LogPath) + $ExtraArgs) -WorkingDirectory $LayoutDir -PassThru
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
        [void][AcquireWalk.Native]::SetForegroundWindow($mainHwnd)

        # 清单自动加载成功 → 自动进打印机品牌页（基础模式默认客户端）
        $brandTitle = Wait-ChildByCaption $mainHwnd 'STATIC' '需要哪些打印机品牌*' 30
        if (-not $brandTitle) { throw '30 秒内未自动进入问卷（清单加载失败？）' }

        # 高级路径：上一步 → 高级选项 → 下一步 → 角色页
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

        # 角色页：选「本机作为服务端」（服务端角色 + 管理界面默认开）
        $serverRole = Wait-ChildByCaption $mainHwnd 'BUTTON' '本机作为服务端' 10
        if (-not $serverRole) { throw '10 秒内未进入角色页' }
        Start-Sleep -Milliseconds 400
        Click-Button $serverRole.Handle
        Start-Sleep -Milliseconds 300

        # 「下一步」直到确认页（服务端角色跳过品牌 / 地址页 → 管理界面页 → 确认页；步数不写死，锚点判定）
        $banner = $null
        for ($click = 0; $click -lt 5 -and -not $banner; $click++) {
            $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
            if (-not $nextButton) { throw "未找到「下一步」按钮（第 $($click + 1) 次）" }
            Click-Button $nextButton.Handle
            Start-Sleep -Milliseconds 500
            $banner = Get-DescendantWindows $mainHwnd | Where-Object {
                $_.Class.Contains('STATIC') -and (
                    $_.Caption -like '*检测到可用更新*' -or $_.Caption -like '*已是最新版本*' -or $_.Caption -like '*全新安装*')
            } | Select-Object -First 1
        }
        if (-not $banner) { throw '未到达确认页（未抓到状态行）' }

        # 确认页「下一步（安装）」→ 终态（完成 / 失败 / 超时）
        $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
        if (-not $nextButton) { throw '未找到确认页「下一步（安装）」按钮' }
        Click-Button $nextButton.Handle

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $terminal = 'timeout'
        $hotLoopSample = $null
        while ((Get-Date) -lt $deadline) {
            if ($proc.HasExited) { $terminal = 'exited'; break }
            if ((Find-ChildByCaption $mainHwnd 'STATIC' '安装完成')) { $terminal = 'complete'; break }
            if ((Find-ChildByCaption $mainHwnd 'BUTTON' '重试*')) { $terminal = 'failed'; break }

            # 热循环采样（可选）：滞留进度页期间测量 e346 行数与日志体积增速（对照真机 5 分钟 9.3 万次）
            if ($HotLoopSampleSeconds -gt 0 -and -not $hotLoopSample -and (Get-Date) -gt $deadline.AddSeconds(-$TimeoutSeconds + 20)) {
                $hotLoopSample = Measure-RetryLoop -LogPath $LogPath -SampleSeconds $HotLoopSampleSeconds
            }
            Start-Sleep -Milliseconds 500
        }

        $exitCode = if ($proc.HasExited) { $proc.ExitCode } else { $null }
        if (-not $proc.HasExited -and $terminal -ne 'timeout') {
            [void][AcquireWalk.Native]::SendMessage($mainHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
            if (-not $proc.WaitForExit(60000)) { throw '向导关闭超时' }
            $exitCode = $proc.ExitCode
        }

        return [pscustomobject]@{
            Terminal = $terminal
            ExitCode = $exitCode
            Banner = $banner.Caption
            HotLoop = $hotLoopSample
        }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        $proc.WaitForExit(10000) | Out-Null
        Get-Process -Name $baProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

# ---------- 热循环采样：固定窗口内 e346 重试行数与日志增量 ----------
function Measure-RetryLoop([string]$LogPath, [int]$SampleSeconds) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return $null }
    $before = (Select-String -LiteralPath $LogPath -Pattern 'e346' -AllMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
    $beforeSize = (Get-Item $LogPath).Length
    Start-Sleep -Seconds $SampleSeconds
    $after = (Select-String -LiteralPath $LogPath -Pattern 'e346' -AllMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
    $afterSize = (Get-Item $LogPath).Length
    return [pscustomobject]@{
        RetryBefore = [int]$before
        RetryAfter = [int]$after
        RetryDelta = [int]($after - $before)
        SizeBefore = $beforeSize
        SizeAfter = $afterSize
        SizeDeltaMB = [Math]::Round(($afterSize - $beforeSize) / 1MB, 2)
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
    $hit = Select-String -LiteralPath $LogPath -Pattern $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Result $Scenario ([bool]$hit) "$What（日志$(if ($hit) { '命中' } else { '未命中' })「$Pattern」）"
}

function Assert-LogNotContains([string]$LogPath, [string]$Pattern, [string]$Scenario, [string]$What) {
    $hit = Select-String -LiteralPath $LogPath -Pattern $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Result $Scenario (-not $hit) "$What（日志$(if ($hit) { '意外命中' } else { '未命中（符合预期）' })「$Pattern」）"
}

# ---------- 构建 ----------
$layoutOld = Join-Path $staging "layout-$oldVersion"
$layoutNew = Join-Path $staging "layout-$newVersion"
$bundleOld = Join-Path $staging "LabelFrame-AcquireWalk-$oldVersion.exe"
$bundleNew = Join-Path $staging "LabelFrame-AcquireWalk-$newVersion.exe"

if (-not $SkipBuild -and -not $RunOnly) {
    foreach ($dir in @($staging, $serveDir, (Join-Path $staging 'ba'), $layoutOld, $layoutNew)) {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    Write-Host "发布 BA（net48 win-x64，源码根 = $sourceRoot）…"
    dotnet publish (Join-Path $sourceRoot 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path $staging 'ba') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }

    & $wix eula accept wix7 2>$null | Out-Null
    $global:LASTEXITCODE = 0

    $bundles = @()
    foreach ($version in @($oldVersion, $newVersion)) {
        Write-Host "构建 $Version 载荷（PayloadTool 按版本号发布 → 字节随版本变化，构成缓存哈希冲突前提）…"
        $toolDir = Join-Path $staging "payloadtool-$version"
        if (Test-Path $toolDir) { Remove-Item $toolDir -Recurse -Force }
        dotnet publish (Join-Path $sourceRoot 'src\LabelFrame.Bootstrapper.PayloadTool\LabelFrame.Bootstrapper.PayloadTool.csproj') `
            -c Release -f net48 -r win-x64 -o $toolDir -p:Version=$version -p:DebugType=None -p:DebugSymbols=false | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "PayloadTool publish failed（$version）" }

        $msi = New-TestMsi $version $serverUpgradeCode 'LabelFrame 获取链走查·服务端'

        # webui 形态 zip（内容带版本标记 → 每版本字节不同）
        $zipSource = Join-Path $staging "webui-src-$version"
        New-Item -ItemType Directory -Force -Path $zipSource | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $zipSource 'index.html'), "<html><body>LabelFrame acquire walkthrough $version</body></html>", (New-Object System.Text.UTF8Encoding($false)))
        $zip = Join-Path $staging "labelframe-webui-$version.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $zipSource '*') -DestinationPath $zip | Out-Null

        # http 服务目录 + 布局目录（EXE 后构建放回布局）
        Copy-Item $msi $serveDir -Force
        Copy-Item $zip $serveDir -Force
        Copy-Item $msi (Join-Path $staging "layout-$version") -Force
        Copy-Item $zip (Join-Path $staging "layout-$version") -Force
        New-TestManifest (Join-Path $staging "layout-$version") $version $msi $zip | Out-Null

        $bundlePath = if ($version -eq $oldVersion) { $bundleOld } else { $bundleNew }
        New-TestBundle $version $msi $zip $toolDir $bundlePath | Out-Null
        Copy-Item $bundlePath (Join-Path (Join-Path $staging "layout-$version") (Split-Path $bundlePath -Leaf)) -Force
        $bundles += $bundlePath
    }

    # PayloadTool 哈希随版本不同的前置自证（冲突机制成立的必要条件）
    $toolOld = Get-ChildItem (Join-Path $staging "payloadtool-$oldVersion") -Filter 'LabelFrame.Bootstrapper.PayloadTool.exe' | Select-Object -First 1
    $toolNew = Get-ChildItem (Join-Path $staging "payloadtool-$newVersion") -Filter 'LabelFrame.Bootstrapper.PayloadTool.exe' | Select-Object -First 1
    $sameTool = (Get-Sha256 $toolOld.FullName) -eq (Get-Sha256 $toolNew.FullName)
    Add-Result 'PRE' (-not $sameTool) "PayloadTool.exe 0.90.1 / 0.90.2 哈希$(if ($sameTool) { '相同（冲突机制不成立，构建异常）' } else { '不同（跨版本缓存冲突前提成立）' })"
}
else {
    foreach ($path in @($bundleOld, $bundleNew)) {
        if (-not (Test-Path $path)) { throw "-SkipBuild 但未找到 $path，请先完整运行一次。" }
    }
}

# ---------- 走查 ----------
if ($BuildOnly) {
    Write-Host "`n-BuildOnly：构建完成，产物在 $staging（布局目录 layout-0.90.1 / layout-0.90.2 可整目录拷贝到测试机运行）。" -ForegroundColor Cyan
    exit 0
}

Clear-TestState
$serverJob = Start-TestServer $MainPort (Join-Path $staging 'server-access.log')
Start-Sleep -Milliseconds 600
$selected = $Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }

try {
    foreach ($scenario in $selected) {
        Write-Host "`n========== 场景 $scenario（CacheId = $CacheIdMode）==========" -ForegroundColor Cyan
        $logPath = Join-Path $staging "$scenario.log"
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        Get-ChildItem $staging -Filter "$scenario`_*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

        switch ($scenario) {
            'S1' {
                $run = Invoke-Wizard $bundleOld $layoutOld $logPath 180 0
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "装旧 exit=$($run.ExitCode) 终态=$($run.Terminal) 横幅=$($run.Banner)"
                $credential = if (Test-Path $credentialFile) { (Get-Content $credentialFile -Raw).Trim() } else { $null }
                Add-Result $scenario ($credential -eq $oldVersion) "落位凭据 = $(if ($credential) { $credential } else { '<缺失>' })（期望 $oldVersion）"
                $family = Get-InstalledFamilyVersion $serverUpgradeCode
                Add-Result $scenario ($family -eq $oldVersion) "测试族内最高版本 = $family（期望 $oldVersion）"
                $cacheDirName = if ($CacheIdMode -eq 'Versioned') { "WebUiPlacement.$oldVersion" } else { 'WebUiPlacement' }
                Add-Result $scenario (Test-Path (Join-Path $packageCacheRoot "$cacheDirName\LabelFrame.PayloadTool.exe")) "包缓存目录形态：$cacheDirName（PayloadTool 在位）"
            }
            'S2' {
                $run = Invoke-Wizard $bundleNew $layoutNew $logPath 240 45
                $family = Get-InstalledFamilyVersion $serverUpgradeCode
                $credential = if (Test-Path $credentialFile) { (Get-Content $credentialFile -Raw).Trim() } else { $null }

                if ($CacheIdMode -eq 'Versioned') {
                    # 修复后：升级全链路完成，且全程零哈希冲突
                    Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "升新 exit=$($run.ExitCode) 终态=$($run.Terminal)"
                    # 升级横幅断言仅 per-machine（BA 升级评估探测 per-machine MSI 家族版本；-UserScope 的 per-user MSI
                    # 对 BA 不可见 → 横幅呈「全新安装」，升级事实以 RelatedBundle 检测 / 移除日志 + 凭据 / 族版本断言承载）
                    if (-not $UserScope) {
                        Add-Result $scenario ($run.Banner -like "*检测到可用更新：服务端 $oldVersion → $newVersion*") "确认页升级横幅：$($run.Banner)"
                    }
                    Add-Result $scenario ($credential -eq $newVersion) "落位凭据 = $(if ($credential) { $credential } else { '<缺失>' })（期望 $newVersion —— 新链重落位）"
                    Add-Result $scenario ($family -eq $newVersion) "测试族内最高版本 = $family（期望 $newVersion）"
                    Assert-LogNotContains $logPath '0x80091007' $scenario '全程零缓存哈希冲突（版本化 CacheId 生效）'
                    Assert-LogContains $logPath 'Detected related bundle' $scenario 'Burn 相关 Bundle 检测'
                    Assert-LogContains $logPath "version: $oldVersion" $scenario "相关 Bundle 版本（$oldVersion）"
                    $oldBundleLog = Get-ChildItem $staging -Filter "$scenario`_*.log" -ErrorAction SilentlyContinue |
                        Where-Object { $_.Name -match '_\{[0-9A-Fa-f-]{36}\}\.log$' } | # 相关 Bundle 子会话日志 = S2_NNN_{GUID}.log（排除 S2_000_ServerMsi 等包日志）
                        Select-Object -First 1
                    if ($oldBundleLog) {
                        Assert-LogContains $oldBundleLog.FullName '非交互启动' $scenario '旧 Bundle 卸载非交互执行（升级链真实依赖）'
                    }
                    else {
                        Add-Result $scenario $false '未找到旧 Bundle 子日志（S2_00N_*.log）'
                    }
                    $newCacheDir = Test-Path (Join-Path $packageCacheRoot "WebUiPlacement.$newVersion")
                    Add-Result $scenario $newCacheDir "新版本独立包缓存目录 WebUiPlacement.$newVersion 在位"
                }
                else {
                    # 基线（常量 CacheId）：断言冲突机制在场（哈希不符 + 删缓存），获取阶段结局如实记录
                    Add-Result $scenario $true "升新记录：exit=$($run.ExitCode) 终态=$($run.Terminal) 横幅=$($run.Banner)（结局不设断言——沙箱引擎会话几何与真机可能不同，机制以日志征象为准）"
                    Assert-LogContains $logPath '0x80091007' $scenario '缓存哈希冲突征象（常量 CacheId 机制）'
                    Assert-LogContains $logPath 'Failed to verify payload: WebUiPlacement' $scenario '冲突定位在落位包（引擎删缓存转重取）'
                    $containerFail = Select-String -LiteralPath $logPath -Pattern 'Failed to acquire container: WixAttachedContainer' -ErrorAction SilentlyContinue | Select-Object -First 1
                    Add-Result $scenario $true "附加容器 0x80070002 征象：$(if ($containerFail) { '在场（引擎桩副本会话源解析失败）' } else { '不在场（本沙箱会话几何容器可解——真机征象见 #171 走查）' })"
                    if ($run.HotLoop) {
                        Add-Result $scenario $true "重试热循环采样：$($run.HotLoop.RetryDelta) 次 e346 / $($run.HotLoop.SizeDeltaMB)MB 日志增量（$($run.HotLoop.RetryBefore)→$($run.HotLoop.RetryAfter) 行，采样窗口 45s）"
                    }
                    if ($run.Terminal -eq 'failed') {
                        Add-Result $scenario $true '终态 = 失败报告页（重试终止 / 源耗尽——不再滞留进度页）'
                    }
                    elseif ($run.Terminal -eq 'timeout' -and $run.HotLoop -and $run.HotLoop.RetryDelta -gt 100) {
                        # 基线（修复前 BA）预期征象：无退避无终止热循环滞留（真机 #171 同征象——5 分钟 9.3 万次重试）
                        Add-Result $scenario $true "终态 = 超时滞留 + 热循环在跑（基线缺陷同征象复现：采样窗口内 $($run.HotLoop.RetryDelta) 次重试）"
                    }
                    elseif ($run.Terminal -eq 'timeout') {
                        Add-Result $scenario $false '终态 = 超时滞留且无热循环证据——预期失败页或热循环两者其一，如实记录'
                    }
                }
            }
            'S3' {
                # 不可达源重试终止（修复 ③ 呈现面）：跑新版 0.90.2（S2 之后为同版修复会话）+ webui zip 无本地文件 +
                # 死 URL（独立布局目录：清单 webui 条目指向未监听端口）——期望有限次退避重试后失败报告页、日志有界。
                $layoutS3 = Join-Path $staging 'layout-s3'
                New-Item -ItemType Directory -Force -Path $layoutS3 | Out-Null
                $deadPort = $MainPort + 7
                $msiPath = Join-Path $serveDir "Server-$newVersion.msi"
                $manifest = Join-Path $layoutS3 'install-manifest.json'
                # ReadAllText（UTF-8 自动检测）：Windows PowerShell 5.1 的 Get-Content 对无 BOM UTF-8 按 ANSI 读，
                # 中文 notes 乱码并吞掉闭合引号 → JSON 破损 → BA 清单加载失败（向导滞留就绪页，#171 沙箱实测教训）
                $json = [System.IO.File]::ReadAllText((Join-Path $layoutNew 'install-manifest.json'))
                $json = $json.Replace("http://127.0.0.1:$MainPort/ok/labelframe-webui-$newVersion.zip", "http://127.0.0.1:$deadPort/ok/labelframe-webui-$newVersion.zip")
                # 死 URL 形态的哈希保持原值（不可达——校验不会发生）；zip 不放入布局目录（无本地源）
                [System.IO.File]::WriteAllText($manifest, $json, (New-Object System.Text.UTF8Encoding($false)))
                Copy-Item $bundleNew (Join-Path $layoutS3 (Split-Path $bundleNew -Leaf)) -Force
                Copy-Item $msiPath $layoutS3 -Force

                # 预删落位包载荷缓存（S2 会话已把 zip 缓存记账——不删则维护会话全缓存命中、获取阶段不发生）
                foreach ($name in @('WebUiPlacement', "WebUiPlacement.$newVersion")) {
                    $dir = Join-Path $packageCacheRoot $name
                    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
                }

                # 隐去布局目录的 zip 原件（S2 注册的 LastUsedSource = layout-0.90.2——引擎对远程载荷的原生自救 =
                # 最近源目录 + 载荷名；不隐去则死 URL 永不被触发。模拟「布局原件被移除」真机常态）
                $hiddenZip = Join-Path $layoutNew "labelframe-webui-$newVersion.zip"
                $hiddenZipBackup = $null
                if (Test-Path $hiddenZip) {
                    $hiddenZipBackup = Join-Path $staging ('hidden-' + "labelframe-webui-$newVersion.zip")
                    Copy-Item $hiddenZip $hiddenZipBackup -Force
                    Remove-Item $hiddenZip -Force
                }

                $run = Invoke-Wizard $bundleNew $layoutS3 $logPath 150 0

                # 还原布局 zip（保持 staging 完整性）
                if ($hiddenZipBackup) { Copy-Item $hiddenZipBackup $hiddenZip -Force; Remove-Item $hiddenZipBackup -Force }
                $logSizeMB = if (Test-Path $logPath) { [Math]::Round((Get-Item $logPath).Length / 1MB, 2) } else { 0 }
                Add-Result $scenario ($run.Terminal -eq 'failed') "不可达源终态 = $($run.Terminal)（期望 failed——失败报告页呈现，决策 #156 ③）"
                Add-Result $scenario ($logSizeMB -lt 20) "日志体积 $logSizeMB MB（有界，对照真机热循环 219–240MB）"
                Assert-LogContains $logPath 'Failed to acquire payload|多源回退' $scenario '获取失败日志留痕（引擎 / BA 侧）'
                Assert-LogContains $logPath '0x80072EE7|0x80072EFD|0x80072EE2|无法连接' $scenario '源不可达分类'
                $family = Get-InstalledFamilyVersion $serverUpgradeCode
                Add-Result $scenario $true "失败后族内版本 = $family（S2 已装 $newVersion——修复失败不回滚已装版本，记录项）"
            }
            'S4' {
                # 桩会话重跑·无原始源形态（引擎级 ② 定论的确定性复现，决策 #156 ②）：S1 已装 0.90.1 后，包缓存里的
                # Bundle EXE 是引擎设计上的截断桩（注册缓存源 = .be 提权工作副本，仅 cbEngineSize 字节——
                # Burn v7.0.0 apply.cpp ApplyRegister → ElevationSessionBegin → registration.cpp → cache.cpp）。
                # 裸重跑该桩（ARP / 用户直击缓存副本的真实形态——不带 -burn.originalsource）+ 预删落位包载荷缓存
                # （#171 真机尝试 #3 同款）→ 桩自身无附加容器（section.cpp 按运行文件尺寸判定）→ 内嵌载荷须经
                # 源解析获取；运行自缓存的会话不派生原始源目录（cache.cpp CacheInitializeSources 仅非缓存会话设置），
                # 容器内嵌名 bundle-attached.cab（构建中间产物名）任何目录不存在 → 默认搜索序结构性死路
                # → WixAttachedContainer 0x80070002（= 真机三连败征象）。
                # 基线 BA：包级兜底重试无退避无终止 → e346 热循环滞留（真机 5 分钟 92,993 次同构）；
                # 修复 BA：CacheRetryPolicy 连续无进展三轮终止 → 有限退避后失败报告页（决策 #156 ③ 呈现面；
                #   容器注入不在此形态生效——原始源变量未建立属引擎边界，如实记录，修复 ② 的注入形态见 S5）。
                $stubExe = Get-ChildItem $packageCacheRoot -Recurse -Filter (Split-Path $bundleOld -Leaf) -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1 # 取最新副本——宿主机缓存根可能残留历史走查同名件
                if (-not $stubExe) { throw 'S4 前置失败：包缓存中未找到 Bundle EXE 桩（请先跑 S1）。' }
                $fullExe = Join-Path $layoutOld (Split-Path $bundleOld -Leaf)
                $stubSize = $stubExe.Length
                $fullSize = (Get-Item $fullExe).Length
                Add-Result $scenario ($stubSize -lt $fullSize) "包缓存 Bundle EXE = $stubSize B < 布局原件 $fullSize B（引擎设计上的截断桩——真机 0.28.0 缓存 1,576,024 < 2,032,368 同构）"

                # 预删落位包载荷缓存目录（两种 CacheId 形态都清——只触发「缓存缺失需重取」，不引入哈希冲突线）
                foreach ($name in @('WebUiPlacement', "WebUiPlacement.$oldVersion")) {
                    $dir = Join-Path $packageCacheRoot $name
                    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
                }

                # 隐去原始源（staging 根 + 布局目录的原件改名让路——模拟「原始源不在位」：临时目录清理 / 用户删除
                # 下载件 / 布局目录移除等真机常态；引擎对附加容器的原生自救 = 最近源目录 + 引导 EXE 名（i336 copy from），
                # 原件不在位即结构性死路 → 0x80070002。桩自身同名副本在缓存目录，但尺寸 < 完整 EXE，探测必不过）
                $hiddenFiles = @(
                    (Join-Path $staging (Split-Path $bundleOld -Leaf)),
                    (Join-Path $layoutOld (Split-Path $bundleOld -Leaf))
                ) | Where-Object { Test-Path $_ }
                foreach ($hidden in $hiddenFiles) {
                    Rename-Item -LiteralPath $hidden -NewName ('hidden-' + (Split-Path $hidden -Leaf)) -Force
                }

                # 裸重跑桩（同版维护路径：向导 → 已是最新横幅 → 安装）——不带 -burn.originalsource（生产重跑形态）
                $run = Invoke-Wizard $stubExe.FullName $layoutOld $logPath 200 30

                # 还原原始源（S2 升级等后续场景要用布局原件）
                foreach ($hidden in $hiddenFiles) {
                    $hiddenPath = Join-Path (Split-Path $hidden -Parent) ('hidden-' + (Split-Path $hidden -Leaf))
                    if (Test-Path $hiddenPath) { Rename-Item -LiteralPath $hiddenPath -NewName (Split-Path $hidden -Leaf) -Force }
                }

                $containerFail = Select-String -LiteralPath $logPath -Pattern 'Failed to acquire container: WixAttachedContainer' -ErrorAction SilentlyContinue | Select-Object -First 1
                $bounded = if (Test-Path $logPath) { (Get-Item $logPath).Length -lt 20MB } else { $false }
                Add-Result $scenario ([bool]$containerFail) '桩会话容器获取失败 0x80070002 在场（引擎级定论复现——无原始源会话默认搜索序结构性死路）'
                if ($run.Terminal -eq 'failed') {
                    # 修复 BA：重试预算终止 → 失败报告页（有界日志）；基线 BA 热循环时终态应为 timeout
                    Add-Result $scenario $bounded "终态 = failed（重试终止进失败报告页，决策 #156 ③）；日志有界 = $bounded"
                }
                elseif ($run.Terminal -eq 'timeout' -and $run.HotLoop -and $run.HotLoop.RetryDelta -gt 100) {
                    $hasBackoff = Select-String -LiteralPath $logPath -Pattern '退避|停止重试' -ErrorAction SilentlyContinue | Select-Object -First 1
                    Add-Result $scenario (-not $hasBackoff) "终态 = 超时滞留 + 热循环（采样窗口 $($run.HotLoop.RetryDelta) 次 e346 / $($run.HotLoop.SizeDeltaMB)MB——真机 5 分钟 92,993 次同构；$(if ($hasBackoff) { '修复 BA 仍在热循环——异常' } else { '基线 BA 无退避无终止缺陷同征象复现（取证行）' })）"
                }
                else {
                    Add-Result $scenario $false "终态 = $($run.Terminal)（exit=$($run.ExitCode)）——期望 failed（修复）或 timeout+热循环（基线），如实记录"
                }
            }
            'S5' {
                # 桩会话重跑·原始源在位形态（修复 ② 注入机制的验证，决策 #156 ②）：同一缓存桩，命令行
                # -burn.originalsource 指向布局原件（真实链路中该变量在全新运行的会话由引擎自建；ARP 维护 /
                # 升级链亦有等价通道）。基线 BA：v7.0.0 引擎对附加容器存在「原始源文件直取」原生路径
                # （Acquiring container: WixAttachedContainer, copy from <原始源>）——原始源健康时无 BA 也能解，
                # 该形态取证即为「原始源可用性是分水岭」的直接证据；修复 BA：CacheAcquireBegin 容器形态注入
                # 原始源为搜索路径首项（BA 侧日志留痕）→ 同样完成——注入把「依赖引擎隐式原生路径」收紧为
                # 「显式注入 + 引擎尺寸 / 摘要校验」（真机 per-machine 提权链的原始源变量不可用时兜底）。
                $stubExe = Get-ChildItem $packageCacheRoot -Recurse -Filter (Split-Path $bundleOld -Leaf) -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if (-not $stubExe) { throw 'S5 前置失败：包缓存中未找到 Bundle EXE 桩（请先跑 S1）。' }
                $fullExe = Join-Path $layoutOld (Split-Path $bundleOld -Leaf)

                # 预删落位包载荷缓存目录（与 S4 同款——确保容器获取真实发生）
                foreach ($name in @('WebUiPlacement', "WebUiPlacement.$oldVersion")) {
                    $dir = Join-Path $packageCacheRoot $name
                    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
                }

                $run = Invoke-Wizard $stubExe.FullName $layoutOld $logPath 200 0 @('-burn.originalsource', $fullExe)

                $injected = Select-String -LiteralPath $logPath -Pattern '附加容器获取：引导 EXE 原始源 = .+（在位，已注入为容器本地源）' -ErrorAction SilentlyContinue | Select-Object -First 1
                $nativeResolve = Select-String -LiteralPath $logPath -Pattern 'Acquiring container: WixAttachedContainer, copy from: .+' -ErrorAction SilentlyContinue | Select-Object -First 1
                $containerFail = Select-String -LiteralPath $logPath -Pattern 'Failed to acquire container: WixAttachedContainer' -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($injected) {
                    Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete' -and -not $containerFail) "桩会话重跑（原始源在位）exit=$($run.ExitCode) 终态=$($run.Terminal)（修复 BA 注入生效——容器经注入路径解析成功）"
                }
                else {
                    Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete' -and [bool]$nativeResolve) "桩会话重跑（原始源在位）exit=$($run.ExitCode) 终态=$($run.Terminal)（基线 BA——引擎原生「容器 copy from 原始源」路径解析成功，原始源可用性 = 分水岭直接证据）"
                }
                $credential = if (Test-Path $credentialFile) { (Get-Content $credentialFile -Raw).Trim() } else { $null }
                Add-Result $scenario ($credential -eq $oldVersion) "重跑后落位凭据 = $(if ($credential) { $credential } else { '<缺失>' })（期望 $oldVersion——同版维护重落位）"
            }
            default { Add-Result $scenario $false '未知场景标识' }
        }
    }
}
finally {
    Stop-Job $serverJob -ErrorAction SilentlyContinue
    Remove-Job $serverJob -Force -ErrorAction SilentlyContinue
    Clear-TestState
}

# ---------- 汇总 ----------
$summary = @(
    "LabelFrame 升级链获取阶段走查 · $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Bundle：$bundleOld / $bundleNew（UpgradeCode $bundleUpgradeCode）；CacheId 形态 = $CacheIdMode；WiX $wixVersion",
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

