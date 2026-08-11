# 一键构建 LabelFrame Client（打印客户端）MSI 安装包（迭代 16/17：安装到 Program Files\LabelFrame\Client）
param(
    [string]$Version = '0.15.3',
    [string]$Runtime = 'win-x64',
    [string]$PfxPath = '',
    [string]$PfxPassword = 'LabelFrame@2026',
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe'
if (-not (Test-Path $wix)) { throw '未找到 WiX Toolset v7，请先安装。' }

# 1) 发布 WinHost（Client，framework-dependent）+ 复制 web/dist
$publishDir = Join-Path $root "artifacts\client\$Runtime"
& (Join-Path $PSScriptRoot 'publish-winhost.ps1') -Runtime $Runtime -OutputDir (Join-Path $root 'artifacts\client')
if (-not $?) { throw 'publish failed' }

# 2) 复制默认配置到发布目录（先于文件清单，确保 appsettings.json 被打包）
Copy-Item (Join-Path $root 'packaging\appsettings.json') (Join-Path $publishDir 'appsettings.json') -Force

# 3) 生成 WiX 文件清单（GUID 加盐 client，避免与 Server 包组件 GUID 冲突）
$filesWxs = Join-Path $root 'packaging\files-client.wxs'
& (Join-Path $root 'packaging\generate-files.ps1') -PublishDir $publishDir -OutFile $filesWxs -GuidSalt 'client'

# 4) wix build
$msi = Join-Path $root "artifacts\LabelFrame-Client-$Version.msi"
$global:LASTEXITCODE = 0
& $wix eula accept wix7 2>$null | Out-Null
& $wix build (Join-Path $root 'packaging\main.wxs') $filesWxs -d PublishDir=$publishDir -o $msi -arch x64 -ext WixToolset.NetFx.wixext 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

# 5) 代码签名（可选：-Sign）
if ($Sign) {
    if (-not $PfxPath) { $PfxPath = Join-Path $root 'artifacts\cert\labelframe.pfx' }
    if (-not (Test-Path $PfxPath)) { throw "未找到证书 $PfxPath，请先运行 scripts\create-signing-cert.ps1" }
    $signtoolPath = $env:SIGNFILE
    if (-not $signtoolPath) {
        $found = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $signtoolPath = $found.FullName }
    }
    if (-not $signtoolPath) {
        $toolsDir = Join-Path $root 'artifacts\tools'
        $cachedSig = Get-ChildItem $toolsDir -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($cachedSig) { $signtoolPath = $cachedSig.FullName } else {
            Write-Host '未找到 signtool，正在从 NuGet 下载 Windows SDK BuildTools 提取…'
            New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
            $ver = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/index.json' -TimeoutSec 60).versions | Select-Object -Last 1
            $zip = Join-Path $toolsDir 'sdkbt.zip'
            Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$ver/microsoft.windows.sdk.buildtools.$ver.nupkg" -OutFile $zip -TimeoutSec 300
            Expand-Archive -Path $zip -DestinationPath $toolsDir -Force
            $extracted = Get-ChildItem $toolsDir -Recurse -Filter signtool.exe | Sort-Object FullName -Descending | Select-Object -First 1
            if (-not $extracted) { throw 'signtool 提取失败。' }
            $signtoolPath = $extracted.FullName
        }
    }
    if (-not $signtoolPath) { throw '未找到 signtool.exe：请安装 Windows SDK，或设置环境变量 SIGNFILE 指向 signtool.exe' }
    Write-Host "使用 signtool：$signtoolPath"
    $global:LASTEXITCODE = 0
    & $signtoolPath sign /f $PfxPath /p $PfxPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $msi 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'MSI 签名失败' }
    Write-Host "MSI 已签名：$msi"
}

Write-Host "MSI 生成完成：$msi"
Write-Host "大小：$([Math]::Round((Get-Item $msi).Length / 1MB, 1)) MB"