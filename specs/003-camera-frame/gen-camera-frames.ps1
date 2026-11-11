param([string]$Out = (Join-Path $PSScriptRoot '..\..\Assets\CameraFrames'))
# Vẽ 4 khung camera PNG trong suốt 1920x1080 bằng System.Drawing (Windows PowerShell 5.1, không cần cài thêm).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$W = 1920; $H = 1080
$dot = [string][char]0x00B7

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}
function C($a, $r, $gr, $b) { [System.Drawing.Color]::FromArgb($a, $r, $gr, $b) }
function Pen($color, $width) { $p = New-Object System.Drawing.Pen($color, $width); $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'; $p }

# Góc chữ L ở 4 góc; glow = vẽ thêm nét rộng mờ phía dưới.
function Draw-Corners($g, $color, $margin, $len, $width, $glow) {
    $pts = @(
        @(@($margin, ($margin + $len)), @($margin, $margin), @(($margin + $len), $margin)),
        @(@(($W - $margin - $len), $margin), @(($W - $margin), $margin), @(($W - $margin), ($margin + $len))),
        @(@($margin, ($H - $margin - $len)), @($margin, ($H - $margin)), @(($margin + $len), ($H - $margin))),
        @(@(($W - $margin - $len), ($H - $margin)), @(($W - $margin), ($H - $margin)), @(($W - $margin), ($H - $margin - $len))))
    $layers = @()
    if ($glow) { $layers += ,@((C 40 $color.R $color.G $color.B), ($width * 4)); $layers += ,@((C 80 $color.R $color.G $color.B), ($width * 2.2)) }
    # bóng tối mảnh để đọc rõ trên nền sáng
    $layers += ,@((C 90 0 0 0), ($width + 3))
    $layers += ,@($color, $width)
    foreach ($layer in $layers) {
        $pen = Pen $layer[0] $layer[1]
        foreach ($c in $pts) {
            $arr = [System.Drawing.PointF[]]@($c | ForEach-Object { New-Object System.Drawing.PointF([float]$_[0], [float]$_[1]) })
            $g.DrawLines($pen, $arr)
        }
        $pen.Dispose()
    }
}
function Draw-Text($g, $text, $fontName, $size, $style, $color, $x, $y, $align) {
    $font = New-Object System.Drawing.Font($fontName, [float]$size, [System.Drawing.FontStyle]$style, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = $align; $fmt.LineAlignment = 'Center'
    $shadow = New-Object System.Drawing.SolidBrush((C 140 0 0 0))
    $g.DrawString($text, $font, $shadow, [float]($x + 2), [float]($y + 2), $fmt)
    $brush = New-Object System.Drawing.SolidBrush($color)
    $g.DrawString($text, $font, $brush, [float]$x, [float]$y, $fmt)
    $font.Dispose(); $brush.Dispose(); $shadow.Dispose(); $fmt.Dispose()
}
function Draw-Badge($g, $cx, $cy, $r, $ring, $fill, $text, $textColor) {
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($fill)), [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))
    $g.DrawEllipse((Pen $ring 4), [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))
    Draw-Text $g $text 'Segoe UI' ($r * 0.62) 'Bold' $textColor $cx $cy 'Center'
}
function Draw-Battery($g, $x, $y, $color, $bars) {
    $pen = Pen $color 4
    $g.DrawRectangle($pen, [float]$x, [float]$y, 70, 34)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($color)), [float]($x + 70), [float]($y + 10), 7, 14)
    for ($i = 0; $i -lt $bars; $i++) { $g.FillRectangle((New-Object System.Drawing.SolidBrush($color)), [float]($x + 7 + $i * 21), [float]($y + 7), 15, 20) }
    $pen.Dispose()
}
function Draw-Crosshair($g, $color, $size) {
    $pen = Pen $color 3
    $g.DrawLine($pen, [float]($W / 2 - $size), [float]($H / 2), [float]($W / 2 + $size), [float]($H / 2))
    $g.DrawLine($pen, [float]($W / 2), [float]($H / 2 - $size), [float]($W / 2), [float]($H / 2 + $size))
    $pen.Dispose()
}
function Save($bmp, $g, $name) {
    $g.Dispose(); $path = Join-Path $Out $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $path
}

$white = C 230 255 255 255
$gold = C 235 240 196 84

# 01 Tối giản: chỉ góc chữ L trắng + dấu ngắm nhỏ.
$bmp, $g = New-Canvas
Draw-Corners $g $white 70 110 5 $false
Draw-Crosshair $g (C 150 255 255 255) 18
Save $bmp $g 'Frame_01_Minimal_Rec.png'

# 02 Streamer neon: góc neon cyan phát sáng, huy hiệu TDN, nhãn LIVE hồng, dòng trạng thái.
$bmp, $g = New-Canvas
$cyan = C 245 0 229 255; $pink = C 245 255 46 136
Draw-Corners $g $cyan 64 130 6 $true
Draw-Badge $g 150 150 52 $cyan (C 150 10 12 30) 'TDN' $cyan
$pill = New-Object System.Drawing.Drawing2D.GraphicsPath
$px = $W - 300; $py = 118; $pw = 168; $ph = 62; $pr = 31
$pill.AddArc($px, $py, 2 * $pr, $ph, 90, 180); $pill.AddArc($px + $pw - 2 * $pr, $py, 2 * $pr, $ph, 270, 180); $pill.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush($pink)), $pill)
$g.FillEllipse((New-Object System.Drawing.SolidBrush((C 255 255 255 255))), [float]($px + 22), [float]($py + 20), 22, 22)
Draw-Text $g 'LIVE' 'Segoe UI' 36 'Bold' (C 255 255 255 255) ($px + 104) ($py + $ph / 2) 'Center'
Draw-Text $g "1080p  $dot  60 FPS  $dot  ON AIR" 'Segoe UI' 34 'Bold' $cyan ($W / 2) ($H - 120) 'Center'
Save $bmp $g 'Frame_02_Streamer_Neon.png'

# 03 Máy quay (gần ảnh mẫu): góc vàng, huy hiệu TDN, REC + chấm đỏ + pin, thông số chụp ở dưới.
$bmp, $g = New-Canvas
Draw-Corners $g $gold 70 120 6 $false
Draw-Badge $g 156 156 50 $gold (C 120 0 0 0) 'TDN' $gold
$g.FillEllipse((New-Object System.Drawing.SolidBrush((C 255 235 40 40))), [float]($W - 350), [float]136, 30, 30)
Draw-Text $g 'REC' 'Consolas' 44 'Bold' $white ($W - 270) 152 'Center'
Draw-Battery $g ($W - 205) 135 $white 3
Draw-Crosshair $g (C 170 240 196 84) 22
Draw-Text $g "1/500   $dot   F4.0   $dot   ISO 800" 'Consolas' 40 'Bold' $gold ($W / 2) ($H - 125) 'Center'
Draw-Text $g "4K  $dot  30P" 'Consolas' 30 'Bold' $white 130 ($H - 125) 'Near'
Save $bmp $g 'Frame_03_Camcorder_Viewfinder.png'

# 04 Sang trọng vàng: viền đôi vàng mảnh + hoa văn kim cương ở 4 góc, không có chi tiết máy quay.
$bmp, $g = New-Canvas
$outer = Pen $gold 4; $inner = Pen (C 200 240 196 84) 2; $shade = Pen (C 80 0 0 0) 7
$g.DrawRectangle($shade, 44, 44, $W - 88, $H - 88)
$g.DrawRectangle($outer, 44, 44, $W - 88, $H - 88)
$g.DrawRectangle($inner, 60, 60, $W - 120, $H - 120)
foreach ($c in @(@(44, 44), @(($W - 44), 44), @(44, ($H - 44)), @(($W - 44), ($H - 44)))) {
    $cx = $c[0]; $cy = $c[1]; $s = 22
    $d = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF($cx, ($cy - $s))), (New-Object System.Drawing.PointF(($cx + $s), $cy)),
        (New-Object System.Drawing.PointF($cx, ($cy + $s))), (New-Object System.Drawing.PointF(($cx - $s), $cy)))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush($gold)), $d)
    $g.DrawPolygon((Pen (C 160 60 40 0) 2), $d)
}
foreach ($mid in @(@(($W / 2), 44), @(($W / 2), ($H - 44)))) {
    $g.DrawLine($outer, [float]($mid[0] - 140), [float]$mid[1], [float]($mid[0] + 140), [float]$mid[1])
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($gold)), [float]($mid[0] - 9), [float]($mid[1] - 9), 18, 18)
}
Save $bmp $g 'Frame_04_Luxury_Gold.png'
