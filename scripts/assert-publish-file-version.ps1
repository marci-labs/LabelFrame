# 核对发布目录里主程序的文件版本与安装包版本一致。Windows Installer 按组件 keyfile 的版本判断升降，
# 「安装包版本正确、程序文件版本停在默认值」的包无法从旧版升级，而包本身看不出任何异常。
# 用法：& scripts/assert-publish-file-version.ps1 -ExePath artifacts\server\win-x64\LabelFrame.Server.exe -Version 1.6.1
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'

# 文件版本只有数字、固定四段：预发行标签与生成元数据不算，缺的段补 0（与 .NET SDK 从 Version 属性推导文件版本的规则一致）
function Format-FileVersion([string]$Text) {
    $parts = @([regex]::Match($Text, '^\d+(?:\.\d+)*').Value -split '\.')
    while ($parts.Count -lt 4) { $parts += '0' }
    return ($parts | Select-Object -First 4) -join '.'
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "发布目录里没有主程序 $ExePath" }
$actual = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $ExePath).Path).FileVersion
if (-not $actual) { throw "主程序 $ExePath 没有文件版本信息，无法确认它与安装包版本 $Version 一致" }
if ((Format-FileVersion $actual) -ne (Format-FileVersion $Version)) {
    throw "主程序 $ExePath 的文件版本是 $actual，与安装包版本 $Version 不一致：程序文件版本低于已安装的版本时，Windows Installer 会拒绝安装，这样的包无法从旧版升级。"
}
Write-Host "文件版本一致：$([IO.Path]::GetFileName($ExePath)) $actual（安装包版本 $Version）"
