# 生成 AndroidHost 启动器图标（与 generate-icon.ps1 / generate-installer-branding.ps1 同一品牌体系：
#   主蓝 22,104,220 + 白色 L）。产出各密度 legacy 位图（API 23-25 兜底；API 26+ 由 mipmap-anydpi-v26
#   自适应图标（矢量）覆盖，不依赖本脚本的位图）：
#   Resources/mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/ic_launcher.png        方形圆角（48/72/96/144/192）
#   Resources/mipmap-{...}/ic_launcher_round.png                              圆形
# 通知小图标（ic_stat_labelframe）为白色单色矢量，不在此生成。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$resDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\LabelFrame.AndroidHost\Resources'

$blue = [System.Drawing.Color]::FromArgb(255, 22, 104, 220)
$white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)

function New-LMarkPath([float]$size) {
    # 与应用图标同构的 L（generate-icon.ps1 同参数）：竖条 0.28→0.50、上 0.24、下 0.72；
    # 横条上沿 0.39（hy-w/2）、下沿 0.72、右至 0.74——六边形一笔画出整只 L
    $w = $size * 0.22
    $vx = $size * 0.28
    $vy = $size * 0.24
    $vy2 = $size * 0.72
    $hx2 = $size * 0.74
    $hy = $size * 0.50
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    [void]$path.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($vx, $vy)),
        (New-Object System.Drawing.PointF(($vx + $w), $vy)),
        (New-Object System.Drawing.PointF(($vx + $w), ($hy - $w * 0.5))),
        (New-Object System.Drawing.PointF($hx2, ($hy - $w * 0.5))),
        (New-Object System.Drawing.PointF($hx2, ($hy + $w))),
        (New-Object System.Drawing.PointF($vx, ($hy + $w)))
    ))
    return $path
}

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return @{ Bmp = $bmp; G = $g }
}

# 方形圆角底（圆角比例同 generate-icon.ps1）
function New-SquareLauncher([int]$size) {
    $c = New-Canvas $size
    $radius = [int]($size * 0.22)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $bg.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $bg.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $bg.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $bg.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $bg.CloseFigure()
    $bgBrush = New-Object System.Drawing.SolidBrush($blue)
    $c.G.FillPath($bgBrush, $bg)
    $fgBrush = New-Object System.Drawing.SolidBrush($white)
    $c.G.FillPath($fgBrush, (New-LMarkPath $size))
    $fgBrush.Dispose(); $bgBrush.Dispose(); $bg.Dispose(); $c.G.Dispose()
    return $c.Bmp
}

# 圆形底 + L 略收（免贴边裁切：L 缩到 84%、居中偏移 8%）
function New-RoundLauncher([int]$size) {
    $c = New-Canvas $size
    $bgBrush = New-Object System.Drawing.SolidBrush($blue)
    $c.G.FillEllipse($bgBrush, 0, 0, $size, $size)
    $scale = 0.84
    $c.G.TranslateTransform($size * (1 - $scale) / 2, $size * (1 - $scale) / 2)
    $c.G.ScaleTransform($scale, $scale)
    $fgBrush = New-Object System.Drawing.SolidBrush($white)
    $c.G.FillPath($fgBrush, (New-LMarkPath $size))
    $fgBrush.Dispose(); $bgBrush.Dispose(); $c.G.Dispose()
    return $c.Bmp
}

$densities = @{
    'mdpi'    = 48
    'hdpi'    = 72
    'xhdpi'   = 96
    'xxhdpi'  = 144
    'xxxhdpi' = 192
}
foreach ($entry in $densities.GetEnumerator()) {
    $dir = Join-Path $resDir ("mipmap-" + $entry.Key)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $square = New-SquareLauncher $entry.Value
    $square.Save((Join-Path $dir 'ic_launcher.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $square.Dispose()
    $round = New-RoundLauncher $entry.Value
    $round.Save((Join-Path $dir 'ic_launcher_round.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $round.Dispose()
    Write-Host ("生成：mipmap-{0}/ic_launcher.png + ic_launcher_round.png（{1}px）" -f $entry.Key, $entry.Value)
}
