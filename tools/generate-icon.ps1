# tools/generate-icon.ps1
# Run once from repo root: pwsh tools/generate-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-RhBitmap([int]$sz) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 角丸パス
    $r = [int]($sz * 0.18)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0,        0,        $r*2, $r*2, 180, 90)
    $p.AddArc($sz-$r*2, 0,        $r*2, $r*2, 270, 90)
    $p.AddArc($sz-$r*2, $sz-$r*2, $r*2, $r*2, 0,   90)
    $p.AddArc(0,        $sz-$r*2, $r*2, $r*2, 90,  90)
    $p.CloseAllFigures()

    # 135° グラデーション (#4caf50 → #2e7d32)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.Point]::new(0, 0),
        [System.Drawing.Point]::new($sz, $sz),
        [System.Drawing.Color]::FromArgb(0x4c, 0xaf, 0x50),
        [System.Drawing.Color]::FromArgb(0x2e, 0x7d, 0x32))
    $g.FillPath($brush, $p)

    # "rh" テキスト
    $fs   = [int]($sz * 0.42)
    $font = New-Object System.Drawing.Font("Arial", $fs,
        [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf   = New-Object System.Drawing.StringFormat
    $sf.Alignment     = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString("rh", $font, [System.Drawing.Brushes]::White,
        [System.Drawing.RectangleF]::new(0, 0, $sz, $sz), $sf)
    $g.Dispose()
    return $bmp
}

$sizes   = @(16, 32, 48, 256)
$bitmaps = $sizes | ForEach-Object { New-RhBitmap $_ }

$outPath = "src/ReloadedHelper.App/Assets/app.ico"
$null    = New-Item -ItemType Directory -Force (Split-Path $outPath)

$pngStreams = $bitmaps | ForEach-Object {
    $s = New-Object System.IO.MemoryStream
    $_.Save($s, [System.Drawing.Imaging.ImageFormat]::Png)
    $s
}

$ms     = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ms)

# ICONDIR ヘッダ
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Length)

# 各 PNG のオフセット計算
$hdrSz   = 6 + 16 * $sizes.Length
$offset  = $hdrSz
$offsets = @()
foreach ($s in $pngStreams) { $offsets += $offset; $offset += $s.Length }

# ICONDIRENTRY × 4
for ($i = 0; $i -lt $sizes.Length; $i++) {
    $sz = $sizes[$i]
    $w  = if ($sz -ge 256) { 0 } else { $sz }
    $h  = if ($sz -ge 256) { 0 } else { $sz }
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$pngStreams[$i].Length)
    $writer.Write([uint32]$offsets[$i])
}

foreach ($s in $pngStreams) { $writer.Write($s.ToArray()) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
Write-Host "Generated $outPath ($([int]($ms.Length / 1024)) KB)"
