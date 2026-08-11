# 打包服务端管理界面插件（迭代 20）：web/dist-server -> artifacts/labelframe-server-webui-<version>.zip
# 前置：前端已执行 build:server（VITE_UI_MODE=server）产出 web/dist-server。
param(
    [string]$Version = '0.16.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$distServer = Join-Path $root 'web\dist-server'
if (-not (Test-Path -LiteralPath $distServer)) {
    throw "未找到前端 server 构建产物 $distServer，请先执行 build:server（VITE_UI_MODE=server 的 vite build --outDir dist-server）。"
}
if (-not (Test-Path -LiteralPath (Join-Path $distServer 'index.html'))) {
    throw "$distServer 缺少 index.html，产物不完整。"
}

$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$zip = Join-Path $artifacts "labelframe-server-webui-$Version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

# Compress-Archive 以目录内容为 zip 根（不含 dist-server 外层目录）
Compress-Archive -Path (Join-Path $distServer '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "插件包已生成：$zip"
Write-Host "部署：解压到服务端插件目录（Windows %ProgramData%\LabelFrame\server\plugins\web-ui；Linux /var/lib/labelframe/server/plugins/web-ui），放进去即生效、移除即无头。"