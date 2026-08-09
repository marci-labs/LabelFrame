# 生成 LabelFrame 图标（双色 L 型）：labelframe.ico（多尺寸 PNG ICO）+ labelframe.png（256）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-LIconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = [System.Drawing.Color]::FromArgb(255, 22, 104, 220)   # 主蓝
    $fg = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)  # 白色 L

    # 圆角背景
    $radius = [int]($size * 0.22)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush($bg)
    $g.FillPath($brush, $path)

    # L 型：竖条 + 横条（白色粗笔画）
    $w = $size * 0.22      # 笔画宽
    $vx = $size * 0.28     # 竖条左 x
    $vx2 = $vx + $w
    $vy = $size * 0.24     # 竖条上 y
    $vy2 = $size * 0.72
    $hx = $vx              # 横条左 x（与竖条对齐）
    $hx2 = $size * 0.74    # 横条右 x
    $hy = $size * 0.50
    $hy2 = $hy + $w

    $fgBrush = New-Object System.Drawing.SolidBrush($fg)
    $g.FillRectangle($fgBrush, $vx, $vy, ($vx2 - $vx), ($vy2 - $vy))       # 竖条
    $g.FillRectangle($fgBrush, $hx, $hy, ($hx2 - $hx), ($hy2 - $hy))       # 横条
    # 竖条与横条连接处补角（圆角笔画过渡）
    $g.FillRectangle($fgBrush, $vx, ($hy - $w * 0.5), $w, ($w * 0.5))

    $fgBrush.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# 生成各尺寸 PNG 字节
$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @{}
foreach ($s in $sizes) {
    $bmp = New-LIconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    $ms.Dispose()
    if ($s -eq 256) { $bmp.Save((Join-Path $outDir 'labelframe.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# 打包 ICO（PNG 压缩 ICO，Windows Vista+ 支持）
$icoPath = Join-Path $outDir 'labelframe.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                    # reserved
$bw.Write([uint16]1)                    # type: icon
$bw.Write([uint16]$sizes.Count)         # count
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # width
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # height
    $bw.Write([byte]0)                   # colors
    $bw.Write([byte]0)                   # reserved
    $bw.Write([uint16]1)                 # planes
    $bw.Write([uint16]32)                # bpp
    $bw.Write([uint32]$pngs[$s].Length)  # size
    $bw.Write([uint32]$offset)           # offset
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Dispose(); $fs.Dispose()
Write-Host "生成：$icoPath（$($sizes -join '/')）与 labelframe.png"
