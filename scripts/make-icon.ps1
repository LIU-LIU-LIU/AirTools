#requires -Version 7
<#
  Generates src/AirTools/Assets/AirTools.ico (and a 512px PNG for the README) from vector shapes.
  Design: a soft light-blue cloud with a small highlight, on a transparent background - no square plate.
  Usage:  ./scripts/make-icon.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/AirTools/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-CloudBitmap([int]$size) {
    $s = [double]$size
    # Unit-square design (0..100) so the same drawing scales to every icon size.
    $cloud = [System.Windows.Media.Geometry]::Parse(
        'M 24,86 C 8,86 2,70 11,60 C 13,52 20,47 29,48 C 31,33 44,24 57,27 C 66,15 86,17 91,34 C 101,35 105,48 98,58 C 106,70 97,86 84,86 Z')
    $group = New-Object System.Windows.Media.DrawingGroup
    $scale = New-Object System.Windows.Media.ScaleTransform(($s / 100.0 * 0.86), ($s / 100.0 * 0.86))
    $move = New-Object System.Windows.Media.TranslateTransform(($s * 0.0356), ($s * 0.066))
    $tg = New-Object System.Windows.Media.TransformGroup; $tg.Children.Add($scale); $tg.Children.Add($move)
    $group.Transform = $tg

    $fill = New-Object System.Windows.Media.LinearGradientBrush
    $fill.StartPoint = [System.Windows.Point]::new(0.2, 0); $fill.EndPoint = [System.Windows.Point]::new(0.8, 1)
    $fill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0xC9, 0xEC, 0xFF), 0))
    $fill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x7F, 0xC8, 0xF8), 0.55))
    $fill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x4F, 0xA6, 0xEE), 1))
    $edge = New-Object System.Windows.Media.Pen ([System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x90, 0x2F, 0x86, 0xD8)), 2.2)
    $edge.LineJoin = 'Round'
    $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing($fill, $edge, $cloud)))

    # Soft highlight along the upper-left puffs (a thin, low-opacity arc rather than a hard shape).
    $shine = [System.Windows.Media.Geometry]::Parse('M 30,38 C 34,30 42,26 50,27 C 43,31 37,36 34,43 Z')
    $white = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x70, 255, 255, 255))
    $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing($white, $null, $shine)))

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen(); $dc.DrawDrawing($group); $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    return $bmp
}

function ConvertTo-PngBytes($bitmap) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $ms = New-Object System.IO.MemoryStream; $enc.Save($ms); return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { ,@($size, (ConvertTo-PngBytes (New-CloudBitmap $size))) }

# ICO container with PNG-compressed entries (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $size = $image[0]; $bytes = $image[1]
    $w.Write([byte]($(if ($size -ge 256) { 0 } else { $size }))); $w.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset); $offset += $bytes.Length
}
foreach ($image in $images) { $w.Write([byte[]]$image[1]) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'AirTools.ico'), $out.ToArray())
[IO.File]::WriteAllBytes((Join-Path $assets 'AirTools-512.png'), (ConvertTo-PngBytes (New-CloudBitmap 512)))
"Wrote AirTools.ico ($($out.Length) bytes, sizes: $($sizes -join ', ')) and AirTools-512.png"
