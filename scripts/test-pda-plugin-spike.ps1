# PDA 外置插件通道 Spike 走查脚本（迭代 96 / Issue #185 AC-02，决策 #156，DESIGN §6.8「跨端格式与 PDA 通道」）
# 目标：Release AOT 构建的 AndroidHost 上动态加载纯托管 fake 传输插件，走通完整链路
#       「加载 → 插件发现 → 配置路由 → 测试打印」——实证 MonoVM interpreter 路径可行性（fail-fast 前置）。
# 执行口径：worker 沙箱只验证「构建成功性」（-BuildApk）；真机走查（UROVO DT50 同级）由验收侧执行——
#           真机不可得时 AC-02 / AC-04 转待验收（恢复条件 = PDA 真机可得）；若走查跑出失败，按 fail-fast
#           中止机制段转用户评估（不硬推）。
# 用法示例（真机接 USB 调试后）：
#   pwsh scripts/test-pda-plugin-spike.ps1 -Version 0.30.0 -BuildApk          # 构建 APK + 插件包并全链路走查
#   pwsh scripts/test-pda-plugin-spike.ps1 -Version 0.30.0 -SkipInstall       # APK 已装，仅走查
# 兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Serial = '',                       # 多设备时指定 adb 序列号
    [string]$ApkPath = '',                      # 已有 APK 时跳过构建安装（配合 -SkipInstall）
    [string]$PackagePath = '',                  # 已有 fake 插件包时跳过构建
    [switch]$BuildApk,                          # 构建 Release AOT APK（EmbedAssembliesIntoApk，与 CI Android job 同型）
    [switch]$SkipInstall,                       # 跳过 APK 安装（设备已装）
    [switch]$KeepState                          # 结束后不清理（保留 fake 配置与已装插件，便于人工查看）
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$applicationId = 'com.labelframe.androidhost'
$localPort = 53970
$pluginId = 'labelframe-transport-fake'
$pass = @(); $fail = @()

function Step([string]$name, [scriptblock]$body) {
    Write-Host "`n==> $name" -ForegroundColor Cyan
    try { & $body | ForEach-Object { Write-Host "    $_" }; $script:pass += $name }
    catch { Write-Host "    失败：$($_.Exception.Message)" -ForegroundColor Red; $script:fail += $name; throw }
}

function Adb([string[]]$arguments) {
    $adbArgs = if ($Serial) { @('-s', $Serial) + $arguments } else { $arguments }
    $output = & adb @adbArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "adb $($arguments -join ' ') 失败：$($output -join ' ')" }
    return $output
}

function Invoke-LocalApi([string]$Method, [string]$Path, $Body = $null, [string]$ContentType = 'application/json') {
    # adb reverse 已把宿主 127.0.0.1:53970 映射到本机同端口
    if ($Method -eq 'GET') {
        return Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$localPort$Path" -TimeoutSec 30
    }
    if ($Body -is [byte[]]) {
        return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$localPort$Path" -Body $Body -ContentType $ContentType -TimeoutSec 120
    }
    return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$localPort$Path" -Body $Body -ContentType $ContentType -TimeoutSec 30
}

# ---- 0) 前置 ----
if (-not $PackagePath) {
    Step '构建 fake 插件包（platforms=android）' { & (Join-Path $PSScriptRoot 'build-fake-android-plugin.ps1') -Version $Version }
    $PackagePath = Join-Path $root "artifacts\$pluginId-$Version.lfplugin"
}
if (-not (Test-Path -LiteralPath $PackagePath)) { throw "找不到插件包：$PackagePath" }

if (-not $SkipInstall) {
    if ($BuildApk -or -not $ApkPath) {
        Step '构建 Release AOT APK（与 CI「Android 构建」同型：EmbedAssembliesIntoApk）' {
            # Release 默认启用（profiled）AOT；动态加载的程序集不在 AOT 图内 → interpreter 解释执行（Spike 假设）
            dotnet build (Join-Path $root 'src\LabelFrame.AndroidHost\LabelFrame.AndroidHost.csproj') `
                -c Release -p:EmbedAssembliesIntoApk=true
            if ($LASTEXITCODE -ne 0) { throw 'APK 构建失败' }
            '构建成功'
        }
        $ApkPath = (Get-ChildItem (Join-Path $root 'src\LabelFrame.AndroidHost\bin\Release\net10.0-android') -Filter '*-Signed.apk' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
    }
    if (-not (Test-Path -LiteralPath $ApkPath)) { throw "找不到 APK：$ApkPath" }

    Step '连接设备并安装 APK' {
        $devices = (& adb devices) | Where-Object { $_ -match '\tdevice$' }
        if (-not $devices) { throw '未发现已连接的 adb 设备（真机走查需 PDA 连接 USB 调试）' }
        Adb @('install', '-r', $ApkPath) | Write-Host
        'APK 已安装'
    }
}

Step '启动应用并打通本地端口（adb reverse）' {
    Adb @('shell', 'am', 'start', '-n', "$applicationId/LabelFrame.AndroidHost.MainActivity") | Out-Null
    Adb @('reverse', "tcp:$localPort", "tcp:$localPort") | Out-Null
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try { Invoke-RestMethod -Uri "http://127.0.0.1:$localPort/healthz" -TimeoutSec 2 | Out-Null; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw '本地 HTTP 未就绪（healthz 不通）——查 adb reverse 与应用启动状态' }
    'healthz 就绪'
}

# ---- 1) 安装（三层校验含平台门） → 2) 重启生效（加载） ----
Step '安装 fake 插件包（本地 HTTP 三层校验 + 平台门）' {
    $bytes = [System.IO.File]::ReadAllBytes($PackagePath)
    $result = Invoke-LocalApi 'POST' '/api/plugins/install' $bytes 'application/octet-stream'
    if (-not $result.ok) { throw '安装接口返回失败' }
    $result.message
}

Step '重启宿主服务使插件装配生效' {
    Adb @('shell', 'am', 'force-stop', $applicationId) | Out-Null
    Start-Sleep -Seconds 2
    Adb @('shell', 'am', 'start', '-n', "$applicationId/LabelFrame.AndroidHost.MainActivity") | Out-Null
    Adb @('reverse', "tcp:$localPort", "tcp:$localPort") | Out-Null
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try { Invoke-RestMethod -Uri "http://127.0.0.1:$localPort/healthz" -TimeoutSec 2 | Out-Null; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw '重启后本地 HTTP 未就绪' }
    '已重启'
}

# ---- 3) 插件发现（已装列表 loaded=true） ----
Step '插件发现：已装列表 loaded=true（interpreter 动态加载实证点）' {
    $installed = Invoke-LocalApi 'GET' '/api/plugins/installed'
    $fake = $installed | Where-Object { $_.PluginId -eq $pluginId }
    if (-not $fake) { throw "已装列表未见 $pluginId" }
    if (-not $fake.Loaded) { throw "插件未加载（loadError=$($fake.LoadError)）——Release AOT 动态加载失败，按 fail-fast 处置" }
    "已加载：$($fake.Name) $($fake.Version)"
}

# ---- 4) 配置路由（brand 切到 fake 插件） ----
Step '配置路由：打印机品牌切到 fake 插件' {
    $config = @{ printerBrand = $pluginId; connectionType = 'tcp'; tcpHost = '127.0.0.1'; tcpPort = 9100 } | ConvertTo-Json
    $saved = Invoke-LocalApi 'POST' '/api/host/config' $config
    # 配置保存后需重启宿主生效（传输在服务启动时创建）
    Adb @('shell', 'am', 'force-stop', $applicationId) | Out-Null
    Start-Sleep -Seconds 2
    Adb @('shell', 'am', 'start', '-n', "$applicationId/LabelFrame.AndroidHost.MainActivity") | Out-Null
    Adb @('reverse', "tcp:$localPort", "tcp:$localPort") | Out-Null
    for ($i = 0; $i -lt 30; $i++) {
        try { Invoke-RestMethod -Uri "http://127.0.0.1:$localPort/healthz" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Seconds 1 } 
    }
    $readback = Invoke-LocalApi 'GET' '/api/host/config'
    if ($readback.PrinterBrand -ne $pluginId) { throw "品牌回读不符：$($readback.PrinterBrand)" }
    "已路由到 $pluginId（printerAddress=$($readback.PrinterAddress)）"
}

# ---- 5) 测试打印（完整链路：校验 → 渲染 → ^GF → fake 传输发送） ----
Step '测试打印：内置测试标签走完整链路到 fake 传输' {
    $job = Invoke-LocalApi 'POST' '/api/host/test-print'
    $final = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        $final = Invoke-LocalApi 'GET' "/api/jobs/$($job.JobId)"
        if ($final.Status -in 'Completed', 'Failed', 'Cancelled') { break }
    }
    if ($final.Status -ne 'Completed') {
        $err = ($final.Items | Where-Object { $_.ErrorMessage } | Select-Object -First 1).ErrorMessage
        throw "测试打印终态 $($final.Status)：$err"
    }
    "作业 $($final.JobId) Completed（$($final.CompletedItems)/$($final.TotalItems)）"
}

Step '取证：fake 传输发送内容落盘（adb run-as 读取插件数据目录）' {
    $sink = Adb @('shell', 'run-as', $applicationId, 'cat', "files/plugins-data/fake-transport-sent.txt")
    $text = ($sink -join "`n")
    if ($text -notmatch '\^XA') { throw "sink 文件无 ZPL 内容：$($text.Substring(0, [Math]::Min(200, $text.Length)))" }
    "fake 传输已收到 $((($text -split "`n") | Where-Object { $_ -match '\^XA' }).Count) 条发送记录（首条：$($text.Substring(0, 80))…）"
}

# ---- 6) 收尾（默认清理：回 zebra + 卸载插件，验证删目录卸载语义） ----
if (-not $KeepState) {
    Step '收尾：回退 Zebra 品牌并卸载 fake 插件（删目录 + 重启生效）' {
        $config = @{ printerBrand = 'zebra'; connectionType = 'tcp' } | ConvertTo-Json
        Invoke-LocalApi 'POST' '/api/host/config' $config | Out-Null
        Invoke-LocalApi 'POST' '/api/plugins/uninstall' (@{ pluginId = $pluginId } | ConvertTo-Json) | Out-Null
        Adb @('shell', 'am', 'force-stop', $applicationId) | Out-Null
        Start-Sleep -Seconds 2
        Adb @('shell', 'am', 'start', '-n', "$applicationId/LabelFrame.AndroidHost.MainActivity") | Out-Null
        Adb @('reverse', "tcp:$localPort", "tcp:$localPort") | Out-Null
        for ($i = 0; $i -lt 30; $i++) {
            try { Invoke-RestMethod -Uri "http://127.0.0.1:$localPort/healthz" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Seconds 1 } 
        }
        $installed = Invoke-LocalApi 'GET' '/api/plugins/installed'
        if ($installed | Where-Object { $_.PluginId -eq $pluginId }) { throw '卸载后插件仍列出' }
        '已回退 Zebra 并卸载干净'
    }
    Adb @('reverse', '--remove', "tcp:$localPort") | Out-Null
}

Write-Host "`n========== Spike 走查结果 ==========" -ForegroundColor Green
$pass | ForEach-Object { Write-Host "  [PASS] $_" }
$fail | ForEach-Object { Write-Host "  [FAIL] $_" -ForegroundColor Red }
if ($fail.Count -gt 0) { exit 1 }
Write-Host '全部通过——Release AOT 动态加载纯托管插件链路实证（interpreter 路径可行）。' -ForegroundColor Green
