# 构建 LabelFrame 安装引导 Bundle（WiX Burn，DESIGN §6.1 决策 #122）
# 前置：Server / Client MSI 已生成（bundle 构建需读取 MSI 包元数据）：
#   .\scripts\build-server-msi.ps1 -Version x.y.z -WixPath <wix.exe>
#   .\scripts\build-msi.ps1       -Version x.y.z -WixPath <wix.exe>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$WixPath = '',
    [string]$MsiDir = '',
    [string]$DownloadBase = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$wix = $WixPath
if (-not $wix) { $wix = $env:WIX_PATH }
if (-not $wix) { $wix = 'C:\Program Files\WiX Toolset v7.0\bin\wix.exe' }
if (-not (Test-Path $wix)) { $wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe' }
if (-not (Test-Path $wix)) { throw "未找到 WiX（$wix），请安装 WiX Toolset v7（dotnet tool install --global wix --version 7.0.0）或通过 -WixPath 指定 wix.exe。" }
if (-not $MsiDir) { $MsiDir = Join-Path $root 'artifacts' }
if (-not $DownloadBase) { $DownloadBase = "https://github.com/marci-labs/LabelFrame/releases/download/v$Version" }

# 1) 发布 BA（net48；RID win-x64 使 mbanative.dll 等 runtimes 资产落位发布目录）
$baDir = Join-Path $root 'artifacts\bootstrapper\ba'
if (Test-Path -LiteralPath $baDir) { Remove-Item -LiteralPath $baDir -Recurse -Force }
dotnet publish (Join-Path $root 'src\LabelFrame.Bootstrapper.Ba\LabelFrame.Bootstrapper.Ba.csproj') `
    -c Release -f net48 -r win-x64 -o $baDir -p:DebugType=None -p:DebugSymbols=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'BA publish failed' }

# 2) 校验 MSI 在场（Compressed=no 仍需 SourceFile 提取 ProductCode 等元数据）
$serverMsi = Join-Path $MsiDir "LabelFrame-Server-$Version.msi"
$clientMsi = Join-Path $MsiDir "LabelFrame-Client-$Version.msi"
foreach ($msi in @($serverMsi, $clientMsi)) {
    if (-not (Test-Path -LiteralPath $msi)) {
        throw "未找到 $msi —— 请先运行 scripts\build-server-msi.ps1 与 scripts\build-msi.ps1 生成双 MSI。"
    }
}

# 3) wix build（Burn 核心内置，自研 BA 无需 Bal 扩展）
$bundleExe = Join-Path $root "artifacts\LabelFrame-Bootstrapper-$Version.exe"
$global:LASTEXITCODE = 0
& $wix eula accept wix7 2>$null | Out-Null
& $wix build (Join-Path $root 'packaging\bootstrapper\Bundle.wxs') `
    -d "BaDir=$baDir\" -d "MsiDir=$MsiDir\" `
    -d "ProductVersion=$Version" -d "BundleVersion=$Version" -d "DownloadBase=$DownloadBase" `
    -o $bundleExe -arch x64 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

Write-Host "Bundle 生成完成：$bundleExe"
Write-Host "大小：$([Math]::Round((Get-Item $bundleExe).Length / 1MB, 2)) MB"
