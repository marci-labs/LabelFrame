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
# 真机首轮走查后修复（#185 fail-fast 取证评论三缺陷）：
#   ① adb 辅助函数不得命名为 Adb（PowerShell 函数优先于同名外部命令 → 死递归调用深度溢出），改名 Invoke-Adb；
#   ② 启动入口不得硬编码「包名/短类名」——Release AOT 产物 ACW 类名为 crc*.MainActivity 形态，按 LAUNCHER 动态解析；
#   ③ 端口打通用 adb forward（reverse 会令设备端 adbd 监听 127.0.0.1:53970，与应用 EmbeddedHttpServer 抢占同址
#      导致宿主 Address already in use 崩溃循环）；宿主侧映射端口取 53971，避开 53970 的任何本机占用。
#   ④ sink 落盘取证优先 run-as，Release 产物（android:debuggable=false）run-as 被拒时回退 logcat 双断言
#      （[FAKE] 发送行在场 + 落盘失败行缺席）——复跑时暴露（约束面版 fake 发送段已通后走到该步）。
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
$devicePort = 53970                            # 设备端 EmbeddedHttpServer 监听端口（LabelHostConfig.LocalPort）
$hostPort = 53971                              # 宿主侧映射端口（≠53970：避开 adbd / 其他本机服务对 127.0.0.1:53970 的占用）
$pluginId = 'labelframe-transport-fake'
$pass = @(); $fail = @()
$script:adbExe = $null
$script:launcherComponent = $null

function Step([string]$name, [scriptblock]$body) {
    Write-Host "`n==> $name" -ForegroundColor Cyan
    try { & $body | ForEach-Object { Write-Host "    $_" }; $script:pass += $name }
    catch { Write-Host "    失败：$($_.Exception.Message)" -ForegroundColor Red; $script:fail += $name; throw }
}

function Get-AdbExe {
    # adb 常不在 PATH：PATH → %LOCALAPPDATA%\Android\Sdk → ANDROID_HOME 依次回退
    if ($script:adbExe) { return $script:adbExe }
    $fromPath = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($fromPath) { $script:adbExe = $fromPath.Source; return $script:adbExe }
    $candidates = @()
    if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe') }
    if ($env:ANDROID_HOME) { $candidates += (Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe') }
    $candidates += 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'   # Visual Studio 捆绑 SDK 安装位
    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $found) { throw '未找到 adb.exe（PATH、%LOCALAPPDATA%\Android\Sdk\platform-tools、ANDROID_HOME 均无）' }
    $script:adbExe = $found
    return $script:adbExe
}

function Invoke-Adb([string[]]$arguments) {
    # 函数名必须是 Invoke-Adb 而非 Adb：PowerShell 命令解析函数优先于同名外部命令，
    # 函数内 & adb 会递归调用自身直至 call depth overflow（首轮真机走查实证缺陷①）。
    $adb = Get-AdbExe
    $adbArgs = if ($Serial) { @('-s', $Serial) + $arguments } else { $arguments }
    # PS 5.1 下 EAP=Stop + 2>&1 会把原生命令的首行 stderr（如 adb 守护进程启动信息）升级为终止错误，调用期放宽
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $adb @adbArgs 2>&1 } finally { $ErrorActionPreference = $prevEap }
    if ($LASTEXITCODE -ne 0) { throw "adb $($arguments -join ' ') 失败：$($output -join ' ')" }
    return $output
}

function Start-HostApp {
    # 启动入口动态解析（缺陷②）：Release AOT 产物 MainActivity 的 ACW 类名是 crc*.MainActivity 形态，
    # 「包名/LabelFrame.AndroidHost.MainActivity」拼接不存在 → am start Error type 3。
    # 按 MAIN/LAUNCHER intent 解析真实组件名；解析不出时回退 monkey 启动，任一路径失败都会显式抛错。
    if (-not $script:launcherComponent) {
        $resolved = Invoke-Adb @('shell', 'cmd', 'package', 'resolve-activity', '--brief',
            '-a', 'android.intent.action.MAIN', '-c', 'android.intent.category.LAUNCHER', $applicationId)
        $component = $resolved | ForEach-Object { "$_".Trim() } |
            Where-Object { $_ -like "$applicationId/*" } | Select-Object -Last 1
        if ($component) { $script:launcherComponent = $component }
    }
    if ($script:launcherComponent) {
        Invoke-Adb @('shell', 'am', 'start', '-n', $script:launcherComponent) | Out-Null
    }
    else {
        Invoke-Adb @('shell', 'monkey', '-p', $applicationId, '-c', 'android.intent.category.LAUNCHER', '1') | Out-Null
    }
}

function Wait-HostReady {
    # adb forward 已把设备 127.0.0.1:53970 映射到本机 53971（缺陷③：reverse 方向反，且设备端 adbd 会抢占 53970）
    for ($i = 0; $i -lt 30; $i++) {
        try { Invoke-RestMethod -Uri "http://127.0.0.1:$hostPort/healthz" -TimeoutSec 2 | Out-Null; return $true } catch { Start-Sleep -Seconds 1 }
    }
    return $false
}

function Invoke-LocalApi([string]$Method, [string]$Path, $Body = $null, [string]$ContentType = 'application/json') {
    if ($Method -eq 'GET') {
        return Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$hostPort$Path" -TimeoutSec 30
    }
    if ($Body -is [byte[]]) {
        return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$hostPort$Path" -Body $Body -ContentType $ContentType -TimeoutSec 120
    }
    return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$hostPort$Path" -Body $Body -ContentType $ContentType -TimeoutSec 30
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
        $devices = Invoke-Adb @('devices') | Where-Object { $_ -match '\tdevice$' }
        if (-not $devices) { throw '未发现已连接的 adb 设备（真机走查需 PDA 连接 USB 调试）' }
        Invoke-Adb @('install', '-r', $ApkPath) | Write-Host
        'APK 已安装'
    }
}

Step '启动应用并打通本地端口（adb forward）' {
    Start-HostApp
    Invoke-Adb @('forward', "tcp:$hostPort", "tcp:$devicePort") | Out-Null
    if (-not (Wait-HostReady)) { throw '本地 HTTP 未就绪（healthz 不通）——查 adb forward 与应用启动状态' }
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
    Invoke-Adb @('shell', 'am', 'force-stop', $applicationId) | Out-Null
    Start-Sleep -Seconds 2
    Start-HostApp
    Invoke-Adb @('forward', "tcp:$hostPort", "tcp:$devicePort") | Out-Null
    if (-not (Wait-HostReady)) { throw '重启后本地 HTTP 未就绪' }
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
    Invoke-Adb @('shell', 'am', 'force-stop', $applicationId) | Out-Null
    Start-Sleep -Seconds 2
    Start-HostApp
    Invoke-Adb @('forward', "tcp:$hostPort", "tcp:$devicePort") | Out-Null
    if (-not (Wait-HostReady)) { throw '配置路由后本地 HTTP 未就绪' }
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

Step '取证：fake 传输发送内容（sink 落盘优先，logcat 回退）' {
    # Release 产物 android:debuggable=false，run-as 被系统拒绝（package not debuggable）——
    # 优先 run-as 直读 sink 落盘文件；不可读时回退 logcat 双断言：[FAKE] 发送行必须在场、落盘失败行必须缺席。
    $text = $null
    try {
        $sink = Invoke-Adb @('shell', 'run-as', $applicationId, 'cat', 'files/plugins-data/fake-transport-sent.txt')
        $text = ($sink -join "`n")
    }
    catch {
        Write-Host '    run-as 不可用（Release 非 debuggable），回退 logcat 取证'
    }
    if ($text -and $text -match '\^XA') {
        "fake 传输 sink 已收到 $((($text -split "`n") | Where-Object { $_ -match '\^XA' }).Count) 条发送记录（首条：$($text.Substring(0, 80))…）"
    }
    else {
        $log = Invoke-Adb @('logcat', '-d', '-v', 'time', '-s', 'LabelFrame.Plugin')
        $sendLines = @($log | Where-Object { $_ -match '\[FAKE\] 模拟发送' })
        $sinkFail = @($log | Where-Object { $_ -match '落盘失败' })
        if ($sendLines.Count -eq 0) { throw 'sink 与 logcat 双通道均无发送证据（[FAKE] 模拟发送行缺席）' }
        if ($sinkFail.Count -gt 0) { throw "发送内容落盘失败：$($sinkFail[0])" }
        "logcat 取证：[FAKE] 发送行共 $($sendLines.Count) 条（$($sendLines[-1].Substring(0, [Math]::Min(110, $sendLines[-1].Length)))…），无落盘失败行"
    }
}

# ---- 6) 收尾（默认清理：回 zebra + 卸载插件，验证删目录卸载语义） ----
if (-not $KeepState) {
    Step '收尾：回退 Zebra 品牌并卸载 fake 插件（删目录 + 重启生效）' {
        $config = @{ printerBrand = 'zebra'; connectionType = 'tcp' } | ConvertTo-Json
        Invoke-LocalApi 'POST' '/api/host/config' $config | Out-Null
        Invoke-LocalApi 'POST' '/api/plugins/uninstall' (@{ pluginId = $pluginId } | ConvertTo-Json) | Out-Null
        Invoke-Adb @('shell', 'am', 'force-stop', $applicationId) | Out-Null
        Start-Sleep -Seconds 2
        Start-HostApp
        Invoke-Adb @('forward', "tcp:$hostPort", "tcp:$devicePort") | Out-Null
        if (-not (Wait-HostReady)) { throw '卸载重启后本地 HTTP 未就绪' }
        $installed = Invoke-LocalApi 'GET' '/api/plugins/installed'
        if ($installed | Where-Object { $_.PluginId -eq $pluginId }) { throw '卸载后插件仍列出' }
        '已回退 Zebra 并卸载干净'
    }
    Invoke-Adb @('forward', '--remove', "tcp:$hostPort") | Out-Null
}

Write-Host "`n========== Spike 走查结果 ==========" -ForegroundColor Green
$pass | ForEach-Object { Write-Host "  [PASS] $_" }
$fail | ForEach-Object { Write-Host "  [FAIL] $_" -ForegroundColor Red }
if ($fail.Count -gt 0) { exit 1 }
Write-Host '全部通过——Release AOT 动态加载纯托管插件链路实证（interpreter 路径可行）。' -ForegroundColor Green
