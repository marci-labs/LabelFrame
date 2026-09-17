# 一键发布 LabelFrame Server（Ubuntu / linux-x64，迭代 19；迭代 71 起默认 self-contained，决策 #134）
# 默认 self-contained（目标机免装 .NET 10 ASP.NET Core Runtime，对齐 Windows 侧引导链教训 #128/#129）；
# -FrameworkDependent 发布需运行时包（目标机自备 runtime，install.sh 检测缺失即报错给官方直链）。
param(
    [string]$Version = '0.15.4',
    [string]$Runtime = 'linux-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\server-linux\$Runtime"
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

$selfContained = -not $FrameworkDependent
Write-Host "publish Server ($Runtime, SelfContained=$selfContained) ..."
$self = if ($selfContained) { 'true' } else { 'false' }
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
Write-Host '一键安装（迭代 71）：sudo bash scripts/install-server-linux.sh（默认在线；--manifest <布局目录> 离线）'
Write-Host '手工部署（高级路径）：sudo bash scripts/deploy-server-ubuntu.sh artifacts\labelframe-server-...linux-x64.tar.gz'
