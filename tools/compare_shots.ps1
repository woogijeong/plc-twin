# Compares tools/out/shots/NN.png with the full-window reference captures (plan Step 6, AC11/AC12).
# (a) panel + buttons: 68 rects, 6x6 inset background patches at 4 corners, classified by color, must match the ref.
# (b) equipment: color masks in the 3D region (UI rects and the ref branding box masked), centroid <= 15 px, count ratio 0.7-1.3.
# (c) values: D40 from the scenario log (152/52/102) vs the ref values recorded in Step 0 (152/51/101), +-1.
# Negative controls: ref vs itself passes; ref 01 vs ref 03 fails the chip mask and exactly D000.0, D000.1, D001.0.
# Writes tools/out/compare.txt and tools/out/compare/side_by_side_NN.png; exit 1 on any failure.
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$shots = Join-Path $PSScriptRoot 'out\shots'
$refs  = Join-Path $root '.omc\specs\mps-ref'
$outDir = Join-Path $PSScriptRoot 'out\compare'
New-Item -ItemType Directory -Force $outDir | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging; using System.Linq;
public class Img {
  public int W, H; public int[] P;
  public Img(string path) {
    using (var b = new Bitmap(path)) {
      W = b.Width; H = b.Height; P = new int[W * H];
      var d = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      for (int y = 0; y < H; y++) System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, P, y * W, W);
      b.UnlockBits(d);
    }
  }
  public int Patch(int x0, int y0) { // per-channel median of a 6x6 patch, 0xRRGGBB
    var r = new List<int>(); var g = new List<int>(); var bl = new List<int>();
    for (int y = y0; y < y0 + 6; y++) for (int x = x0; x < x0 + 6; x++) { int c = P[y * W + x]; r.Add(c >> 16 & 255); g.Add(c >> 8 & 255); bl.Add(c & 255); }
    r.Sort(); g.Sort(); bl.Sort(); return r[18] << 16 | g[18] << 8 | bl[18];
  }
}
public static class Cmp {
  // Panel classes: code colors (ref 5.2/5.3) plus screen-measured ref variants. ponytail: calibrated, distance <= 25 in RGB.
  static readonly string[] Names = { "ON", "OFF", "TITLE", "VALUE", "METAL", "GRAY" };
  static readonly int[][] Colors = {
    new[] { 0x4EE90A, 0x4BE00A }, new[] { 0xFF896D, 0xF58469 }, new[] { 0xEEC647, 0xE5BF45 }, new[] { 0xFFFBDA, 0xF5F1D2 },
    new[] { 0x4380C9, 0x4079C0 }, new[] { 0xC8C8C8, 0xC0C0C0 } };
  public static string Classify(int c) {
    string best = null; double bd = 25.0001;
    for (int i = 0; i < Names.Length; i++) foreach (int k in Colors[i]) {
      double d = Math.Sqrt(Sq((c >> 16 & 255) - (k >> 16 & 255)) + Sq((c >> 8 & 255) - (k >> 8 & 255)) + Sq((c & 255) - (k & 255)));
      if (d < bd) { bd = d; best = Names[i]; } }
    return best ?? string.Format("?{0:X6}", c);
  }
  static double Sq(double v) { return v * v; }
  // Rect class from the TL patch; "MIXED" when a corner differs (rect shifted > ~4 px), "?" when unclassified.
  public static string RectClass(Img im, int x, int y, int w, int h) {
    var cs = new[] { Classify(im.Patch(x + 4, y + 4)), Classify(im.Patch(x + w - 10, y + 4)), Classify(im.Patch(x + 4, y + h - 10)), Classify(im.Patch(x + w - 10, y + h - 10)) };
    if (cs.Any(c => c.StartsWith("?"))) return cs.First(c => c.StartsWith("?"));
    return cs.All(c => c == cs[0]) ? cs[0] : "MIXED(" + string.Join(",", cs) + ")";
  }
  static void Hsv(int c, out double h, out double s, out double v) {
    double r = (c >> 16 & 255) / 255.0, g = (c >> 8 & 255) / 255.0, b = (c & 255) / 255.0;
    double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
    v = mx; s = mx == 0 ? 0 : d / mx;
    if (d == 0) h = 0; else if (mx == r) h = 60 * (((g - b) / d) % 6); else if (mx == g) h = 60 * ((b - r) / d + 2); else h = 60 * ((r - g) / d + 4);
    if (h < 0) h += 360;
  }
  // Equipment masks. ponytail: calibrated hue/saturation windows from the ref captures.
  public static readonly string[] Masks = { "yellow", "purple", "lightblue", "cream", "orange", "chip" };
  static bool In(string m, int c) {
    double h, s, v; Hsv(c, out h, out s, out v);
    switch (m) {
      case "yellow": return h >= 50 && h <= 66 && s > 0.6 && v > 0.5;
      case "purple": return h >= 250 && h <= 290 && s > 0.15 && s < 0.6 && v > 0.45;
      case "lightblue": return h >= 195 && h <= 212 && s > 0.35 && v > 0.75;
      case "cream": return h >= 50 && h <= 72 && s > 0.12 && s < 0.4 && v > 0.75;
      case "orange": return h >= 30 && h <= 48 && s > 0.45 && v > 0.5;
      case "chip": return h >= 225 && h <= 250 && s > 0.8 && v > 0.35;
    }
    return false;
  }
  // count, centroid x, centroid y of a mask outside the excluded rects (x, y, w, h quadruples).
  public static double[] Mask(Img im, string m, int[] ex) {
    long n = 0; double sx = 0, sy = 0;
    for (int y = 0; y < im.H; y++) for (int x = 0; x < im.W; x++) {
      bool skip = false;
      for (int i = 0; i < ex.Length; i += 4) if (x >= ex[i] && x < ex[i] + ex[i + 2] && y >= ex[i + 1] && y < ex[i + 1] + ex[i + 3]) { skip = true; break; }
      if (skip || !In(m, im.P[y * im.W + x])) continue;
      n++; sx += x; sy += y;
    }
    return new[] { n, n > 0 ? sx / n : 0, n > 0 ? sy / n : 0 };
  }
}
'@

$pairs = [ordered]@{
  '01' = '01-idle-full-window-full.png'; '02' = '02-metal-chip-in-magazine-full.png'; '03' = '03-supply-forward-chip-at-drill-full.png'
  '04' = '04-drill-down-held-full.png'; '05' = '05-emit-forward-chip-on-conveyor-full.png'; '06a' = '06a-servo-152-full.png'
  '06b' = '06b-servo-51-full.png'; '06c' = '06c-servo-102-full.png' }
$refD40 = @{ '06a' = 152; '06b' = 51; '06c' = 101 }
$expD40 = @{ '06a' = 152; '06b' = 52; '06c' = 102 }

# Rects (pixels at 1387x1000) come from PanelLayout via the scenario (one table).
$rects = Get-Content (Join-Path $shots 'rects.txt') | ForEach-Object { $f = $_ -split ' '; [pscustomobject]@{ Kind = $f[0]; Name = $f[1]; X = [int]$f[2]; Y = [int]$f[3]; W = [int]$f[4]; H = [int]$f[5] } }
$checked = @($rects | Where-Object { $_.Kind -ne 'Extra' })                     # 64 panel rects + 4 buttons
$ex = New-Object System.Collections.Generic.List[int]
foreach ($r in $rects) { $ex.AddRange([int[]]@(($r.X - 3), ($r.Y - 3), ($r.W + 6), ($r.H + 6))) }
$ex.AddRange([int[]]@(810, 392, 307, 106))   # ref branding box (+270, +55, 300x100) not reproduced (OQ-5)
$ex = $ex.ToArray()
$minPix = 40

function Compare-Pair($refPath, $oursPath, $label) {
  $a = New-Object Img $refPath; $b = New-Object Img $oursPath
  $fails = @(); $panelFails = @(); $maskFails = @()
  foreach ($r in $checked) {
    $ca = [Cmp]::RectClass($a, $r.X, $r.Y, $r.W, $r.H); $cb = [Cmp]::RectClass($b, $r.X, $r.Y, $r.W, $r.H)
    if ($ca -ne $cb -or $ca.StartsWith('?') -or $ca.StartsWith('MIXED')) { $panelFails += $r.Name; $fails += "panel $($r.Name): ref=$ca ours=$cb" }
  }
  $lines = @()
  foreach ($m in [Cmp]::Masks) {
    $ma = [Cmp]::Mask($a, $m, $ex); $mb = [Cmp]::Mask($b, $m, $ex)
    $ea = $ma[0] -lt $minPix; $eb = $mb[0] -lt $minPix
    $d = [math]::Sqrt([math]::Pow($ma[1] - $mb[1], 2) + [math]::Pow($ma[2] - $mb[2], 2))
    $ratio = if ($ma[0] -gt 0) { $mb[0] / $ma[0] } else { 0 }
    $ok = ($ea -and $eb) -or (-not $ea -and -not $eb -and $d -le 15 -and $ratio -ge 0.7 -and $ratio -le 1.3)
    $lines += ('  mask {0,-9} ref n={1,6} c=({2,4:0},{3,4:0})  ours n={4,6} c=({5,4:0},{6,4:0})  dist={7,5:0.0} ratio={8:0.00} {9}' -f $m, $ma[0], $ma[1], $ma[2], $mb[0], $mb[1], $mb[2], $d, $ratio, $(if ($ok) { 'ok' } else { 'FAIL' }))
    if (-not $ok) { $maskFails += $m; $fails += "mask $m" }
  }
  [pscustomobject]@{ Label = $label; Fails = $fails; PanelFails = $panelFails; MaskFails = $maskFails; Lines = $lines }
}

$report = @("compare_shots $(Get-Date -Format s)  (rects: $($checked.Count) panel+button rects, masks min $minPix px)")
$failed = $false
$log = Get-Content (Join-Path $shots 'player.log') -ErrorAction SilentlyContinue
foreach ($k in $pairs.Keys) {
  $ref = Join-Path $refs $pairs[$k]; $ours = Join-Path $shots "$k.png"
  $r = Compare-Pair $ref $ours $k
  if ($expD40.ContainsKey($k)) {
    $line = $log | Select-String "shot $k D40=(\d+)" | Select-Object -First 1
    $v = if ($line) { [int]$line.Matches[0].Groups[1].Value } else { -1 }
    $okV = $v -eq $expD40[$k] -and [math]::Abs($v - $refD40[$k]) -le 1
    $r.Lines += "  D40 ours=$v expected=$($expD40[$k]) ref=$($refD40[$k]) $(if ($okV) { 'ok' } else { 'FAIL' })"
    if (-not $okV) { $r.Fails += 'D40' }
  }
  $status = if ($r.Fails.Count) { 'FAIL'; $failed = $true } else { 'PASS' }
  $report += "$k vs $($pairs[$k]): $status"; $report += $r.Lines; $report += $r.Fails | ForEach-Object { "  - $_" }
  # side by side: ref | ours
  $ia = [System.Drawing.Image]::FromFile($ref); $ib = [System.Drawing.Image]::FromFile($ours)
  $sb = New-Object System.Drawing.Bitmap ($ia.Width + $ib.Width + 8), ([math]::Max($ia.Height, $ib.Height))
  $g = [System.Drawing.Graphics]::FromImage($sb); $g.Clear([System.Drawing.Color]::Black)
  $g.DrawImage($ia, 0, 0, $ia.Width, $ia.Height); $g.DrawImage($ib, $ia.Width + 8, 0, $ib.Width, $ib.Height)
  $g.Dispose(); $sb.Save((Join-Path $outDir "side_by_side_$k.png"), [System.Drawing.Imaging.ImageFormat]::Png); $sb.Dispose(); $ia.Dispose(); $ib.Dispose()
}

# Negative controls
$r1 = Join-Path $refs $pairs['01']; $r3 = Join-Path $refs $pairs['03']
$self = Compare-Pair $r1 $r1 'ref01-vs-ref01'
$selfOk = $self.Fails.Count -eq 0
$neg = Compare-Pair $r1 $r3 'ref01-vs-ref03'
$negOk = ($neg.MaskFails -contains 'chip') -and ((($neg.PanelFails | Sort-Object) -join ',') -eq 'D000.0,D000.1,D001.0')
$report += "negative control ref01 vs ref01 (must pass): $(if ($selfOk) { 'ok' } else { 'FAIL' })"; $report += $self.Fails | ForEach-Object { "  - $_" }
$report += "negative control ref01 vs ref03 (must fail chip + exactly D000.0,D000.1,D001.0): $(if ($negOk) { 'ok' } else { 'FAIL' })"
$report += "  panel fails: $($neg.PanelFails -join ','); mask fails: $($neg.MaskFails -join ',')"
if (-not ($selfOk -and $negOk)) { $failed = $true }
$report += "RESULT: $(if ($failed) { 'FAIL' } else { 'PASS' })"
Set-Content -Path (Join-Path $PSScriptRoot 'out\compare.txt') -Value $report -Encoding UTF8
Copy-Item (Join-Path $PSScriptRoot 'out\compare.txt') (Join-Path $outDir 'compare.txt')
if ($failed) { exit 1 }
exit 0
