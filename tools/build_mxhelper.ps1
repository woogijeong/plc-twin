# Builds MxHelper.exe (32-bit) next to the Unity StreamingAssets. Needs MX Component (C:\MELSEC\Act), .NET Framework csc and the Windows SDK TlbImp.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'PlcTwin\Assets\StreamingAssets'
$tmp = Join-Path $env:TEMP 'mxhelper_build'
New-Item -ItemType Directory -Force $tmp | Out-Null
$tlbimp = (Get-ChildItem 'C:\Program Files (x86)\Microsoft SDKs\Windows' -Recurse -Filter tlbimp.exe | Select-Object -First 1).FullName
& $tlbimp 'C:\MELSEC\Act\Control\ActUtlType.tlb' /out:"$tmp\Interop.ActUtlType.dll" /namespace:ActUtl /machine:X86 | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /platform:x86 /target:exe /optimize /link:"$tmp\Interop.ActUtlType.dll" /out:"$out\MxHelper.exe" "$PSScriptRoot\mxhelper\MxHelper.cs"
if ($LASTEXITCODE) { throw 'csc failed' }
"built $out\MxHelper.exe"
