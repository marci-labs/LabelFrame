# LabelFrame WinHost 快速演示（迭代 2）
# 用法： powershell -ExecutionPolicy Bypass -File .\scripts\demo-winhost.ps1
# 说明：以日志传输（模拟打印机）启动 WinHost，提交一个含中文的库位码作业，
#       打印完成后展示生成的 ZPL，并给出接真实打印机的方法。
param(
    [int]$Port = 53999,
    [string]$Db = (Join-Path $env:TEMP "LabelFrame-demo-$([guid]::NewGuid().ToString('N')).db")
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$exeDir = Join-Path $repo 'src\LabelFrame.WinHost\bin\Debug\net10.0-windows10.0.26100'
$exe = Join-Path $exeDir 'LabelFrame.WinHost.exe'
$listen = "http://127.0.0.1:$Port"
$log = Join-Path $env:TEMP "LabelFrame-demo-$([guid]::NewGuid().ToString('N')).log"
$cfg = Join-Path $exeDir 'appsettings.json'

Write-Host '== 1/4 构建 WinHost ...' -ForegroundColor Cyan
Push-Location $repo
try { dotnet build src\LabelFrame.WinHost\LabelFrame.WinHost.csproj --nologo -v q | Out-Null } finally { Pop-Location }

Write-Host '== 2/4 启动 WinHost（日志传输，模拟打印机）...' -ForegroundColor Cyan
$cfgJson = @{ WinHost = @{ ListenUrl = $listen; DatabasePath = $Db; Transport = 'Log' } } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($cfg, $cfgJson, [System.Text.UTF8Encoding]::new($false))
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$outTask = $proc.StandardOutput.ReadToEndAsync()
$errTask = $proc.StandardError.ReadToEndAsync()

try {
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { Invoke-RestMethod -Uri "$listen/healthz" -TimeoutSec 2 | Out-Null; $ready = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { throw 'WinHost 启动失败' }
    Write-Host "WinHost 已就绪：$listen"

    Write-Host '== 3/4 提交库位码作业（2 张，含中文）...' -ForegroundColor Cyan
    $body = @{
        requestId = "demo-$([guid]::NewGuid().ToString('N'))"
        template = @{
            contract = @{ name = 'location-label'; version = '1.0'; fields = @(
                @{ key = 'locationCode'; displayName = '库位码'; isRequired = $true; type = 'text' },
                @{ key = 'zone'; displayName = '区域'; isRequired = $true; type = 'text' },
                @{ key = 'remark'; displayName = '备注'; type = 'text' }) }
            layout = @{ name = 'location-label-100x60'; contractName = 'location-label'; contractVersion = '1.0'; widthMm = 100; heightMm = 60; elements = @(
                @{ type = 'text'; sourceKey = 'zone'; xMm = 5; yMm = 4; fontHeightMm = 5; fontWidthMm = 5 },
                @{ type = 'text'; sourceKey = 'locationCode'; xMm = 5; yMm = 14; fontHeightMm = 8; fontWidthMm = 8 },
                @{ type = 'barcode'; sourceKey = 'locationCode'; xMm = 5; yMm = 26; heightMm = 22; moduleWidth = 2 }) }
        }
        labels = @(
            @{ data = @{ zone = '中文区域-A'; locationCode = 'A-01-02-03'; remark = '收货' } },
            @{ data = @{ zone = '中文区域-B'; locationCode = 'B-02-03-04'; remark = '补打' } })
    } | ConvertTo-Json -Depth 8

    $job = Invoke-RestMethod -Uri "$listen/api/jobs" -Method Post -ContentType 'application/json' -Body $body
    Write-Host "作业已提交：jobId=$($job.jobId) status=$($job.status) 张数=$($job.totalItems)"

    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 200
        $job = Invoke-RestMethod -Uri "$listen/api/jobs/$($job.jobId)"
        if ($job.status -in @('Completed', 'Failed', 'Cancelled')) { break }
    }
    Write-Host "作业终态：status=$($job.status) completed=$($job.completedItems)/$($job.totalItems)"

    Write-Host '== 4/4 模拟打印机输出的 ZPL（截取）...' -ForegroundColor Cyan
    Start-Sleep -Milliseconds 300
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    $proc.WaitForExit(5000) | Out-Null
    $logs = $outTask.Result
    $marks = [regex]::Matches($logs, '=== LabelFrame 模拟打印机 ===')
    $idx = 0
    foreach ($m in $marks) {
        $idx++
        $end = $logs.IndexOf('=== 输出结束 ===', $m.Index)
        $zpl = $logs.Substring($m.Index, $end - $m.Index)
        Write-Host "--- 第 $idx 张 ---"
        Write-Host $zpl.Trim()
    }
    Write-Host ''
    Write-Host '演示完成。接真实打印机：设置环境变量后启动 WinHost，例如：' -ForegroundColor Green
    Write-Host '  $env:LABELFRAME_TRANSPORT = "Zebra"   # 或 Tcp / WindowsDriver' -ForegroundColor Green
    Write-Host '  $env:LABELFRAME_TCP_HOST = "192.168.1.50"   # Zebra TCP 时' -ForegroundColor Green
    Write-Host '  $env:LABELFRAME_PRINTER = "ZDesigner ZD421-203dpi ZPL"   # Zebra Driver / WindowsDriver 时' -ForegroundColor Green
    Write-Host '  $env:LABELFRAME_DB = "C:\LabelFrame\jobs.db"' -ForegroundColor Green
    Write-Host '  dotnet run --project src\LabelFrame.WinHost' -ForegroundColor Green
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 300
    Remove-Item $cfg -Force -ErrorAction SilentlyContinue
    Remove-Item "$Db-shm", "$Db-wal" -Force -ErrorAction SilentlyContinue
    Remove-Item $Db -Force -ErrorAction SilentlyContinue
    Remove-Item $log -Force -ErrorAction SilentlyContinue
}