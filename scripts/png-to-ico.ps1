# scripts/png-to-ico.ps1
#
# Converts a PNG to a multi-resolution Windows ICO file.
#
# The ICO format supports embedding multiple bitmap entries at different
# sizes; Windows picks the closest match for the context (16x16 in the
# taskbar, 256x256 in Add/Remove Programs, etc.). We resize the source
# to each requested size, encode each as PNG, and assemble them into
# the ICO container manually.
#
# Why not ImageMagick: it's not installed on most dev machines, and we
# don't want a build prereq for one image conversion. .NET's built-in
# System.Drawing handles the heavy lifting.
#
# Usage:
#   .\scripts\png-to-ico.ps1 -Source gundi_logo.png -Destination assets/installer/gundi.ico

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Source,
    [Parameter(Mandatory)] [string]$Destination,
    [int[]]$Sizes = @(256, 128, 64, 48, 32, 16)
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$srcPath = (Resolve-Path $Source).Path
$src = [System.Drawing.Bitmap]::FromFile($srcPath)

# Resize the source to each requested size and encode each as PNG.
$pngBlobs = @()
foreach ($size in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += ,@{ Size = $size; Data = $ms.ToArray() }
    $bmp.Dispose()
    $ms.Dispose()
}
$src.Dispose()

# Assemble the ICO container.
#   6 bytes  ICONDIR header
#   16 bytes ICONDIRENTRY * N
#   then PNG blobs back-to-back
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)

# ICONDIR
$writer.Write([UInt16]0)                # Reserved (must be 0)
$writer.Write([UInt16]1)                # Type: 1 = icon
$writer.Write([UInt16]$pngBlobs.Count)  # Image count

# ICONDIRENTRY records
$dataOffset = 6 + 16 * $pngBlobs.Count
foreach ($blob in $pngBlobs) {
    $size = $blob.Size
    # In ICONDIRENTRY a width/height of 0 means 256.
    $wh = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$wh)             # Width
    $writer.Write([byte]$wh)             # Height
    $writer.Write([byte]0)               # Color palette count (0 for >= 8bpp)
    $writer.Write([byte]0)               # Reserved
    $writer.Write([UInt16]1)             # Color planes
    $writer.Write([UInt16]32)            # Bits per pixel
    $writer.Write([UInt32]$blob.Data.Length)  # Image data size
    $writer.Write([UInt32]$dataOffset)        # Offset from start of file
    $dataOffset += $blob.Data.Length
}

# Image data (PNG-encoded, modern Windows accepts this for any size)
foreach ($blob in $pngBlobs) {
    $writer.Write($blob.Data)
}
$writer.Flush()

# Resolve destination relative to current dir if it doesn't exist yet.
if (-not [System.IO.Path]::IsPathRooted($Destination)) {
    $Destination = Join-Path (Get-Location) $Destination
}
$destDir = Split-Path -Parent $Destination
if (-not (Test-Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
}
[System.IO.File]::WriteAllBytes($Destination, $out.ToArray())
$writer.Dispose()
$out.Dispose()

$bytes = (Get-Item $Destination).Length
Write-Host "Wrote $Destination ($bytes bytes, $($pngBlobs.Count) sizes: $($Sizes -join ', '))"
