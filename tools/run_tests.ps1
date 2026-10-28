# Runs Unity EditMode tests in batchmode. Exit 0 only if Unity exited 0, results.xml shows no failures, and the log has no 'error CS'.
$ErrorActionPreference = 'Stop'
$unity   = 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'
$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'PlcTwin'
$out     = Join-Path $PSScriptRoot 'out'
$results = Join-Path $out 'results.xml'
$log     = Join-Path $out 'unity.log'

# Fail fast if the project is already open (editor or stale batch process).
$projLike = $project.ToLower()
$busy = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.ToLower().Contains($projLike) }
if ($busy) { Write-Host "FAIL: Unity (PID $($busy.ProcessId -join ',')) already has this project open. Close it first."; exit 2 }
$lock = Join-Path $project 'Temp\UnityLockfile'
if (Test-Path $lock) {
    try { $fs = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $fs.Close() }
    catch { Write-Host "FAIL: $lock is locked (project open in another Unity instance)."; exit 2 }
}

New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item $results, $log -ErrorAction SilentlyContinue

# No -quit with -runTests.
$p = Start-Process -FilePath $unity -PassThru -Wait -ArgumentList @(
    '-batchmode', '-projectPath', "`"$project`"", '-runTests', '-testPlatform', 'EditMode',
    '-testResults', "`"$results`"", '-logFile', "`"$log`"")
$code = $p.ExitCode

$fail = @()
if ($code -ne 0) { $fail += "Unity exit code $code" }
if (Test-Path $log) { if (Select-String -Path $log -Pattern 'error CS' -Quiet) { $fail += "compile errors ('error CS') in $log" } }
else { $fail += "log missing: $log" }
if (-not (Test-Path $results)) { $fail += "results.xml missing" }
else {
    $r = ([xml](Get-Content $results -Raw)).'test-run'
    $summary = "passed=$($r.passed) failed=$($r.failed) total=$($r.total) result=$($r.result)"
    Set-Content -Path (Join-Path $out 'summary.txt') -Value $summary
    if ([int]$r.failed -gt 0 -or $r.result -like 'Failed*') { $fail += "tests failed ($summary)" }
}
# Silent on success: 'omc ralph verify' treats any output line as a failure signature. Summary is in tools\out\summary.txt.
if ($fail) { Write-Host ("FAIL: " + ($fail -join '; ')); exit 1 }
exit 0
