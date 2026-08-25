# 运行性能 / 稳定性测试（迭代 33）
# 用法：
#   powershell -File scripts/run-perf.ps1 -Mode perf     # 第二层：延迟/并发（约 1 分钟）
#   powershell -File scripts/run-perf.ps1 -Mode soak     # 第三层：稳定性（默认 5 分钟，LF_SOAK_MINUTES 可调）
#   powershell -File scripts/run-perf.ps1 -Mode bench    # 第一层：微基准（Release，约 5 分钟）
#   powershell -File scripts/run-perf.ps1 -Mode all
param([ValidateSet('perf', 'soak', 'bench', 'all')][string]$Mode = 'perf')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Run-Section([string]$title, [scriptblock]$body) {
    Write-Host "`n===== $title =====" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$title 失败（exit $LASTEXITCODE）" }
}

if ($Mode -in 'perf', 'all') {
    Run-Section '延迟 / 并发（Trait=Perf）' {
        dotnet test (Join-Path $root 'test\LabelFrame.Server.Tests') --no-build -v minimal --filter 'Category=Perf'
        dotnet test (Join-Path $root 'test\LabelFrame.WinHost.Tests') --no-build -v minimal --filter 'Category=Perf'
    }
}

if ($Mode -in 'soak', 'all') {
    Run-Section '稳定性 soak（Trait=Soak，默认 5 分钟 / LF_SOAK_MINUTES 调整）' {
        dotnet test (Join-Path $root 'test\LabelFrame.Server.Tests') --no-build -v minimal --filter 'Category=Soak'
    }
}

if ($Mode -in 'bench', 'all') {
    Run-Section '微基准（BenchmarkDotNet，Release）' {
        dotnet run -c Release --project (Join-Path $root 'test\LabelFrame.Benchmarks') -- --filter '*'
    }
}

Write-Host "`n全部完成。" -ForegroundColor Green
