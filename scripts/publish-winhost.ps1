# 发布 LabelFrame 单机版（WinHost framework-dependent + web/dist）
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = '',
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$publishDir = Join-Path $OutputDir $Runtime

# 给了版本就写进程序文件：安装包脚本靠它保证包内文件版本与安装包版本一致
$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

Write-Host "publish WinHost ($Configuration / $Runtime framework-dependent) ..."
dotnet publish (Join-Path $root 'src\LabelFrame.WinHost\LabelFrame.WinHost.csproj') `
    -c $Configuration -f net10.0-windows10.0.26100 -r $Runtime -p:SelfContained=false @versionArgs `
    -o $publishDir -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'WinHost publish failed' }

$webDist = Join-Path $root 'web\dist'
if (-not (Test-Path $webDist)) {
    Write-Host 'web/dist missing, building frontend ...'
    Push-Location (Join-Path $root 'web')
    pnpm install --frozen-lockfile | Out-Null
    pnpm build | Out-Null
    Pop-Location
}
$targetWeb = Join-Path $publishDir 'web\dist'
# 先清空目标，避免历史构建的旧资源残留进安装包
if (Test-Path -LiteralPath $targetWeb) { Remove-Item -LiteralPath $targetWeb -Recurse -Force }
New-Item -ItemType Directory -Force -Path $targetWeb | Out-Null
Copy-Item -Path (Join-Path $webDist '*') -Destination $targetWeb -Recurse -Force
Write-Host "done: $publishDir"
Write-Host "web ui: $targetWeb"
