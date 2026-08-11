# 一键发布 LabelFrame Server（Ubuntu / linux-x64，迭代 19）
# 默认 framework-dependent（目标机需 .NET 10 ASP.NET Core Runtime）；-SelfContained 发布免运行时包。
param(
    [string]$Version = '0.15.4',
    [string]$Runtime = 'linux-x64',
    [switch]$SelfContained
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\server-linux\$Runtime"
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

Write-Host "publish Server ($Runtime, SelfContained=$SelfContained) ..."
$self = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish (Join-Path $root 'src\LabelFrame.Server\LabelFrame.Server.csproj') `
    -c Release -f net10.0 -r $Runtime -p:SelfContained=$self `
    -o $publishDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# 服务端默认配置（ListenUrl 0.0.0.0:53961）
Copy-Item (Join-Path $root 'packaging\appsettings-server.json') (Join-Path $publishDir 'appsettings.json') -Force

# 归档 tar.gz
$tar = Join-Path $root "artifacts\labelframe-server-$Version-$Runtime.tar.gz"
if (Test-Path $tar) { Remove-Item $tar -Force }
tar -czf $tar -C (Split-Path $publishDir -Parent) (Split-Path $publishDir -Leaf)
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }

Write-Host "发布目录: $publishDir"
Write-Host "归档: $tar ($([Math]::Round((Get-Item $tar).Length / 1MB, 1)) MB)"
Write-Host '部署到 Ubuntu：sudo bash scripts/deploy-server-ubuntu.sh artifacts\labelframe-server-...linux-x64.tar.gz'