# Modbus probe against ModbusServerMitsubishi (plan Step 1). TCP only, no PLC, never touches 'PLC Connect'.
# Starts the server, sends the 9 frames, writes tools/out/modbus_probe.txt (verdict block first),
# stops the server, restarts it (so no probe values remain), stops it again.
param(
    [string]$Exe = 'C:\Users\sozx2\바탕 화면\실습 자료\ModbusServerMitsubishi_20220706\ModbusServerMitsubishi_20220706.exe',
    [string]$OutFile = (Join-Path $PSScriptRoot 'out\modbus_probe.txt'),
    [int]$TimeoutMs = 2000
)
$ErrorActionPreference = 'Stop'
$procName = [IO.Path]::GetFileNameWithoutExtension($Exe)
$log = New-Object System.Text.StringBuilder
$tid = 0

function Hex([byte[]]$b, [int]$max = 0) {
    $n = if ($max -gt 0 -and $b.Length -gt $max) { $max } else { $b.Length }
    $s = ($b[0..([Math]::Max($n,1)-1)] | ForEach-Object { $_.ToString('X2') }) -join ' '
    if ($b.Length -eq 0) { $s = '' }
    if ($n -lt $b.Length) { $s += " ... (+$($b.Length - $n) bytes)" }
    $s
}
function Log([string]$s) { [void]$log.AppendLine($s) }

function Start-Server {
    Start-Process -FilePath $Exe -WindowStyle Minimized | Out-Null
    $t = [Diagnostics.Stopwatch]::StartNew()
    while ($t.ElapsedMilliseconds -lt 15000) {
        if (Get-NetTCPConnection -LocalPort 502 -State Listen -ErrorAction SilentlyContinue) { return }
        Start-Sleep -Milliseconds 200
    }
    throw 'server did not listen on 502 within 15 s'
}
function Stop-Server {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    $t = [Diagnostics.Stopwatch]::StartNew()
    while ((Get-Process -Name $procName -ErrorAction SilentlyContinue) -and $t.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 100 }
}

function New-Client {
    $c = New-Object Net.Sockets.TcpClient
    $c.NoDelay = $true
    $c.ReceiveTimeout = $TimeoutMs
    $c.SendTimeout = $TimeoutMs
    $iar = $c.BeginConnect('127.0.0.1', 502, $null, $null)
    if (-not $iar.AsyncWaitHandle.WaitOne($TimeoutMs)) { $c.Close(); throw 'connect timeout' }
    $c.EndConnect($iar)
    $c
}
function Close-Abrupt($c) { $c.Client.LingerState = New-Object Net.Sockets.LingerOption($true, 0); $c.Close() }

# Sends a request, collects the reply (first read waits up to TimeoutMs, then 300 ms idle gap ends it).
# Returns @{ Reply=[byte[]]; Ms=int; Err=string }
function Xchg($c, [string]$name, [byte[]]$req) {
    Log "--- $name"
    Log ("REQ len={0}: {1}" -f $req.Length, (Hex $req 40))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $buf = New-Object byte[] 1024
    $acc = New-Object Collections.Generic.List[byte]
    $err = ''
    try {
        $ns = $c.GetStream()
        $ns.Write($req, 0, $req.Length)
        $ns.ReadTimeout = $TimeoutMs
        $n = $ns.Read($buf, 0, $buf.Length)
        if ($n -eq 0) { $err = 'closed by peer (0 bytes)' }
        else {
            $acc.AddRange([byte[]]$buf[0..($n-1)])
            $ns.ReadTimeout = 300
            while ($true) {
                try { $n = $ns.Read($buf, 0, $buf.Length) } catch { break }
                if ($n -le 0) { break }
                $acc.AddRange([byte[]]$buf[0..($n-1)])
            }
        }
    } catch { $err = $_.Exception.Message -replace '\s+', ' ' }
    $ms = [int]$sw.ElapsedMilliseconds
    $rep = $acc.ToArray()
    Log ("REP len={0} elapsed={1} ms{2}: {3}" -f $rep.Length, $ms, $(if ($err) { " ERR=[$err]" } else { '' }), (Hex $rep 60))
    @{ Reply = $rep; Ms = $ms; Err = $err }
}

function U16([int]$v) { [byte[]]@((($v -shr 8) -band 0xFF), ($v -band 0xFF)) }
function Next-Tid { $script:tid++; U16 $script:tid }

# Frame builders. Standard = correct MBAP length; Ref = reference 256-byte buffers.
function Std-Read([int]$fc, [int]$start, [int]$qty) {
    [byte[]](@(Next-Tid) + @(0,0, 0,6, 1, $fc) + @(U16 $start) + @(U16 $qty))
}
function Std-Write([int]$start, [int[]]$vals) {
    $bc = 2 * $vals.Count
    $h = @(Next-Tid) + @(0,0) + @(U16 (7 + $bc)) + @(1, 0x10) + @(U16 $start) + @(U16 $vals.Count) + @($bc)
    $d = foreach ($v in $vals) { U16 ($v -band 0xFFFF) }
    [byte[]]($h + @($d))
}
function Ref-Read {
    $b = New-Object byte[] 256
    $t = Next-Tid; $b[0] = $t[0]; $b[1] = $t[1]   # distinct TID per frame so replies are distinguishable
    $b[5] = 6; $b[6] = 1; $b[7] = 4
    $b
}
function Ref-Write {
    $b = New-Object byte[] 256
    $t = Next-Tid; $b[0] = $t[0]; $b[1] = $t[1]
    $b[5] = 6; $b[6] = 1; $b[7] = 0x10; $b[11] = 80; $b[12] = 0   # length 6, qty 80, byte count 0
    $b
}

# Parse standard read reply into words (null if not a plain FC3/FC4 reply of qty words).
function Words($rep, [int]$qty) {
    if ($rep.Length -lt 9 + 2 * $qty -or ($rep[7] -band 0x80)) { return $null }
    $w = New-Object int[] $qty
    for ($i = 0; $i -lt $qty; $i++) { $w[$i] = ($rep[9 + 2*$i] -shl 8) -bor $rep[10 + 2*$i] }
    , $w
}
function Words-Str($w) { if ($null -eq $w) { 'n/a' } else { ($w | ForEach-Object { $_.ToString('X4') }) -join ' ' } }
function Same($a, $b) { $null -ne $a -and $null -ne $b -and (@(Compare-Object $a $b)).Count -eq 0 }

$V = [ordered]@{}   # verdict values
$E = @{}            # evidence frame per verdict line
$c1 = $null
try {
    Stop-Server
    Start-Server
    Log "server started: $Exe ; listening on 127.0.0.1:502 (pid $((Get-Process -Name $procName).Id -join ','))"
    $c = New-Client

    # Frame 1: reference read
    $r1 = Xchg $c 'F1 reference read (256-byte buffer, FC4, qty 0)' (Ref-Read)
    # Frame 2: standard FC4 read start 0 qty 47
    $r2 = Xchg $c 'F2 standard FC4 read start 0 qty 47' (Std-Read 4 0 47)
    $w2 = Words $r2.Reply 47
    Log ("F2 words: " + (Words-Str $w2))
    # Frame 3: standard FC3 read start 0 qty 80
    $r3 = Xchg $c 'F3 standard FC3 read start 0 qty 80' (Std-Read 3 0 80)
    $w3 = Words $r3.Reply 80
    Log ("F3 words: " + (Words-Str $w3))

    # Frame 4: full-table write with sentinel
    $full = New-Object int[] 80
    for ($i = 4; $i -lt 80; $i++) { $full[$i] = 0xA5A5 }
    $full[0] = 1; $full[1] = 2; $full[2] = 3; $full[3] = 4
    for ($i = 40; $i -le 44; $i++) { $full[$i] = $i }
    $r4 = Xchg $c 'F4 standard FC16 start 0 qty 80 bytecount 160 (A5A5 sentinel, D0-3=1..4, D40-44=40..44)' (Std-Write 0 $full)
    $r7a = Xchg $c 'F7a FC3 readback after F4' (Std-Read 3 0 80)
    $w7a = Words $r7a.Reply 80
    Log ("F7a words: " + (Words-Str $w7a))

    # Frame 5: partial writes
    $r5a = Xchg $c 'F5a standard FC16 start 0 qty 4 (0x1111 pattern)' (Std-Write 0 @(0x1111, 0x1112, 0x1113, 0x1114))
    $r5b = Xchg $c 'F5b standard FC16 start 40 qty 5 (0x2222 pattern)' (Std-Write 40 @(0x2222, 0x2223, 0x2224, 0x2225, 0x2226))
    $r7b = Xchg $c 'F7b FC3 readback after F5a+F5b' (Std-Read 3 0 80)
    $w7b = Words $r7b.Reply 80
    Log ("F7b words: " + (Words-Str $w7b))

    # Frame 6: reference write (zero-fill). Only after 4 and 5 are recorded.
    $r6 = Xchg $c 'F6 reference write (256-byte buffer, length 6, qty 80, bytecount 0)' (Ref-Write)
    $r7c = Xchg $c 'F7c FC3 readback after F6' (Std-Read 3 0 80)
    $w7c = Words $r7c.Reply 80
    Log ("F7c words: " + (Words-Str $w7c))
    $c.Close()

    # Frame 8: abrupt close + reconnect within 1 s, repeat frame 2
    $c = New-Client
    $r8a = Xchg $c 'F8a frame 2 before abrupt close' (Std-Read 4 0 47)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Close-Abrupt $c
    $r8b = $null; $reconnErr = ''
    try {
        $c = New-Client
        Log ("F8 reconnect OK after {0} ms" -f $sw.ElapsedMilliseconds)
        $r8b = Xchg $c 'F8b frame 2 after abrupt close + reconnect' (Std-Read 4 0 47)
    } catch { $reconnErr = $_.Exception.Message; Log "F8 reconnect FAILED: $reconnErr" }
    if ($c) { $c.Close() }

    # Frame 9: two simultaneous connections
    $ca = New-Client
    $r9a1 = Xchg $ca 'F9 A: frame 2 (A open alone)' (Std-Read 4 0 47)
    $cb = $null; $r9b = $null; $r9a2 = $null; $bConnErr = ''
    try { $cb = New-Client } catch { $bConnErr = $_.Exception.Message; Log "F9 B connect FAILED: $bConnErr" }
    if ($cb) {
        $r9b = Xchg $cb 'F9 B: frame 2 (second client, A still open)' (Std-Read 4 0 47)
        $r9a2 = Xchg $ca 'F9 A: frame 2 again (while B open)' (Std-Read 4 0 47)
        $cb.Close()
    }
    $ca.Close()

    # ---- verdicts, each derived from captured bytes ----
    $ok = { param($r) $r -and -not $r.Err -and $r.Reply.Length -gt 0 }

    # read frame: reference accepted iff F1 answered with a non-exception FC4 reply
    $ref1ok = (& $ok $r1) -and $r1.Reply.Length -ge 8 -and $r1.Reply[7] -eq 4
    $std2ok = (& $ok $r2) -and $r2.Reply.Length -ge 8 -and $r2.Reply[7] -eq 4
    $V['read frame'] = if ($std2ok) { 'standard' } elseif ($ref1ok) { 'reference' } else { 'neither answered' }
    $E['read frame'] = "F1 (reference) answered=$ref1ok len=$($r1.Reply.Length) err=[$($r1.Err)]; F2 (standard) answered=$std2ok len=$($r2.Reply.Length) err=[$($r2.Err)]. Standard chosen when F2 is answered with function code 4 (only the function code is checked, not the reply length; the server returns the whole 80-word table)."

    # reply framing: from F2 reply length vs MBAP length field
    if ($std2ok) {
        $mbapLen = ($r2.Reply[4] -shl 8) -bor $r2.Reply[5]
        $total = $r2.Reply.Length
        $V['reply framing'] = if ($total -eq 6 + $mbapLen -and $total -ne 256) { 'MBAP length' } elseif ($total -eq 256) { 'fixed 256' } elseif ($total -eq 9 + $r2.Reply[8]) { "other: header 9 B + byte count $($r2.Reply[8]) = $total B (always 80 words, qty ignored), MBAP length field wrong ($mbapLen)" } else { "other (total=$total, MBAP length field=$mbapLen)" }
        $E['reply framing'] = "F2 reply total=$total bytes, MBAP length field=$mbapLen (6+length=$(6+$mbapLen))"
    } else { $V['reply framing'] = if ($ref1ok) { "from F1: total=$($r1.Reply.Length)" } else { 'other (no reply)' }; $E['reply framing'] = 'F2 unanswered' }

    # FC3 readback: F7a equals what F4 wrote
    $fc3ok = (& $ok $r3) -and ($null -ne $w3)
    $rb = Same $w7a $full
    $V['FC3 readback'] = if ($rb) { 'yes' } else { 'no' }
    $E['FC3 readback'] = "F3 answered=$fc3ok; F7a words equal F4 payload (80 words)=$rb"

    # partial FC16 honored: only derivable from readback
    if (-not $rb) { $V['partial FC16 honored'] = 'unknown'; $E['partial FC16 honored'] = 'no usable readback (F7a != F4 payload), so partial writes cannot be judged' }
    else {
        $exp = $full.Clone()
        for ($i = 0; $i -lt 4; $i++) { $exp[$i] = 0x1111 + $i }
        for ($i = 0; $i -lt 5; $i++) { $exp[40 + $i] = 0x2222 + $i }
        $honored = Same $w7b $exp
        $V['partial FC16 honored'] = if ($honored) { 'yes' } else { 'no' }
        $E['partial FC16 honored'] = "F7b equals F4 table with only D0-3=0x1111.. and D40-44=0x2222.. changed (D4-39, D45-79 still A5A5) = $honored; F5a/F5b answered: $(& $ok $r5a)/$(& $ok $r5b)"
    }

    # answers without PLC: any valid Modbus reply to F2 or F1 (no PLC is attached to this server)
    $V['server answers without PLC'] = if ($std2ok -or $ref1ok) { 'yes' } else { 'no' }
    $E['server answers without PLC'] = "F1/F2 replies received with no PLC and no GX Works2 attached (F1 len=$($r1.Reply.Length), F2 len=$($r2.Reply.Length))"

    $V['reconnect after abrupt close'] = if ($r8b -and (& $ok $r8b)) { 'yes' } else { 'no' }
    $E['reconnect after abrupt close'] = if ($reconnErr) { "reconnect failed: $reconnErr" } else { "F8b reply len=$($r8b.Reply.Length) err=[$($r8b.Err)]; F8a len=$($r8a.Reply.Length)" }

    if ($bConnErr) { $V['second connection'] = 'refused'; $E['second connection'] = "B connect failed: $bConnErr" }
    elseif ((& $ok $r9b) -and (& $ok $r9a2)) { $V['second connection'] = 'served'; $E['second connection'] = "F9 B reply len=$($r9b.Reply.Length) in $($r9b.Ms) ms; F9 A reply len=$($r9a2.Reply.Length) in $($r9a2.Ms) ms" }
    elseif (-not (& $ok $r9a2)) { $V['second connection'] = 'starves first'; $E['second connection'] = "A unanswered after B opened: err=[$($r9a2.Err)]; B err=[$($r9b.Err)]" }
    else { $V['second connection'] = 'refused (TCP accepted, never served: B read timed out; first connection not starved)'; $E['second connection'] = "B err=[$($r9b.Err)]; A still answered" }
}
finally {
    foreach ($x in @($c, $ca, $cb)) { if ($x) { try { $x.Close() } catch {} } }
    Stop-Server
}

# Restart so no probe values remain, then close again.
Start-Server
Log "server restarted after probe (pid $((Get-Process -Name $procName).Id -join ','))"
Stop-Server
$left = @(Get-Process -Name $procName -ErrorAction SilentlyContinue).Count
Log "processes named $procName remaining after final stop: $left"

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$head = New-Object System.Text.StringBuilder
[void]$head.AppendLine("=== VERDICT (probe run $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) ===")
foreach ($k in $V.Keys) { [void]$head.AppendLine(("{0} = {1}" -f $k, $V[$k])) }
[void]$head.AppendLine('--- evidence (frame that proves each line) ---')
foreach ($k in $V.Keys) { [void]$head.AppendLine(("{0}: {1}" -f $k, $E[$k])) }
[void]$head.AppendLine('server restarted after probe')
[void]$head.AppendLine('=== RAW FRAMES ===')
[IO.File]::WriteAllText($OutFile, $head.ToString() + $log.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Host "wrote $OutFile"
