# Génère src\PCPilot\Assets\PCPilot.ico (plusieurs tailles, entrées PNG).
param([string]$Out = "$PSScriptRoot\..\src\PCPilot\Assets\PCPilot.ico")
Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
    $r = [Math]::Max(3, [int]($s * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r; $w = $s - 1
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $s, $s), ([System.Drawing.Color]::FromArgb(255, 0, 95, 184)), ([System.Drawing.Color]::FromArgb(255, 0, 178, 170))
    $g.FillPath($brush, $path)
    $fontName = if ((New-Object System.Drawing.Text.InstalledFontCollection).Families.Name -contains 'Segoe Fluent Icons') { 'Segoe Fluent Icons' } else { 'Segoe MDL2 Assets' }
    $font = New-Object System.Drawing.Font $fontName, ([single]($s * 0.52)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF 0, ([single]($s * 0.03)), $s, $s
    $g.DrawString([string][char]0xE7F8, $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 128) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Entrée DIB 32 bits (BITMAPINFOHEADER + pixels BGRA de bas en haut + masque AND) : lisible partout.
        $bw2 = New-Object System.IO.BinaryWriter $ms
        $bw2.Write([uint32]40); $bw2.Write([int32]$s); $bw2.Write([int32]($s * 2)); $bw2.Write([uint16]1); $bw2.Write([uint16]32)
        $bw2.Write([uint32]0); $bw2.Write([uint32]($s * $s * 4)); $bw2.Write([int32]0); $bw2.Write([int32]0); $bw2.Write([uint32]0); $bw2.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $bw2.Write([byte]$c.B); $bw2.Write([byte]$c.G); $bw2.Write([byte]$c.R); $bw2.Write([byte]$c.A) } }
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        for ($y = 0; $y -lt $s; $y++) { $bw2.Write((New-Object byte[] $maskRow)) }
        $bw2.Flush()
    }
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"Icône écrite : $Out ($((Get-Item $Out).Length) octets)"
