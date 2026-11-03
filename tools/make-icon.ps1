param(
    [Parameter(Mandatory)][string]$OutIco,
    [Parameter(Mandatory)][string]$OutPng,
    [Parameter(Mandatory)][string]$Color1,
    [Parameter(Mandatory)][string]$Color2
)
Add-Type -AssemblyName System.Drawing

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    $c1 = [System.Drawing.ColorTranslator]::FromHtml($Color1)
    $c2 = [System.Drawing.ColorTranslator]::FromHtml($Color2)

    # Nền bo góc, gradient chéo
    $bg = RoundRect 8 8 240 240 58
    $rect = New-Object System.Drawing.RectangleF(8, 8, 240, 240)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $g.FillPath($br, $bg)

    # Ánh sáng nhẹ phía trên
    $g.SetClip($bg)
    $hl = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hl.AddEllipse(-40, -150, 336, 260)
    $hb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(38, 255, 255, 255))
    $g.FillPath($hb, $hl)
    $g.ResetClip()

    # Bóng đổ bong bóng chat
    $shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(50, 0, 0, 0))
    $g.TranslateTransform(0, 7)
    $bubble = RoundRect 46 56 164 118 36
    $g.FillPath($shadow, $bubble)
    $tail = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tail.AddPolygon(@(
        (New-Object System.Drawing.PointF(82, 164)),
        (New-Object System.Drawing.PointF(72, 206)),
        (New-Object System.Drawing.PointF(122, 168))))
    $g.FillPath($shadow, $tail)
    $g.TranslateTransform(0, -7)

    # Bong bóng chat trắng
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $bubble)
    $g.FillPath($white, $tail)

    # Máy bay giấy (gửi tin)
    $plane = New-Object System.Drawing.Drawing2D.GraphicsPath
    $plane.AddPolygon(@(
        (New-Object System.Drawing.PointF(184, 112)),
        (New-Object System.Drawing.PointF(84, 76)),
        (New-Object System.Drawing.PointF(114, 112)),
        (New-Object System.Drawing.PointF(84, 148))))
    $pbr = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF(84, 76, 100, 72)), $c1, $c2, 20.0)
    $g.FillPath($pbr, $plane)
    # Nếp gấp
    $fold = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fold.AddPolygon(@(
        (New-Object System.Drawing.PointF(114, 112)),
        (New-Object System.Drawing.PointF(84, 148)),
        (New-Object System.Drawing.PointF(130, 130))))
    $fb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 0, 0, 0))
    $g.FillPath($fb, $fold)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Render $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($sz -eq 256) { $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# Ghép ICO (PNG nén bên trong)
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes)"
