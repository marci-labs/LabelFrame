# 构建 LabelFrame.AndroidHost（需要 Android workload / SDK / JDK17）
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# 1) 查找 JDK：优先 JAVA_HOME，其次 ~/.jdk
if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin\java.exe'))) {
    $jdk = Get-ChildItem "$env:USERPROFILE\.jdk" -Recurse -Filter java.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($jdk) { $env:JAVA_HOME = $jdk.DirectoryName.Substring(0, $jdk.DirectoryName.Length - 4) }
}
if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin\java.exe'))) {
    throw '未找到 JDK 17，请安装并设置 JAVA_HOME。'
}

# 2) Android SDK
$sdk = 'C:\Program Files (x86)\Android\android-sdk'
if (-not (Test-Path $sdk)) {
    $sdk = $env:ANDROID_HOME
}
if (-not $sdk -or -not (Test-Path $sdk)) {
    throw '未找到 Android SDK，请安装并设置 ANDROID_HOME。'
}

Write-Host "JAVA_HOME=$env:JAVA_HOME"
Write-Host "Android SDK=$sdk"

Push-Location $repo
try {
    dotnet build src\LabelFrame.AndroidHost\LabelFrame.AndroidHost.csproj -p:AndroidSdkDirectory="$sdk"
    if ($LASTEXITCODE -ne 0) { throw 'AndroidHost 构建失败。' }
    $apk = Get-ChildItem src\LabelFrame.AndroidHost\bin\Debug\net10.0-android\*-Signed.apk | Select-Object -First 1
    Write-Host "构建成功：$($apk.FullName)"
}
finally {
    Pop-Location
}