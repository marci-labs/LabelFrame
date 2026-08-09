# 发布 LabelFrame 单机版（WinHost framework-dependent + web/dist）
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$publishDir = Join-Path $OutputDir $Runtime

Write-Host "publish WinHost ($Configuration / $Runtime framework-dependent) ..."
dotnet publish (Join-Path $root 'src\LabelFrame.WinHost\LabelFrame.WinHost.csproj') `
    -c $Configuration -r $Runtime -p:SelfContained=false `
    -o $publishDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
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
New-Item -ItemType Directory -Force -Path $targetWeb | Out-Null
Copy-Item -Path (Join-Path $webDist '*') -Destination $targetWeb -Recurse -Force
Write-Host "done: $publishDir"
Write-Host "web ui: $targetWeb"