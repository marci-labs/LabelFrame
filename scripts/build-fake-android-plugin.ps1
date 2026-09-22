# 构建 fake 传输插件包（.lfplugin，迭代 96 / Issue #185 AC-02 Spike 资产，决策 #156，DESIGN §6.8）
# 产物：artifacts\labelframe-transport-fake-<版本>.lfplugin（manifest platforms:["android"]——PDA 外置插件通道专用；
# 纯托管轻量档：Release AOT 宿主内动态加载走 MonoVM interpreter 解释执行，Spike 实证目标）。
# 真机走查脚本：scripts/test-pda-plugin-spike.ps1（本脚本为其第 1 步，也可独立使用）。
# 兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7（CI 同型环境）。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$pluginId = 'labelframe-transport-fake'
$staging = Join-Path $root 'artifacts\fake-plugin-staging'
$packagePath = Join-Path $OutputDir "$pluginId-$Version.lfplugin"

# 1) 发布插件工程（framework-dependent：宿主提供运行时与 LabelFrame.Core 等共享程序集——类型统一必须由宿主提供）
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
dotnet publish (Join-Path $root 'test\LabelFrame.TransportPlugin.Fake\LabelFrame.TransportPlugin.Fake.csproj') `
    -c $Configuration -f net10.0 -p:SelfContained=false `
    -o $staging -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'fake 插件工程 publish 失败' }

# 2) 收集程序集：排除「宿主必带」程序集（与 build-zebra-plugin.ps1 同清单——WinHost / AndroidHost 均随 Core 附带；
#    经插件 ALC 默认上下文回退解析，进包只增体积并威胁类型统一）
$hostProvided = @(
    'LabelFrame.Core.dll',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll',
    'System.IO.Packaging.dll',
    'TemplateFrame.dll',
    'TemplateFrame.Excel.Simple.dll'
)
$dlls = @(Get-ChildItem -Path $staging -File -Filter '*.dll' | Where-Object { $hostProvided -notcontains $_.Name })
if ($dlls.Count -eq 0) { throw '发布产物未找到任何 DLL' }
$mainDll = $dlls | Where-Object { $_.Name -eq 'LabelFrame.TransportPlugin.Fake.dll' }
if (-not $mainDll) { throw '发布产物缺少 LabelFrame.TransportPlugin.Fake.dll' }

# 3) manifest.json（.lfplugin 契约 + 决策 #156 跨端标记：platforms:["android"]——PDA 平台门放行、Windows 端拒绝）
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
$zip = [System.IO.Compression.ZipFile]::Open($packagePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $manifestEntry = $zip.CreateEntry('manifest.json')
    $manifestJson = '{"pluginId":"labelframe-transport-fake","name":"假想品牌（通道验证）","version":"' + $Version + '","description":"PDA 外置插件通道 Spike：fake 传输（发送内容记录到日志与数据目录，不连接真实打印机）","platforms":["android"]}'
    $writer = New-Object System.IO.StreamWriter($manifestEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    try { $writer.Write($manifestJson) } finally { $writer.Dispose() }

    foreach ($dll in $dlls) {
        $entry = $zip.CreateEntry($dll.Name)
        $target = $entry.Open()
        try { [System.IO.File]::OpenRead($dll.FullName).CopyTo($target) } finally { $target.Dispose() }
    }
}
finally { $zip.Dispose() }

$sizeMb = [math]::Round((Get-Item -LiteralPath $packagePath).Length / 1MB, 2)
if ((Get-Item -LiteralPath $packagePath).Length -gt 64MB) { throw "插件包超过 64MB 上限（$sizeMb MB）" }
Write-Host "已构建 fake 插件包：$packagePath（$sizeMb MB，含 $($dlls.Count) 个程序集，platforms=android）"
