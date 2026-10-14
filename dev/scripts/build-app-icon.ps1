param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$assetDirectory = Join-Path $projectRoot 'src/KotorinToys/assets'
$sourcePath = Join-Path $assetDirectory 'KotorinToys.png'
$outputPath = Join-Path $assetDirectory 'KotorinToys.ico'
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$sourceImage = [System.Drawing.Bitmap]::new($sourcePath)
$payloads = [Collections.Generic.List[byte[]]]::new()
try {
    if ($sourceImage.Width -ne $sourceImage.Height -or $sourceImage.GetPixel(0,0).A -ne 0) { throw '透過背景の正方形マスターが必要です。' }
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($sourceImage,[System.Drawing.Rectangle]::new(0,0,$size,$size),0,0,$sourceImage.Width,$sourceImage.Height,[System.Drawing.GraphicsUnit]::Pixel)
            # 全サイズを32bit DIBとANDマスクで保存し、アルファ透過と従来のマスクを両立する。
                $writer = [IO.BinaryWriter]::new($stream,[Text.Encoding]::UTF8,$true)
                try {
                    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                    $writer.Write([uint32]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
                    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
                    $writer.Write([uint32]($size * $size * 4 + $maskStride * $size))
                    for ($i = 0; $i -lt 4; $i++) { $writer.Write([uint32]0) }
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        for ($x = 0; $x -lt $size; $x++) {
                            $pixel = $bitmap.GetPixel($x,$y)
                            $writer.Write([byte]$pixel.B); $writer.Write([byte]$pixel.G); $writer.Write([byte]$pixel.R); $writer.Write([byte]$pixel.A)
                        }
                    }
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        $mask = [byte[]]::new($maskStride)
                        for ($x = 0; $x -lt $size; $x++) {
                            if ($bitmap.GetPixel($x,$y).A -eq 0) {
                                $byte = [int][Math]::Floor($x / 8)
                                $mask[$byte] = [byte]($mask[$byte] -bor (1 -shl (7 - ($x % 8))))
                            }
                        }
                        $writer.Write($mask)
                    }
                    $writer.Flush()
                } finally { $writer.Dispose() }
            $payloads.Add($stream.ToArray())
        } finally { $graphics.Dispose(); $stream.Dispose(); $bitmap.Dispose() }
    }
    $output = [IO.File]::Open($outputPath,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
        $offset = 6 + 16 * $sizes.Length
        for ($i = 0; $i -lt $sizes.Length; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$payloads[$i].Length); $writer.Write([uint32]$offset)
            $offset += $payloads[$i].Length
        }
        foreach ($payload in $payloads) { $writer.Write($payload) }
    } finally { $writer.Dispose(); $output.Dispose() }
    "ICOを再生成しました：$outputPath / $($sizes -join ', ') px"
} finally { $sourceImage.Dispose() }
