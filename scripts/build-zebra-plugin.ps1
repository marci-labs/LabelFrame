# 构建 Zebra 品牌传输官方插件包（.lfplugin，迭代 63 / Issue #56，决策 #123，DESIGN §6.8）
# 产物：artifacts\labelframe-transport-zebra-<版本>.lfplugin（随 Release 附件发布 + 客户端 MSI 附带）
# 版本随主版本演进（发版流水线以当版主版本构建）；包 = zip（根 manifest.json + 插件 DLL + 伴生依赖，64MB 上限实测断言）。
# 兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7（GitHub Actions runner）。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$pluginId = 'labelframe-transport-zebra'
$staging = Join-Path $root 'artifacts\zebra-plugin-staging'
$packagePath = Join-Path $OutputDir "$pluginId-$Version.lfplugin"

# 1) 发布插件工程（framework-dependent：宿主 WinHost 提供运行时与 LabelFrame.Core / SkiaSharp 等共享程序集）
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
dotnet publish (Join-Path $root 'src\LabelFrame.TransportPlugin.Zebra\LabelFrame.TransportPlugin.Zebra.csproj') `
    -c $Configuration -f net10.0-windows10.0.26100 -r win-x64 -p:SelfContained=false `
    -o $staging -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Zebra 插件工程 publish 失败' }

# 2) 收集程序集：发布产物根目录全部 *.dll，排除「宿主必带」程序集（经默认 ALC 回退解析，进包只增体积）
#    - LabelFrame.Core.dll：宿主核心库（PluginDirectoryLoader 依赖解析优先回退宿主默认上下文；类型统一必须由宿主提供）
#    - Core 的 NuGet 依赖（Sqlite / OpenXml / TemplateFrame）：宿主随 Core 一并提供，插件代码零引用
#    - libSkiaSharp.dll：宿主经 LabelFrame.Rendering 随附的原生库（SDK 图形工具引用，插件不单独携带）
#    - runtimes\ 子目录（RID 特定发布已扁平化到根；残余目录不进包——包内伴生 DLL 须与插件 DLL 同目录）
#    注意：Zebra SDK 自身依赖（ZebraPrinterSdk / SdkApi.* / BouncyCastle / FluentFTP / SharpSnmpLib / Newtonsoft.Json /
#    System.Drawing.Common 及其 Windows 投影桥 Microsoft.Windows.SDK.NET 等）全部保留——插件 ALC 须能自解析。
$hostProvided = @(
    'LabelFrame.Core.dll',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll',
    'System.IO.Packaging.dll',
    'TemplateFrame.dll',
    'TemplateFrame.Excel.Simple.dll',
    'libSkiaSharp.dll'
)
$dlls = @(Get-ChildItem -Path $staging -File -Filter '*.dll' | Where-Object { $hostProvided -notcontains $_.Name })
if ($dlls.Count -eq 0) { throw '发布产物未找到任何 DLL' }
$mainDll = $dlls | Where-Object { $_.Name -eq 'LabelFrame.TransportPlugin.Zebra.dll' }
if (-not $mainDll) { throw '发布产物缺少 LabelFrame.TransportPlugin.Zebra.dll' }
$sdkDlls = @($dlls | Where-Object { $_.Name -in @('ZebraPrinterSdk.dll', 'SdkApi.Core.dll', 'SdkApi.Desktop.dll', 'SdkApi.Desktop.Usb.dll') })
if ($sdkDlls.Count -eq 0) { throw '发布产物缺少 Zebra SDK 程序集（ZebraPrinterSdk / SdkApi.*）——检查还原与排除清单' }

# 3) manifest.json（格式对齐迭代 23 的 .lfplugin 契约：pluginId / name / version 必填）
$manifestJson = (@(
    '{'
    '  "pluginId": "' + $pluginId + '",'
    '  "name": "Zebra 品牌传输（官方）",'
    '  "version": "' + $Version + '",'
    '  "description": "Zebra 官方 Link-OS SDK 传输（TCP / USB / Windows 驱动连接与打印机状态）；随 LabelFrame 主版本演进的官方插件。",'
    '  "author": "LabelFrame"'
    '}'
) -join "`n") + "`n"

# 4) 打包（zip 根：manifest.json + DLL；UTF-8 无 BOM）
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($packagePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $manifestEntry = $zip.CreateEntry('manifest.json')
    $entryStream = $manifestEntry.Open()
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($manifestJson)
    $entryStream.Write($bytes, 0, $bytes.Length)
    $entryStream.Dispose()
    foreach ($dll in $dlls) {
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $dll.FullName, $dll.Name, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $zip.Dispose()
}

# 5) 断言：体积 < 64MB（.lfplugin 上限，PluginPackageLimits）；manifest 可回读
$package = Get-Item -LiteralPath $packagePath
if ($package.Length -ge 64MB) { throw "插件包超过 64MB 上限（$([Math]::Round($package.Length / 1MB, 1)) MB）——检查是否误入宿主程序集 / 原生资产" }

Write-Host "Zebra 官方插件包已生成：$packagePath"
Write-Host "大小：$([Math]::Round($package.Length / 1MB, 2)) MB（$($package.Length) 字节，上限 64MB）"
Write-Host "内容：manifest.json + $($dlls.Count) 个 DLL（含 $($sdkDlls.Count) 个 Zebra SDK 程序集：$($sdkDlls.Name -join ' / ')）"
