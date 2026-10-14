param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$path = Join-Path $projectRoot 'src/KotorinToys/assets/KotorinToys.ico'
$data = [IO.File]::ReadAllBytes($path)
$sizes = @(16,20,24,32,40,48,64,96,128,256)
if ([BitConverter]::ToUInt16($data,0) -ne 0 -or [BitConverter]::ToUInt16($data,2) -ne 1 -or [BitConverter]::ToUInt16($data,4) -ne $sizes.Length) { throw 'ICOヘッダーが不正です。' }
for ($i = 0; $i -lt $sizes.Length; $i++) {
    $size = $sizes[$i]; $entry = 6 + 16 * $i
    $width = if ($data[$entry] -eq 0) { 256 } else { [int]$data[$entry] }
    $length = [BitConverter]::ToUInt32($data,$entry + 8)
    $offset = [BitConverter]::ToUInt32($data,$entry + 12)
    if ($width -ne $size -or $offset + $length -gt $data.Length) { throw 'ICOのフレーム定義が不正です。' }
    # 各エントリーを単独で読込み、0=256のサイズ表現を含む実フレームを検証する。
    # 複合ICOに対するSystem.Drawingの選択結果だけで256px欠落と判定しない。
    $single = [byte[]]::new(22 + $length)
    [Array]::Copy($data,0,$single,0,6); $single[4] = 1; $single[5] = 0
    [Array]::Copy($data,$entry,$single,6,16)
    [Array]::Copy([BitConverter]::GetBytes([uint32]22),0,$single,18,4)
    [Array]::Copy($data,$offset,$single,22,$length)
    $stream = [IO.MemoryStream]::new($single)
    $icon = [System.Drawing.Icon]::new($stream,$size,$size)
    $bitmap = $icon.ToBitmap()
    try {
        if ($icon.Width -ne $size -or $icon.Height -ne $size) { throw "寸法が不正です：$size" }
        foreach ($point in @(@(0,0),@(($size - 1),0),@(0,($size - 1)),@(($size - 1),($size - 1)))) {
            if ($bitmap.GetPixel($point[0],$point[1]).A -ne 0) { throw "四隅の透過が不正です：$size" }
        }
        $blue = 0; $red = 0; $green = 0
        for ($y = 0; $y -lt $size; $y++) {
            for ($x = 0; $x -lt $size; $x++) {
                $p = $bitmap.GetPixel($x,$y)
                if ($p.A -lt 128) { continue }
                if ($p.B -gt $p.R * 1.3 -and $p.G -gt $p.R) { $blue++ }
                if ($p.R -gt $p.G * 1.3 -and $p.R -gt $p.B * 1.3) { $red++ }
                if ($p.G -gt $p.R * 1.3 -and $p.G -gt $p.B * 1.3) { $green++ }
            }
        }
        if ($blue -eq 0 -or $red -eq 0 -or $green -eq 0) { throw "04の3色がありません：$size" }
        "PASS：$size pxの寸法・透過・青い鳥／赤緑の積み木"
    } finally { $bitmap.Dispose(); $icon.Dispose(); $stream.Dispose() }
}
if ($offset + $length -ne $data.Length) { throw 'ICO末尾に不明な追加データがあります。' }
