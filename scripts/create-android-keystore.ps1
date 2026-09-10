# 生成 AndroidHost 正式签名 keystore（一次性），可选直接写入 GitHub Actions Secrets。
# release.yml 检测到 ANDROID_KEYSTORE_BASE64 时用该 keystore 签名 Release APK（决策 #104）。
# 用法示例：
#   .\scripts\create-android-keystore.ps1 -Password '<强密码>' -SetGithubSecrets
# 注意：keystore 与密码丢失 = 无法再发同签名升级包（用户必须卸载重装），请把 keystore 与密码备份到安全位置。
param(
    [string]$KeystorePath = 'artifacts\android\labelframe-release.keystore',
    [string]$Alias = 'labelframe',
    [Parameter(Mandatory = $true)]
    [string]$Password,
    # PKCS12 单口令：store 与 key 同密码（.NET Android 签名传 AndroidSigningStorePass / AndroidSigningKeyPass 同值）
    [string]$DName = 'CN=LabelFrame AndroidHost',
    [int]$ValidityDays = 10950,
    [switch]$SetGithubSecrets
)
$ErrorActionPreference = 'Stop'

# 1) 定位 keytool：优先 JAVA_HOME，其次 ~/.jdk（与 build-androidhost.ps1 同策略）
if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin\keytool.exe'))) {
    $jdk = Get-ChildItem "$env:USERPROFILE\.jdk" -Recurse -Filter keytool.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($jdk) { $env:JAVA_HOME = $jdk.DirectoryName.Substring(0, $jdk.DirectoryName.Length - 4) }
}
if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin\keytool.exe'))) {
    throw '未找到 JDK 17（keytool），请安装并设置 JAVA_HOME。'
}
$keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe'

# 2) 生成 RSA 2048 / PKCS12 keystore（现代 keytool 默认 PKCS12，显式声明防歧义）
$dir = Split-Path -Parent $KeystorePath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
if (Test-Path $KeystorePath) { throw "已存在 $KeystorePath——如确要重生成请先删除（旧签名包将无法再被覆盖升级）。" }

& $keytool -genkeypair -storetype PKCS12 -keystore $KeystorePath -alias $Alias `
    -keyalg RSA -keysize 2048 -validity $ValidityDays `
    -storepass $Password -keypass $Password -dname $DName
if ($LASTEXITCODE -ne 0) { throw 'keytool 生成 keystore 失败。' }
Write-Host "已生成：$KeystorePath（alias=$Alias，有效期 $ValidityDays 天）"

# 3) 可选：写入 GitHub Actions Secrets（经 gh CLI；值经临时文件重定向，避免命令行泄露）
if ($SetGithubSecrets) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '未找到 gh——请安装并登录后重试（gh auth login）。' }
    $secrets = @{
        'ANDROID_KEYSTORE_BASE64'   = [Convert]::ToBase64String([IO.File]::ReadAllBytes($KeystorePath))
        'ANDROID_KEYSTORE_PASSWORD' = $Password
        'ANDROID_KEY_ALIAS'         = $Alias
        'ANDROID_KEY_PASSWORD'      = $Password
    }
    foreach ($name in $secrets.Keys) {
        $tmp = New-TemporaryFile
        try {
            [IO.File]::WriteAllText($tmp, $secrets[$name])
            Get-Content $tmp -Raw | gh secret set $name
            if ($LASTEXITCODE -ne 0) { throw "gh secret set $name 失败。" }
        }
        finally {
            Remove-Item $tmp -Force
        }
        Write-Host "已写入 Secret：$name"
    }
    Write-Host 'release.yml 将在下次发版自动使用该 keystore 签名 APK。'
}
else {
    Write-Host '如需 CI 自动签名，请把以下四个值配置为仓库 Secrets（或加 -SetGithubSecrets 由本脚本写入）：'
    Write-Host '  ANDROID_KEYSTORE_BASE64 / ANDROID_KEYSTORE_PASSWORD / ANDROID_KEY_ALIAS / ANDROID_KEY_PASSWORD'
}
