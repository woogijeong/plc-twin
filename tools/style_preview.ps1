# Look preview: renders the shot set for -style a, b, c (tools\shots.ps1) into tools\out\style_preview\{a,b,c}\ and
# composes tools\out\style_preview\contact_sheet.png (equipment region cropped; columns A/B/C, rows 03 stage, 05 pushed onto the belt, 07 lift pick).
# Build first (SceneBuilder.BuildPlayer). -NoRender only recomposes the sheet from existing PNGs.
param([switch]$NoRender)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'out\style_preview'
$styles = @(@('a', 'A  Modern industrial'), @('b', 'B  Realistic kit'), @('c', 'C  Blueprint'))
$rows = @(@('03', '03 chip on the drill stage'), @('05', '05 chip pushed onto the belt'), @('07', '07 lift picking a chip'))
if (-not $NoRender) {
    foreach ($s in $styles) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'shots.ps1') -Style $s[0] -Out (Join-Path $out $s[0])
        if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: shots for style $($s[0])"; exit 1 }
    }
}
Add-Type -AssemblyName System.Drawing
$cw = 583; $ch = 393; $head = 44; $lab = 26
$crop = New-Object Drawing.Rectangle 230, 380, 920, 620 # equipment region of the 1387x1000 frame
$sheet = New-Object Drawing.Bitmap ($cw * 3), ($head + 3 * ($lab + $ch))
$g = [Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = 'HighQualityBicubic'
$g.Clear([Drawing.Color]::White)
$big = New-Object Drawing.Font 'Arial', 16, ([Drawing.FontStyle]::Bold); $small = New-Object Drawing.Font 'Arial', 11
for ($c = 0; $c -lt 3; $c++) {
    $g.DrawString($styles[$c][1], $big, [Drawing.Brushes]::Black, ($c * $cw + 10), 10)
    for ($r = 0; $r -lt 3; $r++) {
        $y = $head + $r * ($lab + $ch)
        $g.DrawString($rows[$r][1], $small, [Drawing.Brushes]::DimGray, ($c * $cw + 10), ($y + 5))
        $img = [Drawing.Image]::FromFile((Join-Path $out "$($styles[$c][0])\$($rows[$r][0]).png"))
        $g.DrawImage($img, (New-Object Drawing.Rectangle ($c * $cw), ($y + $lab), ($cw - 4), $ch), $crop, [Drawing.GraphicsUnit]::Pixel)
        $img.Dispose()
    }
}
$g.Dispose()
$sheet.Save((Join-Path $out 'contact_sheet.png'), [Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
exit 0
