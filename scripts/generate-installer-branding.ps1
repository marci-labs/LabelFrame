# 生成安装包品牌位图（WixUI 规范）：
#   assets/installer-dialog.bmp  493x312（欢迎 / 完成页整幅背景：左侧品牌竖幅 + 右侧留白给 MSI 文字控件）
#   assets/installer-banner.bmp  493x58 （中间各页顶部横幅：留白给标题文字 + 底部主蓝饰条 + 右侧 L 标）
# 与 generate-icon.ps1 同一品牌体系（主蓝 22,104,220 + 白色 L）。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$blue = [System.Drawing.Color]::FromArgb(255, 22, 104, 220)
$blueDark = [System.Drawing.Color]::FromArgb(255, 13, 72, 158)
$white = [System.Drawing.Color]::White
$paper = [System.Drawing.Color]::FromArgb(255, 250, 251, 253)

function New-LMark([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size, [System.Drawing.Color]$markColor) {
    # 与应用图标同构的 L：竖条 + 横条
    $brush = New-Object System.Drawing.SolidBrush($markColor)
    $w = $size * 0.22
    $vx = $x + $size * 0.28; $vy = $y + $size * 0.24
    $hx = $vx;               $hy = $y + $size * 0.50
    $g.FillRectangle($brush, $vx, $vy, $w, ($size * 0.72 - $vy))
    $g.FillRectangle($brush, $hx, $hy, ($x + $size * 0.74 - $hx), $w)
    $g.FillRectangle($brush, $vx, ($hy - $w * 0.5), $w, ($w * 0.5))
    $brush.Dispose()
}

# ---------- 1) 欢迎 / 完成页背景 493x312 ----------
$dlg = New-Object System.Drawing.Bitmap(493, 312, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb))
$g = [System.Drawing.Graphics]::FromImage($dlg)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
$g.Clear($paper)

# 左侧品牌竖幅（宽 150）：主蓝纵向渐变，上 L 标下产品名
$stripW = 150
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(0, 312)), $blue, $blueDark)
$g.FillRectangle($grad, 0, 0, $stripW, 312)

New-LMark $g 45 40 60 $white

$fontWord = New-Object System.Drawing.Font('Segoe UI', 17, ([System.Drawing.FontStyle]::Bold))
$fontTag = New-Object System.Drawing.Font('Microsoft YaHei UI', 10.5)
$brushW = New-Object System.Drawing.SolidBrush($white)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('LabelFrame', $fontWord, $brushW, (New-Object System.Drawing.RectangleF(0, 118, $stripW, 30)), $fmt)
$g.DrawString('仓库标签打印', $fontTag, $brushW, (New-Object System.Drawing.RectangleF(0, 152, $stripW, 22)), $fmt)
$g.Dispose()
$dlg.Save((Join-Path $outDir 'installer-dialog.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$dlg.Dispose()

# ---------- 2) 内页顶部横幅 493x58 ----------
$ban = New-Object System.Drawing.Bitmap(493, 58, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb))
$g = [System.Drawing.Graphics]::FromImage($ban)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
$g.Clear($paper)
# 底部主蓝饰条（标题文字由 MSI 在横幅左侧绘制，左区保持素净）
$brushB = New-Object System.Drawing.SolidBrush($blue)
$g.FillRectangle($brushB, 0, 54, 493, 4)
# 右侧 L 标（蓝色，克制大小）
New-LMark $g 452 12 34 $blue
$g.Dispose()
$ban.Save((Join-Path $outDir 'installer-banner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$ban.Dispose()

Write-Host "生成：$outDir\installer-dialog.bmp（493x312）、installer-banner.bmp（493x58）"
