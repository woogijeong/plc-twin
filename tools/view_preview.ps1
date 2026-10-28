# Camera view preview: renders the shot set with -style a for each view preset (tools\shots.ps1 -View) into
# tools\out\view_preview\{default,front,iso,top}\ and composes tools\out\view_preview\contact_sheet.png (3D band between the
# panel rows cropped; columns Default/Front/Iso/Top, rows 03, 05, 07). If the desktop was not presented (offscreen shots, no
# panel) it also draws panel_mock.png: the Front 05 render with the panel rects from rects.txt painted over it.
# Build first (SceneBuilder.BuildPlayer). -NoRender only recomposes from existing PNGs.
param([switch]$NoRender)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'out\view_preview'
$views = @(@('default', 'Default'), @('front', 'Front'), @('iso', 'Iso'), @('top', 'Top'))
$rows = @(@('03', '03 chip on the drill stage'), @('05', '05 chip pushed onto the belt'), @('07', '07 lift picking a chip'))
if (-not $NoRender) {
    foreach ($v in $views) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'shots.ps1') -Style a -View $v[0] -Out (Join-Path $out $v[0])
        if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: shots for view $($v[0])"; exit 1 }
    }
}
Add-Type -AssemblyName System.Drawing
$cw = 640; $ch = 254; $head = 44; $lab = 26
$crop = New-Object Drawing.Rectangle 0, 370, 1387, 550 # between the word value row and the bottom row of the 1387x1000 frame
$sheet = New-Object Drawing.Bitmap ($cw * 4), ($head + 3 * ($lab + $ch))
$g = [Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = 'HighQualityBicubic'
$g.Clear([Drawing.Color]::White)
$big = New-Object Drawing.Font 'Arial', 16, ([Drawing.FontStyle]::Bold); $small = New-Object Drawing.Font 'Arial', 11
for ($c = 0; $c -lt 4; $c++) {
    $g.DrawString($views[$c][1], $big, [Drawing.Brushes]::Black, ($c * $cw + 10), 10)
    for ($r = 0; $r -lt 3; $r++) {
        $y = $head + $r * ($lab + $ch)
        $g.DrawString($rows[$r][1], $small, [Drawing.Brushes]::DimGray, ($c * $cw + 10), ($y + 5))
        $img = [Drawing.Image]::FromFile((Join-Path $out "$($views[$c][0])\$($rows[$r][0]).png"))
        $g.DrawImage($img, (New-Object Drawing.Rectangle ($c * $cw), ($y + $lab), ($cw - 4), $ch), $crop, [Drawing.GraphicsUnit]::Pixel)
        $img.Dispose()
    }
}
$g.Dispose()
$sheet.Save((Join-Path $out 'contact_sheet.png'), [Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

$front = Join-Path $out 'front'
if (Select-String -Path (Join-Path $front 'player.log') -Pattern 'offscreen' -Quiet) {
    $img = New-Object Drawing.Bitmap (Join-Path $front '05.png')
    $g = [Drawing.Graphics]::FromImage($img)
    $font = New-Object Drawing.Font 'Arial', 8
    $fill = @{ Bit = 'FF896D'; WordTitle = 'EEC647'; WordValue = 'FFFBDA'; Metal = '4380C9'; Nonmetal = 'F58469'; Connect = '4EE90A'; Panel = '4685D1'; Front = '4685D1' }
    foreach ($line in Get-Content (Join-Path $front 'rects.txt')) {
        $kind, $name, $x, $y, $w, $h = $line.Split(' ')
        $rect = New-Object Drawing.Rectangle ([int]$x), ([int]$y), ([int]$w), ([int]$h)
        $hex = if ($fill[$name]) { $fill[$name] } elseif ($fill[$kind]) { $fill[$kind] } elseif ($name -eq 'State' -or $name -eq 'IP' -or $name -eq 'Port') { $null } else { 'C8C8C8' }
        if ($hex) { $g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#' + $hex))), $rect) }
        $g.DrawRectangle([Drawing.Pens]::Gray, $rect)
        $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        $g.DrawString($name,$font, [Drawing.Brushes]::Black, [Drawing.RectangleF]$rect, $sf)
    }
    $g.Dispose()
    $img.Save((Join-Path $out 'panel_mock.png'), [Drawing.Imaging.ImageFormat]::Png)
    $img.Dispose()
}
exit 0
