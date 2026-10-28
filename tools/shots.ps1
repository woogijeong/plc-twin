# Runs the built player at 1387x1000 with -mpsShot and waits for the captures (plan Step 6; 07 is an extra lift-pick shot).
# Build first: Unity -batchmode -quit -projectPath PlcTwin -executeMethod PlcTwin.EditorTools.SceneBuilder.BuildPlayer
# -Style a|b|c renders a look preview (player -style), -View default|front|iso|top a camera preset (player -view);
# -Out overrides the output folder (default tools\out\shots).
param([string]$Style = '', [string]$View = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root 'Build\PlcTwin.exe'
$dir  = if ($Out) { $Out } else { Join-Path $PSScriptRoot 'out\shots' }
$log  = Join-Path $dir 'player.log'
if (-not (Test-Path $exe)) { Write-Host "FAIL: $exe missing (build first)"; exit 1 }
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item (Join-Path $dir '*') -ErrorAction SilentlyContinue
$pargs = @('-screen-width', '1387', '-screen-height', '1000', '-screen-fullscreen', '0', '-mpsShot', "`"$dir`"", '-logFile', "`"$log`"")
if ($Style) { $pargs += @('-style', $Style) }
if ($View) { $pargs += @('-view', $View) }
$p = Start-Process $exe -PassThru -ArgumentList $pargs
if (-not $p.WaitForExit(120000)) { $p.Kill(); Write-Host 'FAIL: player did not quit within 120 s (killed)'; exit 1 }
$names = '01', '02', '03', '04', '05', '06a', '06b', '06c', '07'
$missing = $names | Where-Object { -not (Test-Path (Join-Path $dir "$_.png")) }
if ($missing) { Write-Host "FAIL: missing shots: $($missing -join ', ')"; exit 1 }
exit 0
