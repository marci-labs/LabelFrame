# 生成 WiX 文件清单（fragment wxs）
param(
    [string]$PublishDir = '',
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishDir) { $PublishDir = Join-Path $root 'artifacts\win-x64' }
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot 'files.wxs' }
if (-not (Test-Path (Join-Path $PublishDir 'LabelFrame.WinHost.exe'))) { throw 'publish dir invalid' }

function New-GuidFromPath([string]$rel) {
    $sha = [System.Security.Cryptography.SHA1]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($rel))
    $b = $bytes[0..15]
    $b[7] = (($b[7] -band 0x0f) -bor 0x50)
    $b[8] = (($b[8] -band 0x3f) -bor 0x80)
    return [Guid]::Parse([BitConverter]::ToString($b).Replace('-', '')).ToString().ToUpperInvariant()
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')
$index = 0
Get-ChildItem -Path $PublishDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($PublishDir.Length).TrimStart('\')
    $rel = $rel -replace '\\', '/'
    $guid = New-GuidFromPath $rel
    $fileId = ''
    if ($rel -eq 'LabelFrame.WinHost.exe') { $fileId = ' Id="WinHostExe"' }
    else { $fileId = ' Id="ff' + $index + '"' }
    # 显式唯一短文件名（8.3），避免 WIX1070 语言资源短名冲突
    $ext = [System.IO.Path]::GetExtension($rel)
    if ($ext.Length -gt 0) { $ext = $ext.Substring(1) }   # 去点
    if ($ext.Length -gt 3) { $ext = $ext.Substring(0, 3) }
    $shortName = ('LF{0:D6}.{1}' -f $index, $ext.ToUpperInvariant())
    [void]$sb.AppendLine('      <Component Id="f' + $index + '" Guid="' + $guid + '">')
    [void]$sb.AppendLine('        <File' + $fileId + ' ShortName="' + $shortName + '" Source="$(var.PublishDir)/' + $rel + '" />')
    [void]$sb.AppendLine('      </Component>')
    $index++
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "generated $index files -> $OutFile"
