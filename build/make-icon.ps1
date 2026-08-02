<#
.SYNOPSIS
    Generates ClipGuard.ico from the canonical mark geometry.

.DESCRIPTION
    Reads the shield and clip paths straight out of MarkGeometry.cs rather than
    keeping its own copy, so the app icon, the tray icon and the in-app control
    are all the same drawing. Rendering goes through WPF for the same reason —
    redrawing the mark in GDI+ would be a second implementation to keep in sync.

    Re-run after changing the mark. CI fails if the committed .ico is stale.

.EXAMPLE
    ./build/make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutPath = "$PSScriptRoot/../src/ClipGuard/Assets/ClipGuard.ico",
    [string]$GeometrySource = "$PSScriptRoot/../src/ClipGuard/Controls/MarkGeometry.cs"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

# ── Pull the paths from the single definition ─────────────────────────────
$source = Get-Content $GeometrySource -Raw

function Get-Constant([string]$name) {
    $pattern = "public const string $name\s*=\s*`"([^`"]*)`""
    $match = [regex]::Match($source, $pattern)
    if (-not $match.Success) { throw "Could not find $name in $GeometrySource" }
    return $match.Groups[1].Value
}

$shieldPath = Get-Constant 'ShieldPath'
$clipPath   = Get-Constant 'ClipPath'

$strokeMatch = [regex]::Match($source, 'public const double ClipStrokeWidth\s*=\s*([\d.]+)')
if (-not $strokeMatch.Success) { throw 'Could not find ClipStrokeWidth' }
$strokeWidth = [double]$strokeMatch.Groups[1].Value

$extentMatch = [regex]::Match($source, 'public const double Extent\s*=\s*([\d.]+)')
$extent = [double]$extentMatch.Groups[1].Value

Write-Host "Mark geometry read from $(Split-Path $GeometrySource -Leaf)" -ForegroundColor Cyan

# ── Subtract the clip from the shield, once ───────────────────────────────
$shield = [System.Windows.Media.Geometry]::Parse($shieldPath)
$clip   = [System.Windows.Media.Geometry]::Parse($clipPath)

$pen = New-Object System.Windows.Media.Pen(
    [System.Windows.Media.Brushes]::Black, $strokeWidth)
$pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
$pen.EndLineCap   = [System.Windows.Media.PenLineCap]::Round
$pen.LineJoin     = [System.Windows.Media.PenLineJoin]::Round

# Widen the stroked clip into a fillable outline so it can be cut out. The hole
# stays transparent, which is what lets one icon work on light and dark shells.
$clipOutline = $clip.GetWidenedPathGeometry($pen)
$mark = [System.Windows.Media.Geometry]::Combine(
    $shield, $clipOutline, [System.Windows.Media.GeometryCombineMode]::Exclude, $null)

$accent = [System.Windows.Media.Color]::FromRgb(0xEC, 0x30, 0x13)
$brush = New-Object System.Windows.Media.SolidColorBrush($accent)
$brush.Freeze()

$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-MarkPng([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    try {
        $scale = $size / $extent
        $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
        $dc.DrawGeometry($brush, $null, $mark)
        $dc.Pop()
    }
    finally { $dc.Close() }

    $target = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($target))

    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return $stream.ToArray()
}

# A typed list, not a pipeline: PowerShell unrolls byte[] through the pipeline,
# which silently flattens the frames into one long byte stream.
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) { $frames.Add((New-MarkPng $size)) }

# ── Assemble the ICO ──────────────────────────────────────────────────────
# 6-byte header, a 16-byte directory entry per frame, then the PNG payloads.
# PNG-compressed frames are valid from Vista on.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)

$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
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
