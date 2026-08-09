# 生成 WiX 文件清单（fragment wxs）：根目录文件 + web/dist 子目录
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

function Add-FileLine([System.Text.StringBuilder]$sb, [int]$idx, [string]$guid, [string]$rel, [string]$fileId, [string]$prefix) {
    $ext = [System.IO.Path]::GetExtension($rel)
    if ($ext.Length -gt 0) { $ext = $ext.Substring(1) }
    if ($ext.Length -gt 3) { $ext = $ext.Substring(0, 3) }
    $shortName = ('LF{0:D6}.{1}' -f $idx, $ext.ToUpperInvariant())
    [void]$sb.AppendLine('      <Component Id="' + $prefix + $idx + '" Guid="' + $guid + '">')
    [void]$sb.AppendLine('        <File' + $fileId + ' ShortName="' + $shortName + '" Source="$(var.PublishDir)/' + $rel + '" />')
    [void]$sb.AppendLine('      </Component>')
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
[void]$sb.AppendLine('      <Directory Id="WebDir" Name="web">')
[void]$sb.AppendLine('        <Directory Id="WebDistDir" Name="dist" />')
[void]$sb.AppendLine('      </Directory>')
[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')

$webSb = New-Object System.Text.StringBuilder
[void]$webSb.AppendLine('    <ComponentGroup Id="WebFiles" Directory="WebDistDir">')

$index = 0
$webIndex = 0
Get-ChildItem -Path $PublishDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($PublishDir.Length).TrimStart('\')
    $rel = $rel -replace '\\', '/'
    $guid = New-GuidFromPath $rel
    $isWeb = $rel.StartsWith('web/dist/')
    if ($rel -eq 'LabelFrame.WinHost.exe') {
        Add-FileLine $sb $index $guid $rel ' Id="WinHostExe"' 'f'
        $index++
    } elseif ($isWeb) {
        Add-FileLine $webSb $webIndex $guid $rel (' Id="wf' + $webIndex + '"') 'w'
        $webIndex++
    } else {
        Add-FileLine $sb $index $guid $rel (' Id="ff' + $index + '"') 'f'
        $index++
    }
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$webSb.AppendLine('    </ComponentGroup>')
[void]$sb.Append($webSb.ToString())
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "generated $($index + $webIndex) files -> $OutFile"