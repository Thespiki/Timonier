# Génère l'icône de Timonier (barre à roue blanche sur dégradé marine → turquoise) :
#   src\Timonier\Assets\Timonier.ico (16 à 256 px) et, avec -Png, une image PNG (logo de l'interface, README).
param(
    [string]$Out = "$PSScriptRoot\..\src\Timonier\Assets\Timonier.ico",
    [string]$Png = "",
    [int]$PngSize = 256
)
Add-Type -AssemblyName System.Drawing

function New-WheelBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'

    # Fond : carré arrondi, dégradé diagonal.
    $r = [Math]::Max(3, $s * 0.22); $d = 2 * $r; $w = $s - 1
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc(0, 0, $d, $d, 180, 90); $bg.AddArc($w - $d, 0, $d, $d, 270, 90)
    $bg.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $bg.AddArc(0, $w - $d, $d, $d, 90, 90); $bg.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 10, 52, 110)), ([System.Drawing.Color]::FromArgb(255, 0, 150, 150))
    $g.FillPath($grad, $bg)

    # Barre à roue : jante, moyeu, 8 rayons prolongés par des poignées. Traits épaissis aux petites tailles.
    $c = $s / 2.0
    $small = $s -le 24
    $tiny = $s -le 20   # 16/20 px : pas de rayons intérieurs (illisibles), seulement les poignées autour de la jante
    $rim = $s * $(if ($tiny) { 0.24 } else { 0.255 })
    $rimW = [Math]::Max(1.6, $s * $(if ($small) { 0.10 } else { 0.072 }))
    $spokeW = [Math]::Max(1.3, $s * $(if ($small) { 0.075 } else { 0.056 }))
    $reach = $s * $(if ($small) { 0.42 } else { 0.36 })
    $knob = $s * 0.054
    $hub = [Math]::Max(1.8, $s * $(if ($tiny) { 0.12 } elseif ($small) { 0.11 } else { 0.095 }))
    $white = [System.Drawing.Color]::White

    if ($s -ge 48) {
        # Ombre douce décalée pour détacher la roue du fond.
        $shadow = New-Object System.Drawing.Drawing2D.Matrix; $shadow.Translate(0, $s * 0.02)
        $g.Transform = $shadow
        $sc = [System.Drawing.Color]::FromArgb(60, 0, 20, 40)
        $sp = New-Object System.Drawing.Pen $sc, ([single]$rimW); $g.DrawEllipse($sp, [single]($c - $rim), [single]($c - $rim), [single](2 * $rim), [single](2 * $rim))
        $g.ResetTransform()
    }

    $pen = New-Object System.Drawing.Pen $white, ([single]$spokeW)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $brush = New-Object System.Drawing.SolidBrush $white
    for ($i = 0; $i -lt 8; $i++) {
        $a = [Math]::PI / 8 + $i * [Math]::PI / 4
        $x = $c + [Math]::Cos($a) * $reach; $y = $c + [Math]::Sin($a) * $reach
        $from = if ($tiny) { $rim } else { 0 }
        $g.DrawLine($pen, [single]($c + [Math]::Cos($a) * $from), [single]($c + [Math]::Sin($a) * $from), [single]$x, [single]$y)
        if (-not $small) {
            $x2 = $c + [Math]::Cos($a) * ($reach + $knob * 0.35); $y2 = $c + [Math]::Sin($a) * ($reach + $knob * 0.35)
            $g.FillEllipse($brush, [single]($x2 - $knob), [single]($y2 - $knob), [single](2 * $knob), [single](2 * $knob))
        }
    }
    $rimPen = New-Object System.Drawing.Pen $white, ([single]$rimW)
    $g.DrawEllipse($rimPen, [single]($c - $rim), [single]($c - $rim), [single](2 * $rim), [single](2 * $rim))
    $g.FillEllipse($brush, [single]($c - $hub), [single]($c - $hub), [single](2 * $hub), [single](2 * $hub))
    if ($s -ge 32) {
        $hole = $hub * 0.42
        $holeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 6, 95, 128))
        $g.FillEllipse($holeBrush, [single]($c - $hole), [single]($c - $hole), [single](2 * $hole), [single](2 * $hole))
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-WheelBitmap $s
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 128) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Entrée DIB 32 bits (BITMAPINFOHEADER + pixels BGRA de bas en haut + masque AND) : lisible partout.
        $bw2 = New-Object System.IO.BinaryWriter $ms
        $bw2.Write([uint32]40); $bw2.Write([int32]$s); $bw2.Write([int32]($s * 2)); $bw2.Write([uint16]1); $bw2.Write([uint16]32)
        $bw2.Write([uint32]0); $bw2.Write([uint32]($s * $s * 4)); $bw2.Write([int32]0); $bw2.Write([int32]0); $bw2.Write([uint32]0); $bw2.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $px = $bmp.GetPixel($x, $y); $bw2.Write([byte]$px.B); $bw2.Write([byte]$px.G); $bw2.Write([byte]$px.R); $bw2.Write([byte]$px.A) } }
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        for ($y = 0; $y -lt $s; $y++) { $bw2.Write((New-Object byte[] $maskRow)) }
        $bw2.Flush()
    }
    $bmp.Dispose()
    $entries += , $ms.ToArray()
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $entries[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($e in $entries) { $bw.Write($e) }
$bw.Close()
"Icône écrite : $Out ($((Get-Item $Out).Length) octets)"

if ($Png) {
    $bmp = New-WheelBitmap $PngSize
    New-Item -ItemType Directory -Force (Split-Path $Png) | Out-Null
    $bmp.Save($Png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "Image écrite : $Png"
}
