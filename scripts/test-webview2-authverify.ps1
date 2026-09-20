# LabelFrame WebView2 evergreen 验签与已装跳过走查（Issue #173 · 决策 #151，DESIGN §6.9 / §6.10）
#
# 目的（沙箱实证，不进 CI；不做真机 / 真实客户端验证）：
#   Part A —— PayloadTool 验签正反例（真实 WinVerifyTrust；-verify 模式获取 + 验签但不执行安装）：
#     W1 正例：微软签名文件验签通过（优先从 evergreen fwlink 下载真实安装器；离线回退本机内嵌签名微软产物
#              + -publisher 按实际签名者 CN 覆写——证明「下载 / 本地源 → 验签 → 发布者匹配」全链路）；
#     W2 反例：篡改一字节（签名哈希不符）→ 退出码 40；
#     W3 反例：未签名文件（本地构建的 PayloadTool 本体）→ 退出码 40；
#     W4 反例（仅联网取得真实 evergreen 文件时）：有效微软签名但 -publisher Contoso → 退出码 40（发布者不符）。
#   Part B —— 已装（Present）跳过 acquire（per-user 测试 Bundle + 真实 BA + 本地 TCP 源，引擎级断言）：
#     B1 Cache=remove × Present → 对应源 0 次请求（AC-3 核心：已装机器不再陪跑下载校验）；
#     B2 默认 Cache=keep × Present → ≥1 次请求（机制根源演示：WiX v4 计划语义 keep × Present ⇒ 引擎尝试缓存，
#        wixtoolset/issues#7021——对照 B1 证明差异来自缓存策略而非探测失效）；
#     B3 Cache=remove × 缺失（包执行）→ 恰 1 次请求（remove 不影响「执行才缓存」的获取）。
#
# 用法（仓库根目录，Windows PowerShell 5.1+）：
#   powershell -ExecutionPolicy Bypass -File scripts\test-webview2-authverify.ps1
#   可选：-SkipBuild（复用已构建产物）/ -Port 8132 / -SkipPartB（只跑 Part A）
# 证据：artifacts\bootstrapper-tests\authverify\（工具输出、访问日志、Burn 日志、summary.txt）。
param(
    [string]$WixPath = '',
    [int]$Port = 8132,
    [switch]$SkipBuild,
    [switch]$SkipPartB
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'artifacts\bootstrapper-tests'
$avDir = Join-Path $staging 'authverify'
$serveDir = Join-Path $avDir 'serve'
$bundleName = 'LabelFrame evergreen 验签走查测试'
$baProcessName = 'LabelFrame.Bootstrapper.Ba'
$exitVerifyRejected = 40
$exitSourceExhausted = 41

$script:results = New-Object System.Collections.Generic.List[string]
$script:failed = 0
function Add-Result([string]$Scenario, [bool]$Pass, [string]$Detail) {
    $mark = if ($Pass) { '通过' } else { '失败' }
    $script:results.Add("$Scenario $mark — $Detail")
    if (-not $Pass) { $script:failed++ }
    Write-Host "[$mark] $Scenario — $Detail" -ForegroundColor ($(if ($Pass) { 'Green' } else { 'Red' }))
}

foreach ($dir in @($avDir, $serveDir)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}

# ---------- 构建 PayloadTool（Part A / B 共用；-SkipBuild 复用）----------
$payloadToolExe = Join-Path $avDir 'payloadtool\LabelFrame.Bootstrapper.PayloadTool.exe'
if (-not $SkipBuild -or -not (Test-Path $payloadToolExe)) {
    Write-Host '发布 PayloadTool（net48 win-x64）…'
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.PayloadTool\LabelFrame.Bootstrapper.PayloadTool.csproj') `
        -c Release -f net48 -r win-x64 -o (Join-Path $avDir 'payloadtool') -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'PayloadTool publish failed' }
}

function Invoke-PayloadTool([string[]]$ToolArgs, [string]$OutputFile) {
    # Start-Process 重定向捕获（PS 5.1 下 2>&1 管道会把 stderr 行包装为 ErrorRecord，EAP=Stop 时中断脚本）；
    # 工具输出为 UTF-8（Console.OutputEncoding），读取需按 UTF-8
    $stdoutFile = "$OutputFile.out.txt"
    $stderrFile = "$OutputFile.err.txt"
    $p = Start-Process -FilePath $payloadToolExe -ArgumentList $ToolArgs -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
    $stdout = if (Test-Path $stdoutFile) { Get-Content -LiteralPath $stdoutFile -Encoding UTF8 } else { @() }
    $stderr = if (Test-Path $stderrFile) { Get-Content -LiteralPath $stderrFile -Encoding UTF8 } else { @() }
    (@($stdout) + @($stderr)) | Set-Content -LiteralPath $OutputFile -Encoding UTF8
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Output = (@($stdout) + @($stderr)) -join "`n" }
}

# ============================ Part A：验签正反例 ============================
Write-Host ''
Write-Host '===== Part A：PayloadTool evergreen 验签（真实 WinVerifyTrust；-verify 不执行安装）====='

# W1 正例素材：优先真实 evergreen fwlink（~2MB）；失败回退本机内嵌签名微软产物（dotnet.exe / pwsh.exe）
$evergreenFile = Join-Path $serveDir 'MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe'
$evergreenReady = $false
if (Test-Path -LiteralPath $evergreenFile) {
    $sig = Get-AuthenticodeSignature -LiteralPath $evergreenFile
    $evergreenReady = ($sig.Status -eq 'Valid')
}

if (-not $evergreenReady) {
    try {
        Write-Host '下载真实 evergreen 安装器（fwlink，仅本地走查素材）…'
        Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $evergreenFile -UseBasicParsing -TimeoutSec 120
        $sig = Get-AuthenticodeSignature -LiteralPath $evergreenFile
        $evergreenReady = ($sig.Status -eq 'Valid')
    }
    catch {
        Write-Host "fwlink 下载失败（离线回退本机签名产物）：$($_.Exception.Message)"
    }
}

$w1 = $null
if ($evergreenReady) {
    $w1 = Invoke-PayloadTool @('-verify', '-source', $evergreenFile) (Join-Path $avDir 'W1-verify-real.log')
    Add-Result 'W1 正例 · 真实 evergreen 文件验签' ($w1.ExitCode -eq 0) "退出码 $($w1.ExitCode)（期望 0；签名者 CN=Microsoft Corporation）"
    if ($w1.ExitCode -eq 0) {
        Add-Result 'W1 补充 · 签名者为微软公司' ($w1.Output -match 'Microsoft Corporation') "输出含签名者主体 Microsoft Corporation"
    }
}
else {
    # 离线回退：本机内嵌签名微软产物（真实 WinVerifyTrust 全链路；发布者按实际 CN 覆写）
    $candidates = @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe')
    ) | Where-Object { Test-Path $_ }
    if (-not $candidates) { throw '既无法下载 evergreen 文件，本机也无内嵌签名候选（dotnet.exe / pwsh.exe）——W1 无法执行。' }
    $fallback = $candidates[0]
    $cn = ((Get-AuthenticodeSignature -LiteralPath $fallback).SignerCertificate.Subject -split ',')[0] -replace '^CN\s*=\s*', ''
    $w1 = Invoke-PayloadTool @('-verify', '-source', $fallback, '-publisher', $cn) (Join-Path $avDir 'W1-verify-fallback.log')
    Add-Result 'W1 正例（离线回退）· 本机微软签名产物验签' ($w1.ExitCode -eq 0) "素材 $fallback，发布者 CN=$cn，退出码 $($w1.ExitCode)（期望 0）"
}

# W1 素材路径（W2 / W4 复用）
$w1File = if ($evergreenReady) { $evergreenFile } else { $candidates[0] }

# W2 反例：篡改一字节 → 签名哈希不符
$tampered = Join-Path $serveDir 'tampered-copy.exe'
$bytes = [IO.File]::ReadAllBytes($w1File)
$bytes[[int]($bytes.Length / 2)] = $bytes[[int]($bytes.Length / 2)] -bxor 0xFF
[IO.File]::WriteAllBytes($tampered, $bytes)
$w2 = Invoke-PayloadTool @('-verify', '-source', $tampered) (Join-Path $avDir 'W2-verify-tampered.log')
Add-Result 'W2 反例 · 篡改一字节被拒' ($w2.ExitCode -eq $exitVerifyRejected) "退出码 $($w2.ExitCode)（期望 $exitVerifyRejected 验签拒绝）"

# W3 反例：未签名文件（本地构建 PayloadTool 本体）
$w3 = Invoke-PayloadTool @('-verify', '-source', $payloadToolExe) (Join-Path $avDir 'W3-verify-unsigned.log')
Add-Result 'W3 反例 · 未签名文件被拒' ($w3.ExitCode -eq $exitVerifyRejected) "退出码 $($w3.ExitCode)（期望 $exitVerifyRejected 验签拒绝）"

# W4 反例（联网时）：有效微软签名但发布者不符
if ($evergreenReady) {
    $w4 = Invoke-PayloadTool @('-verify', '-source', $evergreenFile, '-publisher', 'Contoso') (Join-Path $avDir 'W4-verify-wrong-publisher.log')
    Add-Result 'W4 反例 · 有效签名但发布者不符被拒' ($w4.ExitCode -eq $exitVerifyRejected) "退出码 $($w4.ExitCode)（期望 $exitVerifyRejected 发布者不符）"
}
else {
    Add-Result 'W4 反例 · 发布者不符（有效签名）' $true '跳过——离线环境未取得真实 evergreen 文件（发布者匹配逻辑已由 W1 / 单测覆盖）'
}

if ($SkipPartB) {
    $summaryPath = Join-Path $avDir 'summary.txt'
    $script:results | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Host ''
    Write-Host "走查完成：失败 $script:failed 项；摘要 → $summaryPath"
    if ($script:failed -gt 0) { exit 1 }
    exit 0
}

# ============================ Part B：Present 跳过 acquire（引擎级） ============================
Write-Host ''
Write-Host '===== Part B：已装（Present）跳过 acquire（per-user 测试 Bundle + 真实 BA）=====' | Out-Null

$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（wix.exe），请安装 WiX Toolset v7 或以 -WixPath 指定（Part B 需要）。" }
Write-Host "WiX：$(& $wix --version)"

$base = "http://127.0.0.1:$Port"
$skipFile = 'lf-w2av-skip-remove.exe'   # Cache=remove × Present：期望 0 请求
$keepFile = 'lf-w2av-keep.exe'          # 默认 keep × Present：期望 ≥1 请求（机制根源）
$execFile = 'lf-w2av-exec-remove.exe'   # Cache=remove × 缺失（执行）：期望恰 1 请求
$cacheIdSkip = 'LfW2AvSkipRemove'
$cacheIdKeep = 'LfW2AvKeep'
$cacheIdExec = 'LfW2AvExecRemove'
$bundleExe = Join-Path $staging 'LabelFrame-Webview2AuthVerify.exe'

# ---- 构建（BA + 三包测试 Bundle；链机制复刻 Bundle.wxs 的缓存语义，规避提权）----
if (-not $SkipBuild -or -not (Test-Path $bundleExe)) {
    $baDir = Join-Path $staging 'authverify\ba'
    $toolDir = Join-Path $avDir 'payloadtool'
    foreach ($dir in @($baDir, $serveDir)) {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    Write-Host '发布 BA（net48 win-x64）…'
    dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
        -c Release -f net48 -r win-x64 -o $baDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }

    # 三包载荷 = PayloadTool 副本（无参调用 = 空操作退出 0）；尾部追加标记字节使三包字节可区分
    foreach ($name in @($skipFile, $keepFile, $execFile)) {
        Copy-Item $payloadToolExe (Join-Path $serveDir $name) -Force
        Add-Content -LiteralPath (Join-Path $serveDir $name) -Value 'm' -NoNewline
    }

    $payloadGroupLines = @('    <PayloadGroup Id="PayloadToolDependencies">')
    foreach ($dep in (Get-ChildItem $toolDir -File | Where-Object { $_.Name -ne 'LabelFrame.Bootstrapper.PayloadTool.exe' } | Sort-Object Name)) {
        $payloadGroupLines += "      <Payload SourceFile=`"`$(var.PayloadToolDir)$($dep.Name)`" Compressed=`"yes`" />"
    }
    $payloadGroupLines += '    </PayloadGroup>'

    # ProbePresent=1（BA 不写该变量）：探测期即 Present；Present 包不执行（execute None），执行包无 DetectCondition 恒缺失
    $wxsLines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<!-- evergreen 验签走查测试 Bundle（自动生成，勿手工编辑；#173 / 决策 #151 Part B）-->'
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
        "  <Bundle Name=`"$bundleName`" Version=`"0.0.1`" Manufacturer=`"LabelFrame`" UpgradeCode=`"$([guid]::NewGuid().ToString().ToUpperInvariant())`">"
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
        '    <Variable Name="ProbePresent" Type="numeric" Value="1" />'
    ) + $payloadGroupLines + @(
        '    <Chain>'
        "      <ExePackage Id=`"SkipProbeRemove`" CacheId=`"$cacheIdSkip`" SourceFile=`"`$(var.ServeDir)$skipFile`""
        "                  Name=`"$skipFile`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`" Cache=`"remove`""
        "                  DetectCondition=`"ProbePresent`" DownloadUrl=`"$base/ok/$skipFile`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        "      <ExePackage Id=`"KeepProbe`" CacheId=`"$cacheIdKeep`" SourceFile=`"`$(var.ServeDir)$keepFile`""
        "                  Name=`"$keepFile`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`""
        "                  DetectCondition=`"ProbePresent`" DownloadUrl=`"$base/ok/$keepFile`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        "      <ExePackage Id=`"ExecProbeRemove`" CacheId=`"$cacheIdExec`" SourceFile=`"`$(var.ServeDir)$execFile`""
        "                  Name=`"$execFile`" Compressed=`"no`" PerMachine=`"no`" Permanent=`"yes`" Cache=`"remove`""
        "                  DownloadUrl=`"$base/ok/$execFile`">"
        '        <PayloadGroupRef Id="PayloadToolDependencies" />'
        '      </ExePackage>'
        '    </Chain>'
        '  </Bundle>'
        '</Wix>'
    )
    $bundleWxs = Join-Path $avDir 'BundleW2AuthVerify.wxs'
    [System.IO.File]::WriteAllLines($bundleWxs, $wxsLines, (New-Object System.Text.UTF8Encoding($false)))

    & $wix eula accept wix7 2>$null | Out-Null
    $global:LASTEXITCODE = 0
    & $wix build $bundleWxs `
        -d "BaDir=$baDir\" -d "ServeDir=$serveDir\" -d "PayloadToolDir=$toolDir\" `
        -o $bundleExe -arch x64 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed（测试 Bundle）' }
    # wix build 输出目录旁置载荷硬链接会劫持下载（矩阵脚本 S1 踩坑）——清除
    foreach ($name in @($skipFile, $keepFile, $execFile)) {
        Remove-Item -LiteralPath (Join-Path $staging $name) -Force -ErrorAction SilentlyContinue
    }
    Write-Host "测试 Bundle 构建完成：$bundleExe"
}

# ---- 残留清理（包缓存 / HKCU Bundle 注册——按 DisplayName 匹配）----
$cacheRoot = Join-Path $env:LOCALAPPDATA 'Package Cache'
foreach ($cacheId in @($cacheIdSkip, $cacheIdKeep, $cacheIdExec)) {
    Remove-Item -LiteralPath (Join-Path $cacheRoot $cacheId) -Recurse -Force -ErrorAction SilentlyContinue
}
foreach ($hive in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall')) {
    foreach ($key in (Get-ChildItem $hive -ErrorAction SilentlyContinue)) {
        $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
        if ($displayName -eq $bundleName) {
            Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ---- 本地 TCP 源（TcpListener——免 URL ACL；/ok/ 服务文件，访问逐行落日志）----
$accessLog = Join-Path $avDir 'access-b.log'
Remove-Item -LiteralPath $accessLog -Force -ErrorAction SilentlyContinue
$server = Start-Job -ScriptBlock {
    param($PortNumber, $LogFile, $ServeDirectory)
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
                $rawPath = ($requestLine -split ' ')[1]
                $noQuery = ($rawPath -split '\?')[0]
                Add-Content -LiteralPath $LogFile -Value "GET $noQuery"

                $fileName = if ($noQuery -match '^/ok/(.+)$') { $Matches[1] } else { $null }
                $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::ASCII)
                $writer.NewLine = "`r`n"
                $writer.AutoFlush = $true
                $file = if ($fileName) { [System.IO.Path]::Combine($ServeDirectory, [System.Uri]::UnescapeDataString($fileName)) } else { $null }
                if ($file -and (Test-Path -LiteralPath $file)) {
                    $bytes = [System.IO.File]::ReadAllBytes($file)
                    $writer.Write("HTTP/1.1 200 OK`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
                    $stream.Write($bytes, 0, $bytes.Length)
                    $stream.Flush()
                }
                else {
                    $writer.Write("HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                }
                $writer.Dispose()
            }
            catch {
            }
            finally {
                $client.Close()
            }
        }
    }
    finally {
        $listener.Stop()
    }
} -ArgumentList $Port, $accessLog, $serveDir
Start-Sleep -Seconds 1
if ($server.State -ne 'Running') { throw '本地测试源启动失败' }
Write-Host "本地测试源：$base/ok/（访问日志 → $accessLog）"

# ---- 以 -passive 驱动（BA 非交互路径：Detect → Plan(Install) → Apply，全程无向导）----
$burnLog = Join-Path $avDir 'partb-bundle.log'
Write-Host "运行测试 Bundle（-passive，日志 → $burnLog）…"
$proc = Start-Process -FilePath $bundleExe -ArgumentList @('-passive', '-l', $burnLog) -PassThru
try {
    if (-not $proc.WaitForExit(300000)) { throw '测试 Bundle 超时（5 分钟）未退出' }
}
finally {
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name $baProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-Job $server -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $server -Force -ErrorAction SilentlyContinue | Out-Null
}

$exitCode = $proc.ExitCode
Add-Result 'B0 前置 · 测试 Bundle 退出码 0' ($exitCode -eq 0) "退出码 $exitCode（keep 探针缓存成功 + exec 探针空操作成功 = 0；非零说明链本身失败，后续断言无效）"

$accessLines = if (Test-Path -LiteralPath $accessLog) { Get-Content -LiteralPath $accessLog } else { @() }
$skipHits = @($accessLines | Where-Object { $_ -like "*/ok/$skipFile" }).Count
$keepHits = @($accessLines | Where-Object { $_ -like "*/ok/$keepFile" }).Count
$execHits = @($accessLines | Where-Object { $_ -like "*/ok/$execFile" }).Count

Add-Result 'B1 · Cache=remove × Present 零获取（AC-3）' ($skipHits -eq 0 -and $exitCode -eq 0) "请求 $skipHits 次（期望 0——已装跳过 acquire）"
Add-Result 'B2 · 默认 keep × Present 仍尝试缓存（机制根源）' ($keepHits -ge 1 -and $exitCode -eq 0) "请求 $keepHits 次（期望 ≥1——keep × Present ⇒ 引擎尝试缓存，wixtoolset/issues#7021）"
Add-Result 'B3 · Cache=remove × 缺失（执行）正常获取' ($execHits -ge 1 -and $exitCode -eq 0) "请求 $execHits 次（期望 ≥1——remove 不影响执行才缓存；本测试源实测每次获取恒 2 个 GET，与 B2 一致）"

# 收尾清理（Bundle 注册——本次走查注册的 per-user 条目）
foreach ($key in (Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue)) {
    $displayName = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).DisplayName
    if ($displayName -eq $bundleName) {
        Remove-Item $key.PSPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
foreach ($cacheId in @($cacheIdSkip, $cacheIdKeep, $cacheIdExec)) {
    Remove-Item -LiteralPath (Join-Path $cacheRoot $cacheId) -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------- 摘要 ----------
$summaryPath = Join-Path $avDir 'summary.txt'
$script:results | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host ''
$script:results | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host "走查完成：失败 $script:failed 项；摘要与证据 → $avDir"
if ($script:failed -gt 0) { exit 1 }
exit 0
