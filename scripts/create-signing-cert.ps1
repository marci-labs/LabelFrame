# 创建 LabelFrame 代码签名证书（自签名，openssl + .NET 重封装）
# 说明：自签名证书签名后，目标机器仍可能提示未知发布者。
#  - 本机测试：加 -InstallTrusted 把证书装入受信任根，本机安装不再警告。
#  - 正式分发：需购买商业代码签名证书（OV/EV），本脚本仅用于开发验证。
param(
    [string]$PfxPath = '',
    [string]$Password = 'LabelFrame@2026',
    [switch]$InstallTrusted
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $PfxPath) { $PfxPath = Join-Path $root 'artifacts\cert\labelframe.pfx' }
$certDir = Split-Path -Parent $PfxPath
New-Item -ItemType Directory -Force -Path $certDir | Out-Null

# 查找 openssl（Git 自带）
$openssl = @('C:\Program Files\Git\usr\bin\openssl.exe', 'C:\Program Files\Git\mingw64\bin\openssl.exe', 'openssl') | ForEach-Object { Get-Command $_ -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source } | Select-Object -First 1
if (-not $openssl) { throw '未找到 openssl，请安装 Git for Windows。' }

$keyPem = Join-Path $certDir 'key.pem'
$certPem = Join-Path $certDir 'cert.pem'
$rawPfx = Join-Path $certDir 'raw.pfx'
& $openssl req -x509 -newkey rsa:2048 -keyout $keyPem -out $certPem -days 1095 -nodes -subj '/CN=LabelFrame' -addext 'extendedKeyUsage=codeSigning' 2>$null | Out-Null
if (-not $?) { throw '证书生成失败' }
& $openssl pkcs12 -export -out $rawPfx -inkey $keyPem -in $certPem -passout pass:$Password 2>$null | Out-Null
if (-not $?) { throw 'pfx 导出失败' }

# .NET 重封装（保证 CryptoAPI / signtool 兼容）
$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($rawPfx, $Password, $flags)
$bytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $Password)
[System.IO.File]::WriteAllBytes($PfxPath, $bytes)
Remove-Item -LiteralPath $rawPfx -Force -ErrorAction SilentlyContinue
Write-Host "证书已生成：$PfxPath"
Write-Host '指纹：' $cert.Thumbprint

if ($InstallTrusted) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'CurrentUser')
    $store.Open('ReadWrite')
    $store.Add($cert)
    $store.Close()
    Write-Host '已安装到当前用户受信任根（本机不再警告）。'
}