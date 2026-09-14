# LabelFrame 引导下载行为测试矩阵（迭代 61 / Issue #54「范围修订 v2」·DESIGN §6.10）
#
# 目的：本地 HTTP 测试源驱动 per-user 测试 Bundle 的引擎级行为断言——
#   S1 正常下载 / S2 主源 404 换源 / S3 主源超时换源 / S4a 坏哈希换源 / S4b 坏哈希单源拒绝 /
#   S5 中断续传（截断重传）/ S6 缓存命中 + 断网续装。
# 载体 = WiX Burn Bundle + 真实 BA（src/LabelFrame.Bootstrapper.Ba）：多源回退决策在 BA 的
#   CacheAcquireBegin/Complete（IEngine.SetDownloadSource），验证回退链路在引擎边界真实生效。
# 测试链（规避提权，#55 per-user 测试 Bundle 先例）：两 ExePackage（id=ServerMsi/ClientMsi，
#   SourceFile=PayloadTool 无参空操作退出 0），Compressed=no + DownloadUrl；条件消费问卷变量。
# 源行为按 URL 路由内嵌（服务无状态、连接串行处理）：/ok/ 正常、/404/ 不存在、/hang/ 无响应挂起、
#   /corrupt/ 篡改一字节（哈希不符）、/truncate/ 半量截断（连接中断）。
#
# 用法（仓库根目录，Windows PowerShell 5.1+）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-bundle-download-matrix.ps1
#   可选：-Scenarios 'S1,S2'（子集）/ -SkipBuild（复用已构建产物）/ -MainPort 8130 -MirrorPort 8131
# 证据：artifacts\bootstrapper-tests\matrix\<场景>.log（Burn 日志）+ summary.txt + 服务器访问日志。
# 临时产物不进仓（artifacts/ 已 gitignore）；本脚本随仓版本化（矩阵可复现）。S6 会停掉测试源，默认排最后。
param(
    [string]$WixPath = '',
    [int]$MainPort = 8130,
    [int]$MirrorPort = 8131,
    [string]$Scenarios = 'S1,S2,S3,S4a,S4b,S5,S6',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'artifacts\bootstrapper-tests'
$serveDir = Join-Path $staging 'serve'
$matrixDir = Join-Path $staging 'matrix'
$bundleName = 'LabelFrame 下载行为矩阵测试'
$baProcessName = 'LabelFrame.Bootstrapper.Ba'
$bundleProcessName = 'LabelFrame-DownloadMatrix'

# ---------- 前置：wix ----------
$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（wix.exe），请安装 WiX Toolset v7 或以 -WixPath 指定。" }
$wixVersion = & $wix --version
Write-Host "WiX：$wixVersion"

# ---------- Win32 UI 驱动（WinForms 向导：找窗口 / 设文本 / 点按钮）----------
# WinForms 控件使用注册类名（WindowsForms10.EDIT.app.… 等）——类名按子串匹配、标题按通配匹配。
Add-Type -Namespace MatrixUi -Name Native -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
"@

function Get-DescendantWindows([IntPtr]$parent) {
    # 平铺枚举全部后代（EnumChildWindows 递归语义）；跨页面控件更替，按需即时调用
    $items = New-Object System.Collections.Generic.List[object]
    $callback = [MatrixUi.Native+EnumProc]{
        param($hwnd, $lParam)
        $textBuilder = New-Object System.Text.StringBuilder 256
        [void][MatrixUi.Native]::GetWindowText($hwnd, $textBuilder, 256)
        $classBuilder = New-Object System.Text.StringBuilder 128
        [void][MatrixUi.Native]::GetClassName($hwnd, $classBuilder, 128)
        $items.Add([pscustomobject]@{ Handle = $hwnd; Class = $classBuilder.ToString(); Caption = $textBuilder.ToString() })
        return $true
    }
    [void][MatrixUi.Native]::EnumChildWindows($parent, $callback, [IntPtr]::Zero)
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
    [void][MatrixUi.Native]::SendMessage($hwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
}

function Set-EditText([IntPtr]$hwnd, [string]$text) {
    [void][MatrixUi.Native]::SendMessage($hwnd, 0x000C, [IntPtr]::Zero, $text) # WM_SETTEXT
}

# ---------- 本地 HTTP 测试源（TcpListener + 路由行为；访问日志落文件供断言）----------
function Start-TestServer([int]$Port, [string]$LogFile) {
    $job = Start-Job -ScriptBlock {
        param($Port, $LogFile, $ServeDir, $HangSeconds)
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
                    $rawPath = if ($parts.Count -ge 2) { $parts[1] } else { '/' }
                    $noQuery = ($rawPath -split '\?')[0]
                    Add-Content -LiteralPath $LogFile -Value "GET $noQuery"

                    $route = $null; $fileName = $null
                    if ($noQuery -match '^/(ok|404|hang|corrupt|truncate)/(.+)$') {
                        $route = $Matches[1]; $fileName = $Matches[2]
                    }

                    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::ASCII)
                    $writer.NewLine = "`r`n"
                    $writer.AutoFlush = $true

                    if ($null -eq $route -or $route -eq '404') {
                        $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                    }
                    elseif ($route -eq 'hang') {
                        # 无响应挂起：优先触发下载端接收超时；挂起上限后 404 兜底（防矩阵悬挂）
                        Start-Sleep -Seconds $HangSeconds
                        $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                    }
                    else {
                        $file = [System.IO.Path]::Combine($ServeDir, [System.Uri]::UnescapeDataString($fileName))
                        if (-not (Test-Path -LiteralPath $file)) {
                            $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                        }
                        else {
                            $bytes = [System.IO.File]::ReadAllBytes($file)
                            if ($route -eq 'corrupt') {
                                $bytes[[int]($bytes.Length / 2)] = $bytes[[int]($bytes.Length / 2)] -bxor 0xFF # 篡改一字节 → 哈希不符
                            }

                            $writer.Write("HTTP/1.1 200 OK`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
                            if ($route -eq 'truncate') {
                                # 半量截断后断开：连接中断（实发与 Content-Length 不符）
                                $stream.Write($bytes, 0, [int]($bytes.Length / 2))
                                $stream.Flush()
                            }
                            else {
                                $stream.Write($bytes, 0, $bytes.Length)
                                $stream.Flush()
                            }
                        }
                    }
                    $writer.Dispose()
                }
                catch { # 连接被下载端超时中止等——记录后继续服务下一个连接
                }
                finally { $client.Close() }
            }
        }
        finally { $listener.Stop() }
    } -ArgumentList $Port, $LogFile, $serveDir, 45
    return $job
}

function Read-AccessLog([string]$LogFile) {
    if (-not (Test-Path -LiteralPath $LogFile)) { return @() }
    return @(Get-Content -LiteralPath $LogFile | Where-Object { $_ })
}

# ---------- 构建测试 Bundle（per-user 双 ExePackage 链 + 真实 BA）----------
$mainBase = "http://127.0.0.1:$MainPort"
$mirrorBase = "http://127.0.0.1:$MirrorPort"
$serverFileName = 'lf-matrix-server.exe'
$clientFileName = 'lf-matrix-client.exe'
$cacheIdServer = 'LfDlMatrixServerMsi'
$cacheIdClient = 'LfDlMatrixClientMsi'
$upgradeCode = [guid]::NewGuid().ToString().ToUpperInvariant()
$bundleExe = Join-Path $staging 'LabelFrame-DownloadMatrix.exe'

if (-not $SkipBuild) {
    foreach ($dir in @($staging, $serveDir, $matrixDir, (Join-Path $staging 'ba'), (Join-Path $staging 'payloadtool'))) {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    Write-Host '发布 BA（net48 win-x64）与 PayloadTool（测试链包载体）…'
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path $staging 'ba') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.PayloadTool\LabelFrame.Bootstrapper.PayloadTool.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path $staging 'payloadtool') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'PayloadTool publish failed' }

    # 测试链载荷：PayloadTool 本体双份（无参调用 = 空操作退出 0）；客户端副本尾部追加标记字节（两包内容可区分）
    $payloadToolExe = Join-Path $staging 'payloadtool\LabelFrame.Bootstrapper.PayloadTool.exe'
    Copy-Item $payloadToolExe (Join-Path $serveDir $serverFileName) -Force
    Copy-Item $payloadToolExe (Join-Path $serveDir $clientFileName) -Force
    Add-Content -LiteralPath (Join-Path $serveDir $clientFileName) -Value 'm' -NoNewline

    # 测试 Bundle.wxs（临时产物不进仓；链机制复刻 Bundle.wxs 的下载语义，规避 MSI 与提权）。
    # 运行时探测变量置 1：测试链无 runtime 包，仅消费问卷变量。
    # PayloadTool 为 framework-dependent：伴生 DLL 以 PayloadGroup 内嵌（复刻生产落位包机制，§6.9）。
    $bundleWxs = Join-Path $staging 'BundleMatrix.wxs'
    $payloadGroupLines = @('    <PayloadGroup Id="PayloadToolDependencies">')
    foreach ($dep in (Get-ChildItem (Join-Path $staging 'payloadtool') -File | Where-Object { $_.Name -ne 'LabelFrame.Bootstrapper.PayloadTool.exe' } | Sort-Object Name)) {
        $payloadGroupLines += "      <Payload SourceFile=`"`$(var.PayloadToolDir)$($dep.Name)`" Compressed=`"yes`" />"
    }
    $payloadGroupLines += '    </PayloadGroup>'
    $wxsLines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- 下载行为矩阵测试 Bundle（自动生成，勿手工编辑；迭代 61 / #54）-->'
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
        '    <Variable Name="WebView2Installed" Type="numeric" Value="1" />'
        '    <Variable Name="WebUiTargetDir" Type="string" Value="" />'
        '    <Variable Name="PluginZebraTargetDir" Type="string" Value="" />'
    ) + $payloadGroupLines + @(
        '    <Chain>'
        "      <ExePackage Id=`"ServerMsi`" CacheId=`"$cacheIdServer`" SourceFile=`"`$(var.ServeDir)$serverFileName`""
        "                  Name=`"$serverFileName`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`" InstallCondition=`"InstallServer`""
        "                  DownloadUrl=`"$mainBase/ok/$serverFileName`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        "      <ExePackage Id=`"ClientMsi`" CacheId=`"$cacheIdClient`" SourceFile=`"`$(var.ServeDir)$clientFileName`""
        "                  Name=`"$clientFileName`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`" InstallCondition=`"InstallClient`""
        "                  DownloadUrl=`"$mainBase/ok/$clientFileName`">"
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
    # wix build 会在输出目录留包载荷的硬链接中间产物——必须清除，否则 Burn 以 Bundle 同目录为本地源
    # 直接 copy 跳过下载（S1 实测踩坑：copy from <输出目录>/lf-matrix-*.exe）
    foreach ($name in @($serverFileName, $clientFileName)) {
        Remove-Item -LiteralPath (Join-Path $staging $name) -Force -ErrorAction SilentlyContinue
    }
    Write-Host "测试 Bundle 构建完成：$bundleExe（$([Math]::Round((Get-Item $bundleExe).Length / 1MB, 2)) MB）"
}
else {
    if (-not (Test-Path $bundleExe)) { throw "-SkipBuild 但未找到 $bundleExe，请先完整运行一次。" }
    if (-not (Test-Path (Join-Path $serveDir $serverFileName))) { throw "-SkipBuild 但测试载荷缺失（$serveDir），请先完整运行一次。" }
}

# ---------- 测试清单（manifest urls 决定源序与回退链；sha256 / sizeBytes 按本地产物实测）----------
function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-TestManifest([string]$Path, [string[]]$ServerUrls, [string[]]$ClientUrls) {
    $serverFile = Join-Path $serveDir $serverFileName
    $clientFile = Join-Path $serveDir $clientFileName
    $manifestJson = (@(
        '{'
        '  "schemaVersion": 1,'
        '  "labelframeVersion": "0.0.1",'
        '  "generatedAt": "2026-09-14T00:00:00Z",'
        '  "components": ['
        '    {'
        '      "id": "server-msi", "type": "msi", "version": "0.0.1", "dependsOn": [],'
        ('      "urls": [' + (($ServerUrls | ForEach-Object { '"' + $_ + '"' }) -join ', ') + '],')
        ('      "sha256": "' + (Get-Sha256 $serverFile) + '",')
        ('      "sizeBytes": ' + (Get-Item $serverFile).Length + ',')
        '      "silentArgs": "", "topologies": ["standalone"], "notes": "矩阵测试 · 服务端包"'
        '    },'
        '    {'
        '      "id": "client-msi", "type": "msi", "version": "0.0.1", "dependsOn": [],'
        ('      "urls": [' + (($ClientUrls | ForEach-Object { '"' + $_ + '"' }) -join ', ') + '],')
        ('      "sha256": "' + (Get-Sha256 $clientFile) + '",')
        ('      "sizeBytes": ' + (Get-Item $clientFile).Length + ',')
        '      "silentArgs": "", "topologies": ["standalone"], "notes": "矩阵测试 · 客户端包"'
        '    }'
        '  ]'
        '}'
    ) -join "`n") + "`n"
    [System.IO.File]::WriteAllText($Path, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
}

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
# -InterruptAfterRequest：看到指定路径的请求进入访问日志后强制中断进程树（模拟安装中途断电 / 掉线：
#  无正常回滚——Burn 保留 InProgress 注册与已缓存包，构成 run2 断网续装的前提）。
function Invoke-WizardInstall([string]$ManifestPath, [string]$LogPath, [int]$TimeoutSeconds, [string]$InterruptAfterRequest = '') {
    $proc = Start-Process -FilePath $bundleExe -ArgumentList @('-l', $LogPath) -PassThru
    try {
        # 向导窗口在 BA 子进程（out-of-proc）：按进程名 + 标题匹配
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
        [void][MatrixUi.Native]::SetForegroundWindow($mainHwnd)

        # 欢迎页：清单来源输入本地测试清单 → 加载（成功自动进下一页）
        $sourceEdit = Find-ChildByCaption $mainHwnd 'EDIT' '*'
        if (-not $sourceEdit) { throw '未找到清单来源输入框' }
        Set-EditText $sourceEdit.Handle $ManifestPath
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

        # 等终态：完成页（安装完成）或失败报告（重试按钮出现）；或指定请求出现后强制中断
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $terminal = 'timeout'
        while ((Get-Date) -lt $deadline) {
            if ($proc.HasExited) { $terminal = 'exited'; break }
            if ((Find-ChildByCaption $mainHwnd 'STATIC' '安装完成')) { $terminal = 'complete'; break }
            if ((Find-ChildByCaption $mainHwnd 'BUTTON' '重试*')) { $terminal = 'failed'; break }
            if ($InterruptAfterRequest -and ((Read-AccessLog $mainAccessLog) -match [regex]::Escape($InterruptAfterRequest))) {
                # 请求已挂起（如 /hang/ 无响应下载中）——杀进程树模拟中断
                Start-Sleep -Seconds 4
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                Get-Process -Name $baProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                $proc.WaitForExit(15000) | Out-Null
                $terminal = 'interrupted'
                break
            }
            Start-Sleep -Milliseconds 500
        }
        if ($terminal -eq 'timeout') { throw "场景超时（${TimeoutSeconds}s）未到终态" }

        # 关闭向导（终态后允许关闭；BA 以 Apply 状态为退出码）
        if (-not $proc.HasExited) {
            [void][MatrixUi.Native]::SendMessage($mainHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
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

# ---------- 启动测试源 ----------
$mainAccessLog = Join-Path $matrixDir 'access-main.log'
$mirrorAccessLog = Join-Path $matrixDir 'access-mirror.log'
$serverMain = Start-TestServer $MainPort $mainAccessLog
$serverMirror = Start-TestServer $MirrorPort $mirrorAccessLog
Start-Sleep -Seconds 1
if ($serverMain.State -ne 'Running' -or $serverMirror.State -ne 'Running') {
    throw "测试源启动失败（main=$($serverMain.State) mirror=$($serverMirror.State)）——检查端口占用。"
}
Write-Host "测试源就绪：main=:$MainPort mirror=:$MirrorPort（访问日志 $matrixDir）"

Remove-TestRegistration
Clear-PackageCache

$selected = $Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }

try {
    foreach ($scenario in $selected) {
        Write-Host "`n========== 场景 $scenario ==========" -ForegroundColor Cyan
        $logPath = Join-Path $matrixDir "$scenario.log"
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        Clear-PackageCache # 场景隔离（S6 的 run2 在场景内部续用 run1 缓存，不受影响）
        Remove-Item -LiteralPath $mainAccessLog, $mirrorAccessLog -Force -ErrorAction SilentlyContinue

        switch ($scenario) {
            'S1' {
                # 正常下载：主源双包直下
                $manifest = Join-Path $staging 'manifest-s1.json'
                New-TestManifest $manifest @("$mainBase/ok/$serverFileName") @("$mainBase/ok/$clientFileName")
                $run = Invoke-WizardInstall $manifest $logPath 180
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Assert-LogContains $logPath "download from: $mainBase/ok/$serverFileName" $scenario '主源下载服务端包'
                Assert-LogContains $logPath "download from: $mainBase/ok/$clientFileName" $scenario '主源下载客户端包'
            }
            'S2' {
                # 主源 404 换源：server 双源（main 404 → mirror ok），client 主源直下
                $manifest = Join-Path $staging 'manifest-s2.json'
                New-TestManifest $manifest @("$mainBase/404/$serverFileName", "$mirrorBase/ok/$serverFileName") @("$mainBase/ok/$clientFileName")
                $run = Invoke-WizardInstall $manifest $logPath 240
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Assert-LogContains $logPath '多源回退：组件 ServerMsi 切换到第 2/2 个源' $scenario 'BA 换源日志'
                Assert-LogContains $logPath "download from: $mirrorBase/ok/$serverFileName" $scenario '镜像源下载服务端包'
                $main = Read-AccessLog $mainAccessLog
                Add-Result $scenario (@($main | Where-Object { $_ -like "*/404/$serverFileName" }).Count -ge 1) "主源 404 路由被请求（main 访问日志 $(@($main).Count) 条）"
            }
            'S3' {
                # 主源超时换源：server main /hang/ 无响应 → 接收超时（或挂起上限 404 兜底）→ mirror 补救
                $manifest = Join-Path $staging 'manifest-s3.json'
                New-TestManifest $manifest @("$mainBase/hang/$serverFileName", "$mirrorBase/ok/$serverFileName") @("$mainBase/ok/$clientFileName")
                $run = Invoke-WizardInstall $manifest $logPath 420
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Assert-LogContains $logPath '多源回退：组件 ServerMsi 切换到第 2/2 个源' $scenario 'BA 换源日志'
                Assert-LogContains $logPath "download from: $mirrorBase/ok/$serverFileName" $scenario '镜像源下载服务端包'
            }
            'S4a' {
                # 坏哈希换源：main /corrupt/ 篡改一字节 → 校验失败 → 换源重取镜像干净副本
                $manifest = Join-Path $staging 'manifest-s4a.json'
                New-TestManifest $manifest @("$mainBase/corrupt/$serverFileName", "$mirrorBase/ok/$serverFileName") @("$mainBase/ok/$clientFileName")
                $run = Invoke-WizardInstall $manifest $logPath 300
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                $verifyFail = Select-String -LiteralPath $logPath -Pattern '0x80091007|failed to verify|Failed to verify' | Select-Object -First 1
                Add-Result $scenario ([bool]$verifyFail) '校验失败引擎证据（0x80091007 / failed to verify）'
                Assert-LogContains $logPath '多源回退：组件 ServerMsi 切换到第 2/2 个源' $scenario '坏哈希后 BA 换源日志'
                Assert-LogContains $logPath "download from: $mirrorBase/ok/$serverFileName" $scenario '镜像源重取'
            }
            'S4b' {
                # 坏哈希单源拒绝：仅 main /corrupt/——引擎重取额度内同源重试仍坏 → 失败（fail-closed 不装不明文件）
                $manifest = Join-Path $staging 'manifest-s4b.json'
                New-TestManifest $manifest @("$mainBase/corrupt/$serverFileName") @("$mainBase/ok/$clientFileName")
                $run = Invoke-WizardInstall $manifest $logPath 300
                Add-Result $scenario ($run.ExitCode -ne 0 -and $run.Terminal -eq 'failed') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                $corruptGets = @((Read-AccessLog $mainAccessLog) | Where-Object { $_ -like "*/corrupt/$serverFileName" }).Count
                Add-Result $scenario ($corruptGets -ge 2) "坏源被重试获取 ${corruptGets} 次（引擎重取额度内仍失败）"
                Assert-LogContains $logPath '校验失败' $scenario 'BA 失败分类（校验失败）日志'
            }
            'S5' {
                # 中断续传：main /truncate/ 半量断开 → 获取失败 → mirror 完整重传（Burn 载体下「续传」= 失败后重试 / 换源重新获取）
                $manifest = Join-Path $staging 'manifest-s5.json'
                New-TestManifest $manifest @("$mainBase/truncate/$serverFileName", "$mirrorBase/ok/$serverFileName") @("$mainBase/ok/$clientFileName")
                $run = Invoke-WizardInstall $manifest $logPath 300
                Add-Result $scenario ($run.ExitCode -eq 0 -and $run.Terminal -eq 'complete') "exit=$($run.ExitCode) 终态=$($run.Terminal)"
                Assert-LogContains $logPath '多源回退：组件 ServerMsi 切换到第 2/2 个源' $scenario 'BA 换源日志'
                Assert-LogContains $logPath "download from: $mirrorBase/ok/$serverFileName" $scenario '镜像源完整重传'
            }
            'S6' {
                # 缓存命中 + 断网续装（Burn 载体语义）：
                #   run1 = 中断安装：server 主源正常下载校验入缓存；client 走 /hang/ 无响应下载中强制杀进程
                #          （无正常回滚：注册 InProgress + 已缓存包保留——缓存阶段失败会被引擎回滚清缓存，中断才会保留）；
                #   run2 = 断网续装：两源停机——server 走包缓存零网络请求（缓存命中跳过获取），
                #          client 逐源尝试均连接拒绝 → 换源 2/2 → 源耗尽失败报告（分类 = 源不可达）。
                $manifest = Join-Path $staging 'manifest-s6.json'
                New-TestManifest $manifest @("$mainBase/ok/$serverFileName") @("$mainBase/hang/$clientFileName", "$mirrorBase/hang/$clientFileName")
                $log1 = Join-Path $matrixDir 'S6-run1.log'
                $run1 = Invoke-WizardInstall $manifest $log1 180 -InterruptAfterRequest "/hang/$clientFileName"
                Add-Result 'S6.run1' ($run1.Terminal -eq 'interrupted') "run1 终态=$($run1.Terminal)（挂起下载中强制中断，无正常回滚）"
                $serverGets = @((Read-AccessLog $mainAccessLog) | Where-Object { $_ -like "*/ok/$serverFileName" }).Count
                Add-Result 'S6.run1' ($serverGets -ge 1) "服务端包经网络获取 ${serverGets} 次"
                Assert-LogContains $log1 'Verified acquired payload: ServerMsi' 'S6.run1' '服务端包校验入缓存（引擎 verify 日志）'

                # 断网：停两台测试源
                Stop-Job $serverMain, $serverMirror -ErrorAction SilentlyContinue
                Remove-Job $serverMain, $serverMirror -Force -ErrorAction SilentlyContinue
                Write-Host '  测试源已全部停机（断网模拟）——run2 复用 run1 中断保留的包缓存续装'
                Start-Sleep -Seconds 1

                $log2 = Join-Path $matrixDir 'S6-run2.log'
                $run2 = Invoke-WizardInstall $manifest $log2 300
                Add-Result 'S6.run2' ($run2.ExitCode -ne 0 -and $run2.Terminal -eq 'failed') "run2 exit=$($run2.ExitCode) 终态=$($run2.Terminal)"
                $downloadServer = Select-String -LiteralPath $log2 -Pattern "download from:.*$serverFileName"
                Add-Result 'S6.run2' (-not $downloadServer) '断网续装零下载：服务端包无 download-from（缓存命中跳过获取）'
                Assert-LogContains $log2 '多源回退：组件 ClientMsi 切换到第 2/2 个源' 'S6.run2' '客户端包断网换源日志（两源均不可达）'
                Assert-LogContains $log2 '源耗尽' 'S6.run2' '源耗尽提示日志'
                Assert-LogContains $log2 '分类 = SourceUnreachable' 'S6.run2' '断网失败分类（源不可达）'
                Copy-Item $log2 $logPath -Force # 场景主日志 = run2（run1/run2 双份证据均保留）
            }
            default {
                Add-Result $scenario $false '未知场景标识'
            }
        }
    }
}
finally {
    # 收尾：停源、清注册与缓存（证据日志保留）
    Stop-Job $serverMain, $serverMirror -ErrorAction SilentlyContinue
    Remove-Job $serverMain, $serverMirror -Force -ErrorAction SilentlyContinue
    Remove-TestRegistration
    Clear-PackageCache
}

# ---------- 汇总 ----------
$summary = @(
    "LabelFrame 下载行为测试矩阵 · $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Bundle：$bundleExe（UpgradeCode $upgradeCode）",
    "测试源：main=:$MainPort mirror=:$MirrorPort；WiX $wixVersion",
    ''
) + $script:results + @(
    '',
    "结论：$($script:results.Count) 项断言，失败 $script:failed 项。"
)
$summary | Set-Content -LiteralPath (Join-Path $matrixDir 'summary.txt') -Encoding UTF8
Write-Host "`n========== 矩阵汇总（$($script:results.Count) 项断言，失败 $script:failed）==========" -ForegroundColor Cyan
$script:results | ForEach-Object { Write-Host "  $_" }
Write-Host "证据目录：$matrixDir"
if ($script:failed -gt 0) { exit 1 }
exit 0
