$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\OtoPlayer\Assets'
[IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$images = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $graphics.Clear([Drawing.Color]::Transparent)
    $background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 28, 38, 53))
    $foreground = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 139, 230, 189))
    $graphics.FillEllipse($background, 4, 4, 248, 248)
    $graphics.FillRectangle($foreground, 109, 73, 17, 111)
    $graphics.FillRectangle($foreground, 177, 56, 17, 111)
    $points = [Drawing.Point[]]@([Drawing.Point]::new(109, 73), [Drawing.Point]::new(194, 52), [Drawing.Point]::new(194, 79), [Drawing.Point]::new(109, 100))
    $graphics.FillPolygon($foreground, $points)
    $graphics.FillEllipse($foreground, 66, 163, 60, 42)
    $graphics.FillEllipse($foreground, 134, 146, 60, 42)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{ Size = $size; Data = $stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory 'app.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $foreground.Dispose(); $background.Dispose()
}
$file = [IO.File]::Create((Join-Path $assetDirectory 'app.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Data.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Data.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Data) }
} finally { $writer.Dispose(); $file.Dispose() }
