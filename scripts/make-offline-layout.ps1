# 生成 LabelFrame 离线布局目录（迭代 70，Issue #89；契约 = docs/DESIGN.md §6.2 / §6.10，决策 #135）
# 用途：IT 在有网机器预下载当版全部组件 + install-manifest.json + latest.json + 引导 EXE 到一个目录，
#       拷贝（U 盘 / 内网共享）分发后，目标机运行目录中的引导 EXE 即无外网完成首装（VS layout 式）。
# 语义与引导程序 --layout 模式（核心库 OfflineLayoutBuilder）一致：
#   - 组件文件名 = urls[0] 路径末段（须含扩展名）；查询型直链按固定名兜底（runtime-webview2）；
#   - 逐组件按 urls 顺序下载，sha256 与 manifest 逐字节一致才落位（不符换下一源，全源失败 exit 1，fail-closed）；
#   - 重复生成幂等（已存在且哈希一致跳过下载，不符重新下载）；manifest / latest 原样字节落盘。
# 引导 EXE 来源（迭代 68 起随 Release 发布，决策 #132）：-BootstrapperPath 指定本地产物 >
#   本地 artifacts\LabelFrame-Bootstrapper-<版本>.exe > -BootstrapperUrl（默认当版 Release 地址）下载；
#   三者均不可得即失败（布局目录必须内含引导 EXE，fail-closed）。EXE 无 manifest 哈希条目（它本身即
#   布局生成的载体），下载通道 = Release 同信道（TLS + 发版 tag 不可变，#117 残余风险口径）。
#   在线生成用引导程序自身（setup.exe --layout <目录>）时 EXE 自动自复制，无需本参数。
#
# 用法（仓库根目录，Windows PowerShell 5.1+ / PowerShell 7）：
#   稳定通道（最新版）：  powershell -ExecutionPolicy Bypass -File scripts\make-offline-layout.ps1 -OutputDir D:\labelframe-layout
#   指定版本：            ... -Version 0.28.0 -OutputDir D:\layout ...
#   本地产物 EXE：        ... -BootstrapperPath C:\dist\LabelFrame-Bootstrapper-0.28.0.exe ...
#   本地清单来源（测试）： ... -ManifestSource C:\test\install-manifest.json -OutputDir D:\layout ...
param(
    [string]$Version = '',
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$ManifestSource = '',
    [string]$BootstrapperPath = '',
    [string]$BootstrapperUrl = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stableManifestUrl = 'https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json'

function Write-Failure([string]$Message) {
    Write-Host "::error::$Message"
    Write-Host "失败：$Message" -ForegroundColor Red
    exit 1
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Save-HttpResponse([string]$Url, [string]$TargetPath) {
    # 5.1 / 7 兼容下载（-UseBasicParsing 在 7 为无操作参数但合法）
    Invoke-WebRequest -Uri $Url -OutFile $TargetPath -UseBasicParsing
}

function Read-SourceText([string]$Source) {
    # 本地路径 = 文件读取；URL = 单次只读 GET（与引导 InstallManifestLoader 同口径）
    if ($Source -match '^https?://') {
        $client = New-Object System.Net.WebClient
        try { return $client.DownloadString($Source) } finally { $client.Dispose() }
    }
    [IO.File]::ReadAllText($Source)
}

# 组件布局文件名推导（与核心库 OfflineLayoutNaming 同约定：urls[0] 末段含扩展名 → 固定名兜底 → 不可推导）
$fixedNames = @{ 'runtime-webview2' = 'MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe' }
function Get-LayoutFileName([object]$Component) {
    $url = @($Component.urls)[0]
    if ($url) {
        try {
            $uri = [Uri]$url
            if ($uri.Scheme -in @('http', 'https') -and $uri.Segments.Length -gt 0) {
                $segment = $uri.Segments[$uri.Segments.Length - 1].TrimEnd('/')
                if ($segment -and $segment.IndexOf('.') -ge 1 -and $segment -notmatch '[\\/:*?"<>|]') {
                    return [Uri]::UnescapeDataString($segment)
                }
            }
        } catch {
        }
    }
    if ($fixedNames.ContainsKey($Component.id)) { return $fixedNames[$Component.id] }
    return $null
}

# ---- 1) 清单来源解析与加载 ----
if (-not $ManifestSource) {
    if ($Version) {
        $ManifestSource = "https://github.com/marci-labs/LabelFrame/releases/download/v$Version/install-manifest.json"
    } else {
        $ManifestSource = $stableManifestUrl
    }
}

Write-Host "清单来源：$ManifestSource"
try {
    $manifestJson = Read-SourceText $ManifestSource
    $manifest = $manifestJson | ConvertFrom-Json
} catch {
    Write-Failure "清单加载失败（$ManifestSource）：$($_.Exception.Message)"
}

# ---- 2) latest.json 尽力获取（指针文件可选；失败跳过不阻断）----
$latestJson = $null
$latestSource = $null
if ($ManifestSource -match '^https?://') {
    if ($ManifestSource -like '*/releases/latest/download/install-manifest.json') {
        $latestSource = $ManifestSource -replace 'install-manifest\.json$', 'latest.json'
    }
} else {
    $latestSource = Join-Path (Split-Path -Parent $ManifestSource) 'latest.json'
    if (-not (Test-Path -LiteralPath $latestSource)) { $latestSource = $null }
}
if ($latestSource) {
    try { $latestJson = Read-SourceText $latestSource } catch { Write-Host "latest.json 获取失败（跳过，不阻断）：$($_.Exception.Message)" }
}

# ---- 3) 引导 EXE 来源（迭代 68 起随 Release 发布，决策 #132：-BootstrapperPath 本地产物 > 本地 artifacts 缺省 > Release 下载）----
$manifestVersion = $manifest.labelframeVersion
$bootstrapperFileName = "LabelFrame-Bootstrapper-$manifestVersion.exe"
if (-not $BootstrapperPath) {
    $localDefault = Join-Path $root "artifacts\$bootstrapperFileName"
    if (Test-Path -LiteralPath $localDefault) {
        $BootstrapperPath = $localDefault
        Write-Host "引导 EXE 使用本地产物：$BootstrapperPath"
    }
}
if (-not $BootstrapperPath) {
    # 本地无产物：从 Release 下载当版引导 EXE（EXE 无 manifest 哈希条目——它本身即布局生成载体，
    # 信任口径 = Release 同信道：TLS + 发版 tag 不可变，#117 残余风险；与用户手动下载引导 EXE 等价）
    if (-not $BootstrapperUrl) {
        $BootstrapperUrl = "https://github.com/marci-labs/LabelFrame/releases/download/v$manifestVersion/$bootstrapperFileName"
    }
    $tempExe = Join-Path ([IO.Path]::GetTempPath()) "$bootstrapperFileName.download"
    try {
        Write-Host "下载引导 EXE：$BootstrapperUrl"
        Save-HttpResponse $BootstrapperUrl $tempExe
        $BootstrapperPath = $tempExe
    }
    catch {
        Write-Failure "引导 EXE 获取失败（本地 artifacts\$bootstrapperFileName 不在，Release 下载失败：$($_.Exception.Message)）——布局目录必须内含引导 EXE；或以 -BootstrapperPath 指定本机构建产物（scripts\build-bundle.ps1 -Version $manifestVersion 可生成）。"
    }
}

# ---- 4) 逐组件获取（urls 顺序回退 + sha256 fail-closed + 幂等复用）----
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$downloaded = 0
$reused = 0
foreach ($component in @($manifest.components)) {
    $fileName = Get-LayoutFileName $component
    if (-not $fileName) {
        Write-Failure "组件 $($component.id) 无法推导布局文件名（urls[0] 无可辨识文件名且不在固定名兜底表）——布局目录必须收录全部组件。"
    }

    $targetPath = Join-Path $OutputDir $fileName
    if (Test-Path -LiteralPath $targetPath) {
        $existing = Get-Sha256 $targetPath
        if ($existing -eq $component.sha256) {
            Write-Host "复用（哈希一致）：[$($component.id)] $fileName"
            $reused++
            continue
        }
        Write-Host "已存在但哈希不符（篡改 / 旧版残留），重新下载：[$($component.id)] $fileName"
    }

    $tempPath = "$targetPath.download"
    $failures = @()
    $acquired = $false
    foreach ($url in @($component.urls)) {
        try {
            Save-HttpResponse $url $tempPath
        } catch {
            $failures += "$url → $($_.Exception.Message)"
            continue
        }
        $actual = Get-Sha256 $tempPath
        if ($actual -ne $component.sha256) {
            $failures += "$url → 哈希不符（manifest=$($component.sha256.Substring(0,12))… 实测=$($actual.Substring(0,12))…）"
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            continue
        }
        if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Force }
        Move-Item -LiteralPath $tempPath -Destination $targetPath
        Write-Host "下载完成（哈希校验通过）：[$($component.id)] $fileName ← $url"
        $acquired = $true
        break
    }
    if (-not $acquired) {
        Write-Failure "组件 $($component.id) 全部 $(@($component.urls).Count) 个源获取失败（sha256 与 manifest 一致才落位，fail-closed）：$($failures -join '；')"
    }
    $downloaded++
}

# ---- 5) manifest / latest 原样落盘 + 引导 EXE 复制 ----
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $OutputDir 'install-manifest.json'), $manifestJson, $utf8NoBom)
Write-Host "已写入：install-manifest.json（官方原样字节，LabelFrame $manifestVersion）"
if ($latestJson) {
    [IO.File]::WriteAllText((Join-Path $OutputDir 'latest.json'), $latestJson, $utf8NoBom)
    Write-Host '已写入：latest.json（官方原样字节）'
}

$bootstrapperTarget = Join-Path $OutputDir $bootstrapperFileName
Copy-Item -LiteralPath $BootstrapperPath -Destination $bootstrapperTarget -Force
Write-Host "已复制：引导 EXE → $bootstrapperTarget"

$total = @($manifest.components).Count
Write-Host ""
Write-Host "布局目录生成完成：$OutputDir" -ForegroundColor Green
Write-Host "组件 $total 个（下载 $downloaded / 复用 $reused）+ install-manifest.json$(if ($latestJson) { ' + latest.json' }) + 引导 EXE。"
Write-Host '把整个目录拷贝到目标机器，直接运行其中的引导程序即可离线安装（清单与组件取自本地目录，全程无需联网）。'
