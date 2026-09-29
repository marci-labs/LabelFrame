#Requires -Version 7.0
<#
.SYNOPSIS
    全仓文案盘点（迭代 107 / #240；决策 #164 落地路径⑧「治理迭代」产物）。

.DESCRIPTION
    扫描 web/src（*.ts / *.tsx，排除 *.test.* / *.spec.* 与 __tests__ / __mocks__）与
    src（*.cs，排除 bin / obj 与测试文件），把含 CJK 文本的条目按三类启发式分类：

      1. 注释            —— // 行注释（含 /// XML doc）与 /* */ 块注释；
      2. 日志与诊断      —— 字面量处于日志调用语境（[LoggerMessage(Message =]、ILogger 扩展、
                            Serilog 静态 Log.*、HostLog.*、Console.Write*、Debug/Trace.WriteLine、
                            console.*）；
      3. 用户可见 UI 文案 —— 其余字符串字面量（含 API 错误消息、原生弹窗 / 抛错消息等）；
                            .tsx 中字符串 / 注释之外的 CJK 文本（JSX 文本节点）计入本类。

    计数口径（三类一致）：
      - 「条」 = 内容剥去 {…} / ${…} 占位后，极大连续 CJK 文案段数
                （CJK 汉字 / CJK 标点 / 全角形式，段内允许空白；至少含 1 个汉字）；
      - 「汉字」 = CJK 统一表意字符数（U+3400–4DBF、U+4E00–9FFF、兼容区 U+F900–FAFF）。

    解析方式为单遍组合正则（注释与字符串互斥、左优先），非完整语法分析；
    解析盲区（如 C# 插值孔内含引号的字面量）漏出的 CJK 文本按「残留」捕获并依据同一
    日志语境启发式并入日志 / UI 两类（详见输出「口径与局限」）。
    本脚本只读仓库、不改任何文件，可重复运行、无需人工干预。

.PARAMETER Root
    仓库根目录（默认：脚本所在 scripts/ 的上一级）。

.PARAMETER Top
    「Top 重灾区」列出的文件数（按 UI 条数降序，默认 20）。

.PARAMETER OutputFile
    若指定，统计表另存为该 Markdown 文件（UTF-8 无 BOM）；同时仍输出到标准输出。

.EXAMPLE
    pwsh -NoProfile -File scripts/inventory-ui-text.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [int]$Top = 20,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'

# 控制台输出统一 UTF-8（中文在重定向 / 非 UTF-8 控制台下不乱码；无控制台宿主时忽略）。
try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch { }

# ---------- 口径常量 ----------
$IdeoTest = '[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]'
$SegmentCharClass = '\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKUnifiedIdeographs}\p{IsCJKCompatibility}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}\u2014\u2015\u2026\u00b7'
$SegmentPattern = "(?:[$SegmentCharClass]\s*)+"
$PlaceholderPattern = '\$\{[^{}]*\}|\{[^{}]*\}'

# 组合正则：注释（cm）与字符串字面量（str）互斥提取，左优先匹配；chr = 字符字面量（'x'，含 '"'，
# 防止其中的引号被误当字符串起点；字符字面量本身不计文案）。
# C# 字符串覆盖："""raw""" / @$"…" / $@"…" / @"…" / $"…" / "…"（普通串限单行，防未闭合吞噬全文）。
$CsPattern = '(?s)(?<cm>/\*.*?\*/|//[^\n]*)|(?<chr>''(?:\\.|[^''\\])'')|(?<str>"""(?:[^"]|"(?!""))*"""|[@$]{1,2}"(?:[^"]|"")*"|"(?:\\.|[^"\\\n])*")'
# TS/TSX 字符串覆盖：`template`（可跨行、内嵌 ${…} 与引号）/ 'single' / "double"（普通串限单行）。
$TsPattern = '(?s)(?<cm>/\*.*?\*/|//[^\n]*)|(?<str>`(?:\\.|[^`\\])*`|''(?:\\.|[^''\\\n])*''|"(?:\\.|[^"\\\n])*")'
# 日志调用语境（匹配「上一事件结束 → 本字面量开始」之间的间隙文本）。
$LogGapPattern = '(?i)(?:^|[\s;{}\[]\s*)(?:\[\s*LoggerMessage\b[^\]\[]*\bMessage\s*=\s*$|[\w.]*(?:logger|hostlog|console|debug|log|trace)[\w.]*\s*\.\s*(?:LogInformation|LogWarning|LogError|LogDebug|LogCritical|LogTrace|Information|Warning|Error|Debug|Fatal|Info|Warn|WriteLine|Write|log|warn|error|info|debug|trace)\s*\([^;{}]*$)'

# ---------- 工具函数 ----------
function Get-SegmentStats {
    param([string]$Text)
    $runs = 0
    foreach ($seg in [regex]::Matches($Text, $script:SegmentPattern)) {
        if ($seg.Value -cmatch $script:IdeoTest) { $runs++ }
    }
    $chars = [regex]::Matches($Text, $script:IdeoTest).Count
    [pscustomobject]@{ Runs = $runs; Chars = $chars }
}

function Get-FileInventory {
    param([string]$Path, [bool]$IsTs)

    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ($null -eq $raw) { $raw = '' }
    $pattern = if ($IsTs) { $script:TsPattern } else { $script:CsPattern }
    $isTsx = $Path -like '*.tsx'

    $stats = [ordered]@{
        CommentRuns = 0; CommentChars = 0
        LogRuns = 0;     LogChars = 0
        UiRuns = 0;      UiChars = 0
        ResidualRuns = 0; ResidualChars = 0   # 解析盲区漏出、已按语境并入日志 / UI 的量（透明度口径）
    }

    # 事件 = 注释 / 字符串匹配 + 「残留段」（注释与字符串之外仍含汉字的文本段，索引与原文对齐）。
    # .tsx 的残留段主要即 JSX 文本节点（用户可见）；其余范围的残留段为解析盲区漏出的字面量内容。
    # 匹配区间以 NUL（非空白、非 CJK）填充：保持索引对齐，且不让残留段经空白桥接跨过被抹区间。
    $fillUnit = [string][char]0
    $base = [System.Text.StringBuilder]::new($raw)
    $matches = [regex]::Matches($raw, $pattern)
    $events = [System.Collections.Generic.List[object]]::new()
    foreach ($m in $matches) {
        [void]$base.Remove($m.Index, $m.Length)
        [void]$base.Insert($m.Index, ($fillUnit * $m.Length))
        $kind = if ($m.Groups['cm'].Success) { 'Comment' } elseif ($m.Groups['str'].Success) { 'Str' } else { 'Chr' }
        $events.Add([pscustomobject]@{ Index = $m.Index; Length = $m.Length; Kind = $kind; Value = $m.Value })
    }
    foreach ($seg in [regex]::Matches($base.ToString(), $script:SegmentPattern)) {
        if ($seg.Value -cmatch $script:IdeoTest) {
            $events.Add([pscustomobject]@{ Index = $seg.Index; Length = $seg.Length; Kind = 'Residual'; Value = $seg.Value })
        }
    }
    $sorted = @($events | Sort-Object Index)

    $prevEnd = 0
    foreach ($ev in $sorted) {
        $gap = $raw.Substring($prevEnd, $ev.Index - $prevEnd)
        $prevEnd = $ev.Index + $ev.Length

        if ($ev.Kind -eq 'Comment') {
            $s = Get-SegmentStats $ev.Value
            $stats.CommentRuns += $s.Runs; $stats.CommentChars += $s.Chars
        }
        elseif ($ev.Kind -eq 'Str') {
            $content = [regex]::Replace($ev.Value, $script:PlaceholderPattern, '')
            $s = Get-SegmentStats $content
            if ($gap -cmatch $script:LogGapPattern) {
                $stats.LogRuns += $s.Runs; $stats.LogChars += $s.Chars
            }
            else {
                $stats.UiRuns += $s.Runs; $stats.UiChars += $s.Chars
            }
        }
        elseif ($ev.Kind -eq 'Residual') {
            $s = Get-SegmentStats ([regex]::Replace($ev.Value, $script:PlaceholderPattern, ''))
            $stats.ResidualRuns += $s.Runs; $stats.ResidualChars += $s.Chars
            # 残留按同一日志语境启发式归并：.tsx 残留 = JSX 文本节点（一律 UI）；其余按语境分。
            if (-not $isTsx -and $gap -cmatch $script:LogGapPattern) {
                $stats.LogRuns += $s.Runs; $stats.LogChars += $s.Chars
            }
            else {
                $stats.UiRuns += $s.Runs; $stats.UiChars += $s.Chars
            }
        }
        # 'Chr'（字符字面量）不计文案。
    }

    [pscustomobject]@{
        Path = $Path
        CommentRuns = $stats.CommentRuns; CommentChars = $stats.CommentChars
        LogRuns = $stats.LogRuns;         LogChars = $stats.LogChars
        UiRuns = $stats.UiRuns;           UiChars = $stats.UiChars
        ResidualRuns = $stats.ResidualRuns; ResidualChars = $stats.ResidualChars
    }
}

# ---------- 文件发现 ----------
$webRoot = Join-Path $Root 'web/src'
$srcRoot = Join-Path $Root 'src'
foreach ($dir in @($webRoot, $srcRoot)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        throw "目录不存在：$dir（请用 -Root 指定仓库根）"
    }
}

$webFiles = @(Get-ChildItem -LiteralPath $webRoot -Recurse -File |
    Where-Object { $_.Extension -in '.ts', '.tsx' -and $_.Name -notmatch '\.(test|spec)\.tsx?$' -and $_.FullName -notmatch '[\\/]__tests?__[\\/]|[\\/]__mocks__[\\/]' } |
    Sort-Object FullName)
$csFiles = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -File -Filter *.cs |
    Where-Object {
        $rel = $_.FullName.Substring($srcRoot.Length).TrimStart('\', '/')
        $segments = $rel -split '[\\/]'
        # 目录段精确等于 bin/obj/test(s)；文件名 *.Test.cs / *.Tests.cs / *.spec.cs 视为测试。
        # 不用「段内包含 test」——会误伤 LatestPointer / TemplateStore（la-test / la-teSt-ore）等生产文件。
        -not ($segments | Where-Object { $_ -cmatch '^(?i:bin|obj|tests?)$' }) -and
        $_.Name -notmatch '(?i)\.(test|spec)\.cs$|(?i)tests?\.cs$'
    } |
    Sort-Object FullName)

$inventories = @()
foreach ($f in $webFiles) { $inventories += Get-FileInventory -Path $f.FullName -IsTs $true }
foreach ($f in $csFiles) { $inventories += Get-FileInventory -Path $f.FullName -IsTs $false }

# 汇总（路径统一为仓库相对路径 + 正斜杠，便于阅读与排序稳定）
$rootFull = (Get-Item -LiteralPath $Root).FullName
foreach ($inv in $inventories) {
    $rel = $inv.Path.Substring($rootFull.Length).TrimStart('\', '/') -replace '\\', '/'
    $inv | Add-Member -NotePropertyName RelPath -NotePropertyValue $rel -PassThru | Out-Null
}
$webInv = @($inventories | Where-Object { $_.RelPath -like 'web/src/*' })
$csInv = @($inventories | Where-Object { $_.RelPath -like 'src/*' })

function Get-ScopeRow {
    param([string]$Name, $array)
    $sum = [ordered]@{
        Scope = $Name; Files = @($array).Count
        CommentRuns = ($array | Measure-Object CommentRuns -Sum).Sum
        CommentChars = ($array | Measure-Object CommentChars -Sum).Sum
        LogRuns = ($array | Measure-Object LogRuns -Sum).Sum
        LogChars = ($array | Measure-Object LogChars -Sum).Sum
        UiRuns = ($array | Measure-Object UiRuns -Sum).Sum
        UiChars = ($array | Measure-Object UiChars -Sum).Sum
        ResidualRuns = ($array | Measure-Object ResidualRuns -Sum).Sum
        ResidualChars = ($array | Measure-Object ResidualChars -Sum).Sum
    }
    foreach ($k in @('CommentRuns','CommentChars','LogRuns','LogChars','UiRuns','UiChars','ResidualRuns','ResidualChars')) {
        if ($null -eq $sum[$k]) { $sum[$k] = 0 }
    }
    [pscustomobject]$sum
}

$scopeWeb = Get-ScopeRow 'web/src（*.ts/tsx，排除测试）' $webInv
$scopeCs = Get-ScopeRow 'src（*.cs，排除 bin/obj/test）' $csInv
$scopeAll = Get-ScopeRow '合计' ($webInv + $csInv)

# ---------- 输出 ----------
$generated = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
$uiFiles = @($inventories | Where-Object { $_.UiRuns -gt 0 } | Sort-Object @{ Expression = 'UiRuns'; Descending = $true }, @{ Expression = 'UiChars'; Descending = $true }, RelPath)
$topN = @($uiFiles | Select-Object -First $Top)

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# 全仓文案盘点底账（inventory-ui-text.ps1）')
$lines.Add('')
$lines.Add('- 生成时间（UTC）：' + $generated)
$lines.Add('- 仓库根：' + $Root)
$lines.Add('- 用途：决策 #164 落地路径⑧——多语言迁移（迭代 108~112）范围估算底账；Issue 即事实源，本表不落长期文档。')
$lines.Add('- 复跑：`pwsh -NoProfile -File scripts/inventory-ui-text.ps1`（只读、无人工干预；统计口径见文末）。')
$lines.Add('')
$lines.Add('## 1. 三类总量汇总')
$lines.Add('')
$lines.Add('| 范围 | 文件数 | 注释：条 / 汉字 | 日志与诊断：条 / 汉字 | 用户可见 UI 文案：条 / 汉字 | 残留补入：条 / 汉字 |')
$lines.Add('|---|---:|---:|---:|---:|---:|')
foreach ($row in @($scopeWeb, $scopeCs, $scopeAll)) {
    $lines.Add(('| {0} | {1} | {2} / {3} | {4} / {5} | {6} / {7} | {8} / {9} |' -f `
        $row.Scope, $row.Files, `
        $row.CommentRuns, $row.CommentChars, `
        $row.LogRuns, $row.LogChars, `
        $row.UiRuns, $row.UiChars, `
        $row.ResidualRuns, $row.ResidualChars))
}
$lines.Add('')
$lines.Add(('其中含 UI 文案的文件 {0} / {1} 个（web {2} / {3}，C# {4} / {5}）。「残留补入」已含在日志 / UI 两列内。' -f `
    @($uiFiles).Count, @($inventories).Count, `
    @($webInv | Where-Object { $_.UiRuns -gt 0 }).Count, @($webInv).Count, `
    @($csInv | Where-Object { $_.UiRuns -gt 0 }).Count, @($csInv).Count))
$lines.Add('')
$lines.Add('## 2. Top {0} 重灾区（按 UI 文案条数降序；条 / 汉字为 UI 类）' -f @($topN).Count)
$lines.Add('')
$lines.Add('| 排名 | 文件 | UI 条数 | UI 汉字 | 注释条 | 日志条 |')
$lines.Add('|---:|---|---:|---:|---:|---:|')
$rank = 0
foreach ($f in $topN) {
    $rank++
    $lines.Add(('| {0} | `{1}` | {2} | {3} | {4} | {5} |' -f $rank, $f.RelPath, $f.UiRuns, $f.UiChars, $f.CommentRuns, $f.LogRuns))
}
$lines.Add('')
$lines.Add('## 3. 每文件三类明细（仅列 UI 条数 > 0 的文件，按 UI 条数降序）')
$lines.Add('')
$lines.Add('| 文件 | 注释条 | 日志条 | UI 条数 | UI 汉字 | 残留补入条 |')
$lines.Add('|---|---:|---:|---:|---:|---:|')
foreach ($f in $uiFiles) {
    $lines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} |' -f $f.RelPath, $f.CommentRuns, $f.LogRuns, $f.UiRuns, $f.UiChars, $f.ResidualRuns))
}
$lines.Add('')
$lines.Add('## 4. 口径与局限')
$lines.Add('')
$lines.Add('- 分类启发式：注释 = `//`（含 `///` XML doc）与 `/* */`；日志与诊断 = 字面量处于 `[LoggerMessage(… Message =`、ILogger 扩展（logger. / _logger. 等）、Serilog `Log.*`、`HostLog.*`、`Console.Write*`、`Debug/Trace.WriteLine`、`console.*` 调用语境；其余字符串字面量（含 API 错误消息、Windows / Android 原生弹窗、抛错消息等）与 .tsx 的 JSX 文本节点 = 用户可见 UI 文案。字符字面量（`''x''`）不计文案。')
$lines.Add('- 「条」= 剥除 `{…}` / `${…}` 占位后的极大连续 CJK 文案段（段内允许空白，须至少含 1 个汉字）；带参文案按段计可能略高于翻译字符串数；内嵌 HTML/JS 的长字面量（如 PDA 状态页）按其中文案段计。')
$lines.Add('- 解析为单遍组合正则（左优先），非编译器级。「残留补入」列 = 解析盲区漏出的 CJK 文本（典型：C# 插值孔内含引号的字面量 `$"…{x ?? "中文"}…"` 外层串提前闭合、孔内文案漏出；.tsx 的 JSX 文本节点也走该通道），已按同一日志语境启发式并入日志 / UI 两类，未丢失；该列在 .tsx 为 JSX 文本量，在其余范围应较小，显著偏大即解析盲区扩大信号。')
$lines.Add('- 扫描范围：`web/src`（*.ts/tsx，排除 `*.test.*` / `*.spec.*` / `__tests__` / `__mocks__`）与 `src`（*.cs，排除 bin/obj 与测试文件）；`test/`、`tools/`、安装链 BA / WiX / 引导问卷源码在 `src` 内会被扫到但不参与迁移（决策 #164：安装链维持中文单语）；Android XML 资源不在扫描范围。')
$lines.Add('- 本表为一次性底账快照，仓库内容变化后请复跑脚本取新数；结果不落 docs/（Issue 即事实源，WORKFLOW §3）。')

if ($OutputFile) {
    $outDir = Split-Path -Parent $OutputFile
    if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
    [System.IO.File]::WriteAllLines($OutputFile, $lines, [System.Text.UTF8Encoding]::new($false))
}

$lines | ForEach-Object { Write-Output $_ }
