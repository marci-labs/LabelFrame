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
    # AndroidFastDeployment=false：程序集打包进 APK，产物可脱离开发环境独立安装
    # （默认 Fast Deployment 只装壳到设备、程序集放 files/.__override__/，纯 adb install 会启动 abort）。
    dotnet build src\LabelFrame.AndroidHost\LabelFrame.AndroidHost.csproj -p:AndroidSdkDirectory="$sdk" -p:AndroidFastDeployment=false
    if ($LASTEXITCODE -ne 0) { throw 'AndroidHost 构建失败。' }
    $apk = Get-ChildItem src\LabelFrame.AndroidHost\bin\Debug\net10.0-android\*-Signed.apk | Select-Object -First 1
    Write-Host "构建成功：$($apk.FullName)"
}
finally {
    Pop-Location
}