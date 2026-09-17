# LabelFrame 离线布局走查（迭代 70 / Issue #89 ·DESIGN §6.2 / §6.10，决策 #135）
#
# 目的：本地同构取证（断网实机的机器级口径无法本机自证，实机部分转待验收）——
#   G    在线机器生成布局目录：bundle --layout <目录> --manifest <测试源>（BA 生成模式，passive 无向导）
#        → 目录含全部组件（sha256 与 manifest 逐字节一致）+ install-manifest.json + latest.json + 引导 EXE；
#   I1   离线首装（隐式检测）：运行布局目录内的引导 EXE，欢迎页默认邻接本地清单（不改来源输入框），
#        全流程安装成功；测试源保持在线但访问日志零命中（零外网等价取证）+ BA「本地源命中」日志；
#   I2   篡改拦截（断网 fail-closed，AC-04）：篡改布局内组件一字节，测试源停机（外网不可达等价），
#        sha256 校验拦截 → 换源失败 → 源耗尽失败报告，篡改内容绝不落装；
#   I2b  篡改拦截（在线换源重取，AC-04 另一口径）：EXE 于布局目录之外、清单指向被篡改布局目录——
#        本地源校验失败 → 按有效源序（[本地] ++ urls）换 urls 重取干净副本 → 安装成功；
#   R1   无布局目录在线安装回归（AC-03）：无邻接清单时默认稳定通道形态（走查注入测试 URL），
#        按 urls 下载安装——行为与现状一致（既有下载引擎测试全数保留另行通过 dotnet test / 矩阵脚本）。
# 载体与手法复刻 test-bundle-download-matrix.ps1：per-user 测试 Bundle（双 ExePackage 规避提权）+ 真实 BA
#   七页向导 Win32 UI 自动化 + 本地 TcpListener 测试源（访问日志断言）。
#
# 用法（仓库根目录，Windows PowerShell 5.1+，需交互桌面会话）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-offline-layout.ps1
#   可选：-Scenarios 'G,I1'（子集）/ -SkipBuild（复用已构建产物）/ -Port 8140
# 证据：artifacts\bootstrapper-tests\offline\<场景>.log（Burn 日志）+ summary.txt + 访问日志。
# 临时产物不进仓（artifacts/ 已 gitignore）；本脚本随仓版本化（走查可复现）。
param(
    [string]$WixPath = '',
    [int]$Port = 8140,
    [string]$Scenarios = 'G,I1,I2,I2b,R1',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'artifacts\bootstrapper-tests'
$serveDir = Join-Path $staging 'serve-offline'
$offlineDir = Join-Path $staging 'offline'
$bundleName = 'LabelFrame 离线布局走查'
$baProcessName = 'LabelFrame.Bootstrapper.Ba'
$serverFileName = 'lf-offline-server.exe'
$clientFileName = 'lf-offline-client.exe'
$cacheIdServer = 'LfOfflineLayoutServerMsi'
$cacheIdClient = 'LfOfflineLayoutClientMsi'
$upgradeCode = [guid]::NewGuid().ToString().ToUpperInvariant()
$bundleExeName = 'LabelFrame-OfflineLayout.exe'
$bundleExe = Join-Path $staging $bundleExeName
$manifestUrl = "http://127.0.0.1:$Port/releases/latest/download/install-manifest.json"
$latestUrl = "http://127.0.0.1:$Port/releases/latest/download/latest.json"
$serverUrl = "http://127.0.0.1:$Port/main/$serverFileName"
$clientUrl = "http://127.0.0.1:$Port/main/$clientFileName"

# ---------- 前置：wix ----------
$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（wix.exe），请安装 WiX Toolset v7 或以 -WixPath 指定。" }
$wixVersion = & $wix --version
Write-Host "WiX：$wixVersion"

# ---------- Win32 UI 驱动（复刻矩阵脚本：WinForms 注册类名子串匹配）----------
Add-Type -Namespace OfflineUi -Name Native -MemberDefinition @"
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
    $callback = [OfflineUi.Native+EnumProc]{
        param($hwnd, $lParam)
        $textBuilder = New-Object System.Text.StringBuilder 256
        [void][OfflineUi.Native]::GetWindowText($hwnd, $textBuilder, 256)
        $classBuilder = New-Object System.Text.StringBuilder 128
        [void][OfflineUi.Native]::GetClassName($hwnd, $classBuilder, 128)
        $items.Add([pscustomobject]@{ Handle = $hwnd; Class = $classBuilder.ToString(); Caption = $textBuilder.ToString() })
        return $true
    }
    [void][OfflineUi.Native]::EnumChildWindows($parent, $callback, [IntPtr]::Zero)
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
    [void][OfflineUi.Native]::SendMessage($hwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
}

function Set-EditText([IntPtr]$hwnd, [string]$text) {
    [void][OfflineUi.Native]::SendMessage($hwnd, 0x000C, [IntPtr]::Zero, $text) # WM_SETTEXT
}

# ---------- 本地 HTTP 测试源（精确路径路由 + 访问日志；断网场景直接停机）----------
function Start-TestServer([int]$PortNumber, [string]$LogFile, [string]$ManifestPath, [string]$LatestPath) {
    $job = Start-Job -ScriptBlock {
        param($PortNumber, $LogFile, $ServeDir, $ManifestPath, $LatestPath)
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $PortNumber)
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
                    $rawPath = if ($parts.Count -ge 2) { $parts[1] } else { '/' }
                    $noQuery = ($rawPath -split '\?')[0]
                    Add-Content -LiteralPath $LogFile -Value "GET $noQuery"

                    $file = $null
                    switch ($noQuery) {
                        '/releases/latest/download/install-manifest.json' { $file = $ManifestPath }
                        '/releases/latest/download/latest.json' { $file = $LatestPath }
                        default {
                            if ($noQuery -match '^/main/(.+)$') {
                                $file = [System.IO.Path]::Combine($ServeDir, [System.Uri]::UnescapeDataString($Matches[1]))
                            }
                        }
                    }

                    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::ASCII)
                    $writer.NewLine = "`r`n"
                    $writer.AutoFlush = $true
                    if ($null -eq $file -or -not (Test-Path -LiteralPath $file)) {
                        $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                    }
                    else {
                        $bytes = [System.IO.File]::ReadAllBytes($file)
                        $writer.Write("HTTP/1.1 200 OK`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
                        $stream.Write($bytes, 0, $bytes.Length)
                        $stream.Flush()
                    }
                    $writer.Dispose()
                }
                catch {
                }
                finally { $client.Close() }
            }
        }
        finally { $listener.Stop() }
    } -ArgumentList $PortNumber, $LogFile, $serveDir, $ManifestPath, $LatestPath
    return $job
}

function Read-AccessLog([string]$LogFile) {
    if (-not (Test-Path -LiteralPath $LogFile)) { return @() }
    return @(Get-Content -LiteralPath $LogFile | Where-Object { $_ })
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------- 构建测试 Bundle（per-user 双 ExePackage 链 + 真实 BA；矩阵脚本同构）----------
if (-not $SkipBuild) {
    foreach ($dir in @($staging, $serveDir, $offlineDir, (Join-Path $staging 'ba'), (Join-Path $staging 'payloadtool'))) {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    Write-Host '发布 BA（net48 win-x64）与 PayloadTool（测试链包载体）…'
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path $staging 'ba') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.PayloadTool\LabelFrame.Bootstrapper.PayloadTool.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path $staging 'payloadtool') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'PayloadTool publish failed' }

    # 测试链载荷：PayloadTool 本体双份（无参 = 空操作退出 0）；客户端副本尾部标记字节（两包可区分）
    $payloadToolExe = Join-Path $staging 'payloadtool\LabelFrame.Bootstrapper.PayloadTool.exe'
    Copy-Item $payloadToolExe (Join-Path $serveDir $serverFileName) -Force
    Copy-Item $payloadToolExe (Join-Path $serveDir $clientFileName) -Force
    Add-Content -LiteralPath (Join-Path $serveDir $clientFileName) -Value 'm' -NoNewline

    # 测试 manifest / latest（G 场景由测试源提供；哈希 / 体积按本地产物实测）
    $manifestPath = Join-Path $staging 'manifest-offline.json'
    $manifestJson = (@(
        '{'
        '  "schemaVersion": 1,'
        '  "labelframeVersion": "0.0.1",'
        '  "generatedAt": "2026-09-17T00:00:00Z",'
        '  "components": ['
        '    {'
        '      "id": "server-msi", "type": "msi", "version": "0.0.1", "dependsOn": [],'
        ('      "urls": ["' + $serverUrl + '"],')
        ('      "sha256": "' + (Get-Sha256 (Join-Path $serveDir $serverFileName)) + '",')
        ('      "sizeBytes": ' + (Get-Item (Join-Path $serveDir $serverFileName)).Length + ',')
        '      "silentArgs": "", "topologies": ["standalone"], "notes": "离线布局走查 · 服务端包"'
        '    },'
        '    {'
        '      "id": "client-msi", "type": "msi", "version": "0.0.1", "dependsOn": [],'
        ('      "urls": ["' + $clientUrl + '"],')
        ('      "sha256": "' + (Get-Sha256 (Join-Path $serveDir $clientFileName)) + '",')
        ('      "sizeBytes": ' + (Get-Item (Join-Path $serveDir $clientFileName)).Length + ',')
        '      "silentArgs": "", "topologies": ["standalone"], "notes": "离线布局走查 · 客户端包"'
        '    }'
        '  ]'
        '}'
    ) -join "`n") + "`n"
    [System.IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

    $latestPath = Join-Path $staging 'latest-offline.json'
    $latestJson = (@(
        '{'
        '  "labelframeVersion": "0.0.1",'
        ('  "manifestUrl": "' + $manifestUrl + '"')
        '}'
    ) -join "`n") + "`n"
    [System.IO.File]::WriteAllText($latestPath, $latestJson, (New-Object System.Text.UTF8Encoding($false)))

    # 测试 Bundle.wxs（临时产物不进仓；机制复刻 Bundle.wxs 下载语义 + 矩阵脚本 per-user 手法）
    $bundleWxs = Join-Path $staging 'BundleOffline.wxs'
    $payloadGroupLines = @('    <PayloadGroup Id="PayloadToolDependencies">')
    foreach ($dep in (Get-ChildItem (Join-Path $staging 'payloadtool') -File | Where-Object { $_.Name -ne 'LabelFrame.Bootstrapper.PayloadTool.exe' } | Sort-Object Name)) {
        $payloadGroupLines += "      <Payload SourceFile=`"`$(var.PayloadToolDir)$($dep.Name)`" Compressed=`"yes`" />"
    }
    $payloadGroupLines += '    </PayloadGroup>'
    $wxsLines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 离线布局走查测试 Bundle（自动生成，勿手工编辑；迭代 70 / #89）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Bundle Name=`"$bundleName`" Version=`"0.0.1`" Manufacturer=`"LabelFrame`" UpgradeCode=`"$upgradeCode`">"
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
        "      <ExePackage Id=`"ServerMsi`" CacheId=`"$cacheIdServer`" SourceFile=`"`$(var.ServeDir)$serverFileName`""
        "                  Name=`"$serverFileName`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`" InstallCondition=`"InstallServer`""
        "                  DownloadUrl=`"$serverUrl`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        "      <ExePackage Id=`"ClientMsi`" CacheId=`"$cacheIdClient`" SourceFile=`"`$(var.ServeDir)$clientFileName`""
        "                  Name=`"$clientFileName`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`" InstallCondition=`"InstallClient`""
        "                  DownloadUrl=`"$clientUrl`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        '    </Chain>'
        '  </Bundle>'
        '</Wix>'
    )
    [System.IO.File]::WriteAllLines($bundleWxs, $wxsLines, (New-Object System.Text.UTF8Encoding($false)))

    & $wix eula accept wix7 2>$null | Out-Null
    $global:LASTEXITCODE = 0
    & $wix build $bundleWxs `
        -d "BaDir=$(Join-Path $staging 'ba')\" -d "ServeDir=$serveDir\" -d "PayloadToolDir=$(Join-Path $staging 'payloadtool')\" `
        -o $bundleExe -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed（测试 Bundle）' }
    # wix build 会在输出目录留包载荷硬链接——必须清除，否则 R1（在线回归）会被 Bundle 同目录本地源短路
    foreach ($name in @($serverFileName, $clientFileName)) {
        Remove-Item -LiteralPath (Join-Path $staging $name) -Force -ErrorAction SilentlyContinue
    }
    Write-Host "测试 Bundle 构建完成：$bundleExe（$([Math]::Round((Get-Item $bundleExe).Length / 1MB, 2)) MB）"
}
else {
    if (-not (Test-Path $bundleExe)) { throw "-SkipBuild 但未找到 $bundleExe，请先完整运行一次。" }
    if (-not (Test-Path (Join-Path $serveDir $serverFileName))) { throw "-SkipBuild 但测试载荷缺失（$serveDir），请先完整运行一次。" }
}

$testManifestPath = Join-Path $staging 'manifest-offline.json'
$testLatestPath = Join-Path $staging 'latest-offline.json'

# ---------- Burn per-user 状态清理（缓存 / 注册——场景隔离与收尾）----------
function Clear-PackageCache {
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'Package Cache'
    foreach ($cacheId in @($cacheIdServer, $cacheIdClient)) {
        $dir = Join-Path $cacheRoot $cacheId
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  已清理包缓存：$cacheId"
        }
    }
}

function Remove-TestRegistration {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (-not (Test-Path $uninstallRoot)) { return }
    foreach ($key in (Get-ChildItem $uninstallRoot -ErrorAction SilentlyContinue)) {
        $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
        if ($displayName -eq $bundleName) {
            Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  已清理测试 Bundle 注册：$($key.PSChildName)"
        }
    }
}

# ---------- 驱动向导走完问卷并等待终态 ----------
# -ManifestSourceText：注入清单来源输入框（空串 = 不注入——I1/I2 验证邻接清单隐式默认）；
# -UseBundle：运行哪个 EXE（布局目录内副本 vs 构建产物）。
function Invoke-WizardInstall([string]$LogPath, [int]$TimeoutSeconds, [string]$ManifestSourceText = '', [string]$UseBundle = '') {
    $exe = if ($UseBundle) { $UseBundle } else { $bundleExe }
    $proc = Start-Process -FilePath $exe -ArgumentList @('-l', $LogPath) -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(40)
        $mainHwnd = [IntPtr]::Zero
        while ((Get-Date) -lt $deadline -and $mainHwnd -eq [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 300
            foreach ($candidate in (Get-Process -Name $baProcessName -ErrorAction SilentlyContinue)) {
                if ($candidate.MainWindowTitle -like '*LabelFrame 安装引导*') {
                    $mainHwnd = $candidate.MainWindowHandle
                    break
                }
            }
            if ($mainHwnd -eq [IntPtr]::Zero -and $proc.HasExited) { throw 'BA 向导窗口未出现且 Bundle 进程已退出' }
        }
        if ($mainHwnd -eq [IntPtr]::Zero) { throw '40 秒内未找到 BA 向导主窗口' }
        [void][OfflineUi.Native]::SetForegroundWindow($mainHwnd)

        # 欢迎页：按需注入清单来源 → 加载（成功自动进下一页）
        if ($ManifestSourceText) {
            $sourceEdit = Find-ChildByCaption $mainHwnd 'EDIT' '*'
            if (-not $sourceEdit) { throw '未找到清单来源输入框' }
            Set-EditText $sourceEdit.Handle $ManifestSourceText
        }
        $loadButton = Find-ChildByCaption $mainHwnd 'BUTTON' '加载清单'
        if (-not $loadButton) { throw '未找到「加载清单」按钮' }
        Click-Button $loadButton.Handle

        # 拓扑页：选「单机一体」（两包都纳入：InstallServer + InstallClient）
        $standalone = Wait-ChildByCaption $mainHwnd 'BUTTON' '单机一体' 20
        if (-not $standalone) { throw '20 秒内未进入拓扑页（清单加载失败？）' }
        Start-Sleep -Milliseconds 400
        Click-Button $standalone.Handle
        Start-Sleep -Milliseconds 300

        # 「下一步」×4：品牌 → 管理界面 → 确认页 → 确认页「下一步」= 开始安装
        for ($click = 0; $click -lt 4; $click++) {
            $nextButton = Wait-ChildByCaption $mainHwnd 'BUTTON' '下一步*' 10
            if (-not $nextButton) { throw "未找到「下一步」按钮（第 $($click + 1) 次）" }
            Click-Button $nextButton.Handle
            Start-Sleep -Milliseconds 500
        }

        # 等终态：完成页或失败报告（重试按钮出现）
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
            [void][OfflineUi.Native]::SendMessage($mainHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
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

function Assert-LogNotContains([string]$LogPath, [string]$Pattern, [string]$Scenario, [string]$What) {
    $hit = Select-String -LiteralPath $LogPath -Pattern $Pattern | Select-Object -First 1
    Add-Result $Scenario (-not $hit) "$What（日志$(if ($hit) { '仍命中' } else { '零命中' })「$Pattern」）"
}

# ---------- 启动测试源 ----------
$accessLog = Join-Path $offlineDir 'access.log'
$serverJob = Start-TestServer $Port $accessLog $testManifestPath $testLatestPath
Start-Sleep -Seconds 1
if ($serverJob.State -ne 'Running') { throw "测试源启动失败（state=$($serverJob.State)）——检查端口占用。" }
Write-Host "测试源就绪：:$Port（访问日志 $offlineDir）"

Remove-TestRegistration
Clear-PackageCache

# 布局目录（G 生成；I 系列各用独立副本）
$layoutDir = Join-Path $offlineDir 'layout'

$selected = $Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }

try {
    foreach ($scenario in $selected) {
        Write-Host "`n========== 场景 $scenario ==========" -ForegroundColor Cyan
        $logPath = Join-Path $offlineDir "$scenario.log"
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        Clear-PackageCache
        Remove-Item -LiteralPath $accessLog -Force -ErrorAction SilentlyContinue

        switch ($scenario) {
            'G' {
                # 在线机器生成布局目录（BA --layout 模式，passive 无向导；清单来源 = 测试源 URL）
                if (Test-Path -LiteralPath $layoutDir) { Remove-Item -LiteralPath $layoutDir -Recurse -Force }
                $genLog = Join-Path $offlineDir 'G.log'
                Remove-Item -LiteralPath $genLog -Force -ErrorAction SilentlyContinue
                $proc = Start-Process -FilePath $bundleExe -ArgumentList @('-passive', '-l', $genLog, '--layout', $layoutDir, '--manifest', $manifestUrl) -PassThru
                if (-not $proc.WaitForExit(180000)) {
                    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                    Add-Result $scenario $false '生成超时（180s）'
                }
                else {
                    Add-Result $scenario ($proc.ExitCode -eq 0) "生成退出码 = $($proc.ExitCode)（期望 0）"

                    # 目录完整性：双组件（sha256 与 manifest 一致）+ manifest + latest + 引导 EXE
                    $manifest = Get-Content -LiteralPath $testManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    foreach ($entry in @($manifest.components)) {
                        $fileName = ($entry.urls[0] -split '/')[-1]
                        $file = Join-Path $layoutDir $fileName
                        $present = Test-Path -LiteralPath $file
                        Add-Result $scenario $present "布局文件在位：$fileName"
                        if ($present) {
                            Add-Result $scenario ((Get-Sha256 $file) -eq $entry.sha256) "布局哈希与 manifest 一致：$fileName（AC-01）"
                        }
                    }
                    $manifestCopy = Join-Path $layoutDir 'install-manifest.json'
                    Add-Result $scenario ((Test-Path -LiteralPath $manifestCopy) -and ((Get-FileHash -LiteralPath $manifestCopy).Hash -eq (Get-FileHash -LiteralPath $testManifestPath).Hash)) 'install-manifest.json 原样字节落盘'
                    Add-Result $scenario (Test-Path -LiteralPath (Join-Path $layoutDir 'latest.json')) 'latest.json 落盘'
                    $layoutExe = Join-Path $layoutDir $bundleExeName
                    Add-Result $scenario ((Test-Path -LiteralPath $layoutExe) -and ((Get-FileHash -LiteralPath $layoutExe).Hash -eq (Get-FileHash -LiteralPath $bundleExe).Hash)) '引导 EXE 自复制入目录（AC-01）'
                    Assert-LogContains $genLog '布局目录生成结束：exit=0x00000000' $scenario 'BA 生成完成日志'
                }
            }
            'I1' {
                # 离线首装（隐式检测）：运行布局目录内的 EXE，不改清单来源输入框（邻接清单默认）；
                # 测试源保持在线——访问日志零命中 = 零外网等价取证（AC-02 本地同构口径）
                if (-not (Test-Path -LiteralPath (Join-Path $layoutDir $bundleExeName))) { Add-Result $scenario $false '前置缺失：请先运行 G'; continue }
                $run = Invoke-WizardInstall $logPath 180 '' (Join-Path $layoutDir $bundleExeName)
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Assert-LogContains $logPath '检测到引导程序同目录安装清单，默认离线布局安装' $scenario '邻接清单隐式检测（零网络起步）'
                Assert-LogContains $logPath "本地源命中：组件 ServerMsi 使用布局目录文件 $serverFileName" $scenario '服务端包本地源命中'
                Assert-LogContains $logPath "本地源命中：组件 ClientMsi 使用布局目录文件 $clientFileName" $scenario '客户端包本地源命中'
                Assert-LogNotContains $logPath 'download from:' $scenario '全程零下载（本地源优先，AC-02）'
                $access = Read-AccessLog $accessLog
                Add-Result $scenario ($access.Count -eq 0) "测试源零命中（访问日志 $(@($access).Count) 条——源在线但从未被请求，零外网等价取证）"
            }
            'I2' {
                # 篡改拦截（断网 fail-closed，AC-04）：布局副本篡改组件一字节 + 测试源停机（外网不可达等价）
                if (-not (Test-Path -LiteralPath (Join-Path $layoutDir $bundleExeName))) { Add-Result $scenario $false '前置缺失：请先运行 G'; continue }
                $tamperedDir = Join-Path $offlineDir 'layout-tampered'
                if (Test-Path -LiteralPath $tamperedDir) { Remove-Item -LiteralPath $tamperedDir -Recurse -Force }
                Copy-Item -LiteralPath $layoutDir -Destination $tamperedDir -Recurse
                $tamperedFile = Join-Path $tamperedDir $serverFileName
                $bytes = [System.IO.File]::ReadAllBytes($tamperedFile)
                $bytes[[int]($bytes.Length / 2)] = $bytes[[int]($bytes.Length / 2)] -bxor 0xFF
                [System.IO.File]::WriteAllBytes($tamperedFile, $bytes)

                Stop-Job $serverJob -ErrorAction SilentlyContinue
                Remove-Job $serverJob -Force -ErrorAction SilentlyContinue
                Write-Host '  测试源已停机（断网模拟）——篡改组件 + 无可用外网源'
                Start-Sleep -Seconds 1

                $run = Invoke-WizardInstall $logPath 300 '' (Join-Path $tamperedDir $bundleExeName)
                Add-Result $scenario ($run.ExitCode -ne 0 -and $run.Terminal -eq 'failed') "exit=$($run.ExitCode) 终态=$($run.Terminal)（fail-closed，期望失败）"
                Assert-LogContains $logPath "本地源命中：组件 ServerMsi 使用布局目录文件 $serverFileName" $scenario '篡改文件经本地源获取（进入校验）'
                Assert-LogContains $logPath '校验失败' $scenario 'sha256 校验拦截（BA 失败分类日志）'
                Assert-LogNotContains $logPath "Verified acquired payload: ServerMsi" $scenario '篡改内容未通过校验（绝不落装）'
                Assert-LogContains $logPath '源耗尽' $scenario '断网换源失败 → 源耗尽（fail-closed）'

                # 恢复测试源（后续场景使用）
                $serverJob = Start-TestServer $Port $accessLog $testManifestPath $testLatestPath
                Start-Sleep -Seconds 1
                if ($serverJob.State -ne 'Running') { throw '测试源恢复失败' }
            }
            'I2b' {
                # 篡改拦截（在线换源重取，AC-04 另一口径）：EXE 于布局目录之外运行（引擎原生同目录本地源不参与），
                # 清单来源注入被篡改布局目录的本地清单 → 有效源序 = [篡改本地文件] ++ urls → 本地校验失败换 urls 重取干净副本
                if (-not (Test-Path -LiteralPath (Join-Path $layoutDir 'install-manifest.json'))) { Add-Result $scenario $false '前置缺失：请先运行 G'; continue }
                $tamperedDir = Join-Path $offlineDir 'layout-tampered-online'
                if (Test-Path -LiteralPath $tamperedDir) { Remove-Item -LiteralPath $tamperedDir -Recurse -Force }
                Copy-Item -LiteralPath $layoutDir -Destination $tamperedDir -Recurse
                $tamperedFile = Join-Path $tamperedDir $serverFileName
                $bytes = [System.IO.File]::ReadAllBytes($tamperedFile)
                $bytes[[int]($bytes.Length / 2)] = $bytes[[int]($bytes.Length / 2)] -bxor 0xFF
                [System.IO.File]::WriteAllBytes($tamperedFile, $bytes)

                $run = Invoke-WizardInstall $logPath 300 (Join-Path $tamperedDir 'install-manifest.json') $bundleExe
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)（换源重取干净副本后成功）"
                Assert-LogContains $logPath "本地源命中：组件 ServerMsi 使用布局目录文件 $serverFileName" $scenario '篡改文件经本地源获取'
                Assert-LogContains $logPath '校验失败' $scenario 'sha256 校验拦截（篡改内容不落装）'
                Assert-LogContains $logPath '多源回退：组件 ServerMsi 切换到第 2/2 个源' $scenario '本地源失败 → 换 urls（有效源序 [本地] ++ urls）'
                Assert-LogContains $logPath "download from: $serverUrl" $scenario 'urls 干净副本重取'
                $access = Read-AccessLog $accessLog
                Add-Result $scenario (@($access | Where-Object { $_ -like "*/main/$serverFileName" }).Count -ge 1) "重取命中测试源（访问日志含服务端包）"
            }
            'R1' {
                # 无布局目录在线安装回归（AC-03）：构建产物 EXE（无邻接清单）+ 测试源 URL 清单 → 按 urls 下载安装（现状行为）
                $run = Invoke-WizardInstall $logPath 180 $manifestUrl $bundleExe
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Assert-LogNotContains $logPath '检测到引导程序同目录安装清单' $scenario '无邻接清单不误报（默认稳定通道形态）'
                Assert-LogNotContains $logPath '本地源命中' $scenario '无本地源参与（纯 urls，行为与现状一致）'
                Assert-LogContains $logPath "download from: $serverUrl" $scenario '服务端包按 urls 下载'
                Assert-LogContains $logPath "download from: $clientUrl" $scenario '客户端包按 urls 下载'
            }
            default {
                Add-Result $scenario $false '未知场景标识'
            }
        }
    }
}
finally {
    # 收尾：停源、清注册与缓存（证据日志保留）
    Stop-Job $serverJob -ErrorAction SilentlyContinue
    Remove-Job $serverJob -Force -ErrorAction SilentlyContinue
    Remove-TestRegistration
    Clear-PackageCache
}

# ---------- 汇总 ----------
$summary = @(
    "LabelFrame 离线布局走查 · $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Bundle：$bundleExe（UpgradeCode $upgradeCode）；测试源 ：$Port；WiX $wixVersion",
    ''
) + $script:results + @(
    '',
    "结论：$($script:results.Count) 项断言，失败 $script:failed 项。",
    '断网实机取证（AC-02 实机口径：断网 VM / 实机 + 抓包）不在本脚本能力内——转待验收（恢复条件见 Issue #89）。'
)
$summary | Set-Content -LiteralPath (Join-Path $offlineDir 'summary.txt') -Encoding UTF8
Write-Host "`n========== 走查汇总（$($script:results.Count) 项断言，失败 $script:failed）==========" -ForegroundColor Cyan
$script:results | ForEach-Object { Write-Host "  $_" }
Write-Host "证据目录：$offlineDir"
if ($script:failed -gt 0) { exit 1 }
exit 0
