# Generates a multi-resolution icon.ico (classic 32-bit DIB frames, widest compatibility).
# Run:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# Replace icon.ico at any time with your own; build.bat uses whatever is there.

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'icon.ico'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$entries = @()

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = [Math]::Max(2, [int]($size * 0.22))
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 74, 111, 240),
        [System.Drawing.Color]::FromArgb(255, 34, 56, 141),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(46, 255, 255, 255), [Math]::Max(1, $size / 64))
    $g.DrawPath($edge, $path)

    $font = New-Object System.Drawing.Font('Georgia', [float]($size * 0.62), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF(0, (-$size * 0.02), $size, $size)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('M', $font, $white, $textRect, $format)

    $white.Dispose(); $format.Dispose(); $font.Dispose()
    $edge.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()

    $data = $bmp.LockBits(
        (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER (height is doubled: XOR image + AND mask)
    $bw.Write([int]40)
    $bw.Write([int]$size)
    $bw.Write([int]($size * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]0)
    $bw.Write([int]($size * $size * 4))
    $bw.Write([int]0); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0)

    # XOR pixels, bottom-up
    for ($y = $size - 1; $y -ge 0; $y--) {
        $bw.Write($pixels, $y * $stride, $size * 4)
    }

    # AND mask: 1 = transparent, bottom-up, rows padded to 4 bytes
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $mask = New-Object byte[] ($maskStride * $size)
    for ($y = 0; $y -lt $size; $y++) {
        $row = ($size - 1 - $y) * $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            $alpha = $pixels[($y * $stride) + ($x * 4) + 3]
            if ($alpha -eq 0) {
                $_mi = $row + [Math]::Floor($x / 8); $mask[$_mi] = [byte]($mask[$_mi] -bor [int](0x80 -shr ($x % 8)))
            }
        }
    }
    $bw.Write($mask)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Close(); $ms.Close()
    return ,$bytes
}

foreach ($size in $sizes) {
    $frame = New-Frame $size
    $entries += [pscustomobject]@{ Size = $size; Bytes = $frame }
}

$offset = 6 + (16 * $entries.Count)
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$entries.Count)

foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Flush(); $bw.Close(); $fs.Close()

$info = Get-Item $out
"Wrote {0} ({1:N0} bytes, {2} sizes)" -f $info.Name, $info.Length, $entries.Count

