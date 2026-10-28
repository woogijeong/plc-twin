# Before/after sheet: Unity style A (tools/out/style_preview/a/05.png) beside the Blender previews.
# Run after build_mps.py:  powershell -File tools\blender\contact_sheet.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\out'
$tiles = @(
    @("$out\style_preview\a\05.png",       'BEFORE  Unity primitives, style A (05.png)'),
    @("$out\blender_preview\a_default.png", 'AFTER  Blender build_mps.py, same camera'),
    @("$out\blender_preview\a_front.png",   'AFTER  front-low (perspective)'),
    @("$out\blender_preview\a_top.png",     'AFTER  top (ortho)'))
$w = 1387; $h = 1000
$sheet = New-Object System.Drawing.Bitmap (2 * $w), (2 * $h)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font 'Segoe UI', 28, ([System.Drawing.FontStyle]::Bold)
for ($i = 0; $i -lt 4; $i++) {
    $img = [System.Drawing.Image]::FromFile((Resolve-Path $tiles[$i][0]))
    $x = ($i % 2) * $w; $y = [math]::Floor($i / 2) * $h
    $g.DrawImage($img, $x, $y, $w, $h)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 30, 34, 40))), $x, $y, $w, 56)
    $g.DrawString($tiles[$i][1], $font, [System.Drawing.Brushes]::White, ($x + 16), ($y + 6))
    $g.DrawRectangle([System.Drawing.Pens]::DarkGray, $x, $y, $w - 1, $h - 1)
    $img.Dispose()
}
$dst = Join-Path $out 'blender_preview\contact_sheet.png'
$sheet.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $sheet.Dispose()
"wrote $dst"
