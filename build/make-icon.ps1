<#
.SYNOPSIS
    Generates ClipGuard.ico from the pixel-shield mark.

.DESCRIPTION
    The shield is defined once as an 8x9 grid — the same grid used by
    PixelShield.xaml and TrayService.BuildIcon — so the app icon, the tray icon
    and the in-app mark can never drift apart.

    Re-run after changing the grid. Output: src/ClipGuard/Assets/ClipGuard.ico
#>
[CmdletBinding()]
param(
    [string]$OutPath = "$PSScriptRoot/../src/ClipGuard/Assets/ClipGuard.ico"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The shield, as filled rectangles on an 8x9 grid: x, y, width, height.
$body = @(
    @(1, 0, 6, 1),
    @(0, 1, 8, 4),
    @(1, 5, 6, 2),
    @(2, 7, 4, 1),
    @(3, 8, 2, 1)
)
$notch = @(3, 2, 2, 2)   # punched back out to transparent

$accent = [System.Drawing.Color]::FromArgb(255, 0xEC, 0x30, 0x13)
$sizes  = @(16, 24, 32, 48, 64, 128, 256)

function New-ShieldPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $g.Clear([System.Drawing.Color]::Transparent)

        # Largest whole-pixel cell that fits 9 rows, so edges stay crisp.
        $cell = [math]::Max(1, [math]::Floor($size / 9))
        $offsetX = [math]::Floor(($size - 8 * $cell) / 2)
        $offsetY = [math]::Floor(($size - 9 * $cell) / 2)

        $brush = New-Object System.Drawing.SolidBrush($accent)
        foreach ($r in $body) {
            $g.FillRectangle($brush, $offsetX + $r[0] * $cell, $offsetY + $r[1] * $cell,
                                     $r[2] * $cell, $r[3] * $cell)
        }
        $brush.Dispose()

        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $clear = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)
        $g.FillRectangle($clear, $offsetX + $notch[0] * $cell, $offsetY + $notch[1] * $cell,
                                 $notch[2] * $cell, $notch[3] * $cell)
        $clear.Dispose()
    }
    finally { $g.Dispose() }

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $stream.ToArray()
}

# A typed list, not a pipeline: PowerShell unrolls byte[] through the pipeline,
# which silently flattens the frames into one long byte stream.
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) { $frames.Add((New-ShieldPng $size)) }

# ICO container: 6-byte header, then a 16-byte directory entry per frame,
# then the PNG payloads. PNG-compressed frames are valid from Vista on.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)

$writer.Write([uint16]0)               # reserved
$writer.Write([uint16]1)               # type: icon
$writer.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))   # 0 means 256
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)             # palette size
    $writer.Write([byte]0)             # reserved
    $writer.Write([uint16]1)           # colour planes
    $writer.Write([uint16]32)          # bits per pixel
    $writer.Write([uint32]$frames[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()

$dir = Split-Path $OutPath -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
$writer.Dispose()

$resolved = (Resolve-Path $OutPath).Path
Write-Host "Wrote $resolved ($([math]::Round((Get-Item $resolved).Length / 1KB, 1)) KB, $($sizes.Count) sizes)" -ForegroundColor Green
