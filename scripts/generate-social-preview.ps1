# 生成 GitHub 社交预览图（1280x640）：品牌蓝渐变底 + L 标 + 产品名 + 标语
# 与 generate-icon.ps1 / generate-installer-branding.ps1 同一品牌体系
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
$png = Join-Path $outDir 'social-preview.png'

$bmp = New-Object System.Drawing.Bitmap(1280, 640, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

# 背景：左深右浅渐变（主蓝 → 深蓝）
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(1280, 640)),
    [System.Drawing.Color]::FromArgb(255, 22, 104, 220),
    [System.Drawing.Color]::FromArgb(255, 10, 60, 140))
$g.FillRectangle($bg, 0, 0, 1280, 640)

function New-LMark([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size) {
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $w = $size * 0.22
    $vx = $x + $size * 0.28; $vy = $y + $size * 0.20
    $hx = $vx;               $hy = $y + $size * 0.48
    $g.FillRectangle($brush, $vx, $vy, $w, ($size * 0.68 - $vy))
    $g.FillRectangle($brush, $hx, $hy, ($x + $size * 0.74 - $hx), $w)
    $g.FillRectangle($brush, $vx, ($hy - $w * 0.5), $w, ($w * 0.5))
    $brush.Dispose()
}

# L 标（左上 80px 区域，白色）
New-LMark $g 80 80 120

# 产品名
$fontTitle = New-Object System.Drawing.Font('Segoe UI', 56, ([System.Drawing.FontStyle]::Bold))
$fontTag = New-Object System.Drawing.Font('Microsoft YaHei UI', 22)
$fontTagEn = New-Object System.Drawing.Font('Segoe UI', 14)
$brushW = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$brushSub = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 200, 220, 250))

$g.DrawString('LabelFrame', $fontTitle, $brushW, (New-Object System.Drawing.PointF(80, 230)))
$g.DrawString('仓库标签打印框架', $fontTag, $brushW, (New-Object System.Drawing.PointF(84, 310)))
$g.DrawString('Template Designer · Print Service · Device Host', $fontTagEn, $brushSub, (New-Object System.Drawing.PointF(84, 360)))

# 底部一行特点
$fontFeat = New-Object System.Drawing.Font('Microsoft YaHei UI', 16)
$g.DrawString('Web 设计器  ·  Excel 批量导入  ·  传输插件  ·  Win / Linux', $fontFeat, $brushSub, (New-Object System.Drawing.PointF(84, 540)))

# 右下角 L 标（大半透明装饰）
New-LMark $g 900 380 280

$g.Dispose()
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "生成：$png（1280x640）"
