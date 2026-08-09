# 一键构建 LabelFrame MSI 安装包
param(
    [string]$Version = '0.11.0',
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe'
if (-not (Test-Path $wix)) { throw '未找到 WiX Toolset v7，请先安装。' }

# 1) 发布 WinHost（self-contained）+ 复制 web/dist
& (Join-Path $PSScriptRoot 'publish-winhost.ps1') -Runtime $Runtime
if (-not $?) { throw 'publish failed' }
$publishDir = Join-Path $root "artifacts\$Runtime"

# 2) 复制默认配置到发布目录（先于文件清单，确保 appsettings.json 被打包）
Copy-Item (Join-Path $root 'packaging\appsettings.json') (Join-Path $publishDir 'appsettings.json') -Force

# 3) 生成 WiX 文件清单
$filesWxs = Join-Path $root 'packaging\files.wxs'
& (Join-Path $root 'packaging\generate-files.ps1') -PublishDir $publishDir -OutFile $filesWxs

# 4) wix build
$msi = Join-Path $root "artifacts\LabelFrame-$Version.msi"
$global:LASTEXITCODE = 0
& $wix eula accept wix7 2>$null | Out-Null
& $wix build (Join-Path $root 'packaging\main.wxs') $filesWxs -d PublishDir=$publishDir -o $msi 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }
Write-Host "MSI 生成完成：$msi"
Write-Host "大小：$([Math]::Round((Get-Item $msi).Length / 1MB, 1)) MB"
