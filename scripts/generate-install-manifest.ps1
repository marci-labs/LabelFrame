# 生成安装清单 install-manifest.json 与最新版本指针 latest.json（迭代 58，Issue #51）
# 契约：docs/DESIGN.md §6.2（决策 #115 schema、#120 实现决议）；manifest 由 CI 生成、禁止人工维护。
# 生成点：.github/workflows/release.yml 的 release job（下载当版全部产物后、创建 Release 前）。
# 兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7（GitHub Actions runner）。
#
# 用法：
#   生成 + 断言（CI 路径）：
#     pwsh ./scripts/generate-install-manifest.ps1 -Version 0.26.0 -AssetsDir release -OwnerRepo marci-labs/LabelFrame
#   仅断言既有清单（AC-03 破坏性用例复验）：
#     pwsh ./scripts/generate-install-manifest.ps1 -Version 0.26.0 -AssetsDir release -VerifyOnly
#   仅生成不断言（本地实验；CI 不得使用 -SkipVerify）：
#     pwsh ./scripts/generate-install-manifest.ps1 -Version 0.26.0 -AssetsDir release -SkipVerify
#
# 断言粒度（用户 2026-09-14 决议，Issue #51）：schema 字段完整性 + 产物存在性 + 哈希抽验
# （哈希按条目全量重算比对，5 个产物成本可忽略，覆盖且强于抽样）；
# 缺产物 / 缺哈希 / schema 不符任一命中即非零退出，workflow 构建失败（AC-03）。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$AssetsDir = 'release',
    [string]$OwnerRepo = 'marci-labs/LabelFrame',
    [string]$OutputDir = '',
    [switch]$VerifyOnly,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'

# 当版产物 -> manifest 条目映射（组件稳定 id 与字段语义见 DESIGN §6.2 组件条目表）。
# runtime / plugin-<brand> 条目按 schema 预留、无产物不出现：runtime（厂商直链 + CI 锁哈希）随专项 5/8（#55）
# 运行时前置补装落地，官方插件随专项 7/8（#56）外置化落地，届时在下方追加条目即可。
# dependsOn 首版全部留空：仅引用同集合内存在的条目，runtime 条目未出现前不引用（§6.2 依赖语义），
# runtime 条目落地时补 dependsOn（server-msi -> runtime-desktop、client-msi -> runtime-desktop + runtime-webview2）。
$componentSpecs = @(
    @{ id = 'server-msi';   type = 'msi';       pattern = "LabelFrame-Server-$Version.msi";               topologies = @('standalone', 'server-win', 'offline'); notes = '服务端（Windows 服务 LabelFrameServer）' }
    @{ id = 'client-msi';   type = 'msi';       pattern = "LabelFrame-Client-$Version.msi";               topologies = @('standalone', 'client', 'offline');      notes = '打印客户端（Web UI 托管 + 界面壳 + 托盘）' }
    @{ id = 'webui';        type = 'webui-zip'; pattern = "labelframe-server-webui-$Version.zip";        topologies = @('standalone', 'server-win', 'server-linux', 'offline'); notes = '服务端管理界面插件（开关组件，落位 plugins/web-ui）' }
    @{ id = 'linux-server'; type = 'archive';   pattern = "labelframe-server-$Version-linux-x64.tar.gz"; topologies = @('server-linux', 'offline');             notes = 'Linux 服务端归档（systemd 部署）' }
    @{ id = 'pda-apk';      type = 'apk';       pattern = "LabelFrame-AndroidHost-$Version.apk";          topologies = @('pda', 'offline');                      notes = 'PDA 宿主 APK（拓扑标记 pda：经服务端下载中心扫码下载，不进 PC 引导预设）' }
)

$schemaVersion = 1
$allowedTypes = @('msi', 'lfplugin', 'webui-zip', 'apk', 'runtime', 'archive')
$allowedTopologies = @('standalone', 'server-win', 'server-docker', 'server-linux', 'client', 'offline', 'pda')

if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = $AssetsDir }
$manifestPath = Join-Path $OutputDir 'install-manifest.json'
$latestPath = Join-Path $OutputDir 'latest.json'
$tag = "v$Version"
$releaseBase = "https://github.com/$OwnerRepo/releases/download/$tag"
# 产物存在性 / 哈希抽验只对本仓 Release 附件源执行；runtime 条目（未来）指向厂商直链，不适用本地复核
$releaseUrlPrefix = "https://github.com/$OwnerRepo/releases/download/"

function Write-Failure([string]$Message) {
    # GitHub Actions 注解要求单行；本地自验时退出码即判据
    Write-Host "::error::$Message"
    Write-Host "失败：$Message" -ForegroundColor Red
    exit 1
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-JsonString([string]$Value) {
    # 手写最小 JSON 序列化：跨 5.1 / 7 输出一致（ConvertTo-Json 在 5.1 会转义中文且格式不同）
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $Value.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($code -eq 8) { [void]$sb.Append('\b') }
        elseif ($code -eq 12) { [void]$sb.Append('\f') }
        elseif ($code -eq 10) { [void]$sb.Append('\n') }
        elseif ($code -eq 13) { [void]$sb.Append('\r') }
        elseif ($code -eq 9) { [void]$sb.Append('\t') }
        elseif ($code -lt 32) { [void]$sb.Append(('\u{0:x4}' -f $code)) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    $sb.ToString()
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    # UTF-8 无 BOM 落盘（5.1 的 Out-File -Encoding utf8 带 BOM，引导程序 JSON 解析不假设 BOM）
    [IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

# ---- 生成 ----
if (-not $VerifyOnly) {
    if (-not (Test-Path -LiteralPath $OutputDir)) {
        New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    }

    $entryJsons = New-Object System.Collections.Generic.List[string]
    foreach ($spec in $componentSpecs) {
        $full = Join-Path $AssetsDir $spec.pattern
        $file = Get-Item -LiteralPath $full -ErrorAction SilentlyContinue
        if ($null -eq $file) {
            Write-Failure "缺产物：组件 $($spec.id) 对应文件不存在（$full）——manifest 只收录当版真实产物，禁止空条目"
        }
        $url = "$releaseBase/$($file.Name)"
        # 注意：Windows PowerShell 5.1 会把数组字面量内的裸 + 拼接拆成多个元素，必须用括号包裹为单个表达式
        $entryJsons.Add(@(
            '    {'
            ('      "id": ' + (ConvertTo-JsonString $spec.id) + ',')
            ('      "type": ' + (ConvertTo-JsonString $spec.type) + ',')
            ('      "version": ' + (ConvertTo-JsonString $Version) + ',')
            '      "dependsOn": [],'
            ('      "urls": [' + (ConvertTo-JsonString $url) + '],')
            ('      "sha256": ' + (ConvertTo-JsonString (Get-Sha256 $file.FullName)) + ',')
            ('      "sizeBytes": ' + $file.Length.ToString() + ',')
            '      "silentArgs": "",'
            ('      "topologies": [' + (($spec.topologies | ForEach-Object { ConvertTo-JsonString $_ }) -join ', ') + '],')
            ('      "notes": ' + (ConvertTo-JsonString $spec.notes))
            '    }'
        ) -join "`n")
    }

    $generatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $manifestJson = (@(
        '{'
        ('  "schemaVersion": ' + $schemaVersion + ',')
        ('  "labelframeVersion": ' + (ConvertTo-JsonString $Version) + ',')
        ('  "generatedAt": ' + (ConvertTo-JsonString $generatedAt) + ',')
        '  "components": ['
        ($entryJsons -join ",`n")
        '  ]'
        '}'
    ) -join "`n") + "`n"
    Write-Utf8NoBom $manifestPath $manifestJson

    $latestJson = (@(
        '{'
        ('  "labelframeVersion": ' + (ConvertTo-JsonString $Version) + ',')
        ('  "manifestUrl": ' + (ConvertTo-JsonString "$releaseBase/install-manifest.json"))
        '}'
    ) -join "`n") + "`n"
    Write-Utf8NoBom $latestPath $latestJson

    Write-Host "已生成：$manifestPath"
    Write-Host "已生成：$latestPath"
}

# ---- 断言（AC-03：字段完整性 + 产物存在性 + 哈希抽验；独立重读落盘文件，生成与断言互不共享中间态）----
$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Failure "install-manifest.json 不存在：$manifestPath"
}
if (-not (Test-Path -LiteralPath $latestPath)) {
    Write-Failure "latest.json 不存在：$latestPath"
}

$manifest = $null
$latest = $null
try { $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { Write-Failure "install-manifest.json 不是合法 JSON：$($_.Exception.Message)" }
try { $latest = Get-Content -LiteralPath $latestPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { Write-Failure "latest.json 不是合法 JSON：$($_.Exception.Message)" }

# 顶层字段完整性
if ($manifest.schemaVersion -ne $schemaVersion) { $failures.Add("schemaVersion 应为 $schemaVersion，实际：$($manifest.schemaVersion)") }
if ([string]::IsNullOrWhiteSpace($manifest.labelframeVersion)) { $failures.Add('缺顶层必填字段 labelframeVersion') }
elseif ($manifest.labelframeVersion -ne $Version) { $failures.Add("labelframeVersion（$($manifest.labelframeVersion)）与版本号 $Version 不一致") }
if ([string]::IsNullOrWhiteSpace($manifest.generatedAt)) { $failures.Add('缺顶层必填字段 generatedAt') }

$components = @($manifest.components)
if ($components.Count -eq 0) { $failures.Add('components 为空数组') }

# 组件集合完整性（恰好当版预期集合：缺 = 缺产物 / 漏收录，多 = schema 不符）
$expectedIds = @($componentSpecs | ForEach-Object { $_.id })
$actualIds = @($components | ForEach-Object { $_.id })
foreach ($id in @($expectedIds | Where-Object { $actualIds -notcontains $_ })) {
    $failures.Add("缺组件条目：$id（当版应有产物未收录或产物缺失）")
}
foreach ($id in @($actualIds | Where-Object { $expectedIds -notcontains $_ })) {
    $failures.Add("多出组件条目：$id（不在当版预期集合）")
}

foreach ($c in $components) {
    $cid = if ($c.id) { $c.id } else { '<无 id>' }

    # 字段完整性（必填：id / type / version / urls / sha256 / sizeBytes / topologies）
    if ([string]::IsNullOrWhiteSpace($c.id)) { $failures.Add("组件条目缺必填字段 id") }
    if ([string]::IsNullOrWhiteSpace($c.type)) { $failures.Add("[$cid] 缺必填字段 type") }
    elseif ($allowedTypes -notcontains $c.type) { $failures.Add("[$cid] type 非法：$($c.type)（允许：$($allowedTypes -join ' / '))") }
    if ([string]::IsNullOrWhiteSpace($c.version)) { $failures.Add("[$cid] 缺必填字段 version") }

    $urls = @($c.urls | Where-Object { $_ })
    if ($urls.Count -eq 0) { $failures.Add("[$cid] 缺必填字段 urls 或为空数组（多源数组首期至少含主源）") }

    $topologies = @($c.topologies | Where-Object { $_ })
    if ($topologies.Count -eq 0) { $failures.Add("[$cid] 缺必填字段 topologies 或为空数组") }
    foreach ($t in $topologies) {
        if ($allowedTopologies -notcontains $t) { $failures.Add("[$cid] topologies 含未知预设 id：$t（允许：$($allowedTopologies -join ' / '))") }
    }

    foreach ($dep in @($c.dependsOn)) {
        if ($actualIds -notcontains $dep) { $failures.Add("[$cid] dependsOn 引用不存在的组件：$dep（依赖只约束同集合内组件）") }
    }

    # 哈希：必填、小写 64 位 hex（缺哈希场景在此拦截）
    $sha = $c.sha256
    if ([string]::IsNullOrWhiteSpace($sha)) { $failures.Add("[$cid] 缺必填字段 sha256（哈希强制，CI 实测）") }
    elseif ($sha -notmatch '^[0-9a-f]{64}$') { $failures.Add("[$cid] sha256 非小写 64 位 hex：$sha") }

    # sizeBytes：必填、正整数
    $size = $c.sizeBytes
    if ($null -eq $size) { $failures.Add("[$cid] 缺必填字段 sizeBytes") }
    elseif (-not (($size -is [int]) -or ($size -is [long]) -or ($size -is [double]))) { $failures.Add("[$cid] sizeBytes 应为数字，实际：$size") }
    elseif ([double]$size -le 0 -or [Math]::Floor([double]$size) -ne [double]$size) { $failures.Add("[$cid] sizeBytes 应为正整数：$size") }

    # 产物存在性 + 哈希抽验（仅本仓 Release 附件源；URL 形态校验对所有源生效）
    foreach ($url in $urls) {
        if ($url -notmatch '^https://') { $failures.Add("[$cid] urls 含非 https 源：$url") }
        if ($url.StartsWith($releaseUrlPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $fileName = ($url -split '/')[-1]
            if ([string]::IsNullOrWhiteSpace($fileName)) {
                $failures.Add("[$cid] Release 源 URL 缺文件名：$url")
                continue
            }
            $local = Join-Path $AssetsDir $fileName
            if (-not (Test-Path -LiteralPath $local)) {
                $failures.Add("[$cid] 产物不存在：$fileName（URL 指向的附件在产物目录中缺失）")
            }
            elseif (-not [string]::IsNullOrWhiteSpace($sha)) {
                $actual = Get-Sha256 $local
                if ($actual -ne $sha) { $failures.Add("[$cid] 哈希不符（抽验拦截）：$fileName manifest=$sha 实测=$actual") }
                else { Write-Host "哈希复核通过：[$cid] $fileName（$sha）" }
            }
        }
    }
}

# latest.json：版本号 + 当版 manifest URL
if ($latest.labelframeVersion -ne $Version) { $failures.Add("latest.json labelframeVersion（$($latest.labelframeVersion)）与版本号 $Version 不一致") }
if ($latest.manifestUrl -ne "$releaseBase/install-manifest.json") { $failures.Add("latest.json manifestUrl 应为 $releaseBase/install-manifest.json，实际：$($latest.manifestUrl)") }

if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "::error::$f" }
    Write-Failure "断言未通过：$($failures.Count) 项（schema 字段完整性 / 产物存在性 / 哈希抽验）——见上方逐条错误"
}

Write-Host "断言通过：install-manifest.json（$($components.Count) 组件）字段完整性 / 产物存在性 / 哈希复核全部一致；latest.json 指针正确（$Version）。"
