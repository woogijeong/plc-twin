using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using PlcTwin.Mc;
using PlcTwin.Twin;

namespace PlcTwin.Tests
{
    /// <summary>MX transport: line protocol helpers, a fake helper process (PowerShell script, no MX Component needed), config.</summary>
    public class MxTransportTests
    {
        const string BaseJson = "{\"transport\":\"mc3e\"," +
            "\"mc3e\":{\"ip\":\"192.168.3.39\",\"port\":5010,\"monitorTimer250ms\":4,\"pollMs\":100}," +
            "\"modbus\":{\"ip\":\"127.0.0.1\",\"port\":502,\"unitId\":1,\"pollMs\":200,\"writeTableBase\":0,\"readTableBase\":100}," +
            "MX\"connectTimeoutMs\":3000,\"timeoutMs\":1500," +
            "\"map\":{\"inBits\":\"D0\",\"inWords\":\"D40\",\"outBits\":\"D100\",\"outWords\":\"D140\"},\"panelVisible\":true}";
        static string Json(string mx = "") => BaseJson.Replace("MX", mx);

        [Test]
        public void BuildFrames()
        {
            Assert.AreEqual("OPEN 1", MxTransport.BuildOpen(1));
            Assert.AreEqual("R 100 4", MxTransport.BuildRead(100, 4));
            Assert.AreEqual("W 40 140 -2 0", MxTransport.BuildWrite(40, new short[] { 140, -2, 0 }));
        }

        [Test]
        public void ParseReply()
        {
            CollectionAssert.AreEqual(new short[] { 1, -2, 3 }, MxTransport.ParseReply("OK 1 -2 3"));
            Assert.IsEmpty(MxTransport.ParseReply("OK"));
            var e = Assert.Throws<MxException>(() => MxTransport.ParseReply("ERR 0100000A"));
            StringAssert.Contains("0100000A", e.Message);
            Assert.Throws<MxException>(() => MxTransport.ParseReply(null));
            Assert.Throws<MxException>(() => MxTransport.ParseReply("OK 99999"));
        }

        // Fake helper: same protocol as MxHelper.exe over a 400-word memory. "slow" station never answers R (timeout test).
        const string FakeScript = @"
$m = New-Object 'int16[]' 400; $open = $false; $slow = $false
while (($l = [Console]::In.ReadLine()) -ne $null) {
  $p = $l.Trim().Split(' ')
  switch ($p[0]) {
    'Q' { exit }
    'OPEN' { if ($p[1] -eq '9') { 'ERR 01010005' } else { $open = $true; $slow = ($p[1] -eq '8'); 'OK' } }
    'R' { if ($slow) { Start-Sleep 5 } else { 'OK ' + (($m[[int]$p[1]..([int]$p[1] + [int]$p[2] - 1)]) -join ' ') } }
    'W' { for ($i = 2; $i -lt $p.Length; $i++) { $m[[int]$p[1] + $i - 2] = [int16]$p[$i] }; 'OK' }
    default { 'ERR unknown command' }
  }
}";

        static string script;
        static string Args => "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
        const string Ps = "powershell.exe";
        static string PsPath => Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", @"System32\WindowsPowerShell\v1.0\powershell.exe");

        [OneTimeSetUp]
        public void WriteScript() { script = Path.Combine(Path.GetTempPath(), "fake_mx_helper.ps1"); File.WriteAllText(script, FakeScript); }

        static MxTransport Open(int station, int timeoutMs = 3000) =>
            MxTransport.Connect(PsPath, Args, station, 15000, timeoutMs, new ManualResetEvent(false));

        [Test]
        public void WriteThenReadRoundTrip()
        {
            using (var t = Open(1))
            {
                t.WriteWords(40, new short[] { 140, 0, -1 });
                t.WriteWords(0, new short[] { 0x7FFF });
                CollectionAssert.AreEqual(new short[] { 140, 0, -1 }, t.ReadWords(40, 3));
                CollectionAssert.AreEqual(new short[] { 0x7FFF, 0 }, t.ReadWords(0, 2));
            }
        }

        [Test]
        public void OpenError_ThrowsMxExceptionAndLeavesNoProcess()
        {
            var e = Assert.Throws<MxException>(() => Open(9));
            StringAssert.Contains("01010005", e.Message);
        }

        [Test]
        public void SlowReply_TimesOutAndTransportIsDead()
        {
            using (var t = Open(8, 400))
            {
                var e = Assert.Throws<TimeoutException>(() => t.ReadWords(0, 1));
                Assert.AreEqual("timeout", e.Message);
                Assert.Catch(() => t.ReadWords(0, 1)); // dead for good: caller disposes and reconnects
            }
        }

        [Test]
        public void MissingHelperFile()
        {
            Assert.Throws<FileNotFoundException>(() => MxTransport.Connect(@"C:\no\such\MxHelper.exe", "", 1, 1000, 1000, new ManualResetEvent(false)));
        }

        [Test]
        public void ConfigWithoutMxSection_DefaultsToStation1At100ms()
        {
            var (c, err) = TwinConfig.Parse(Json());
            Assert.IsNull(err);
            Assert.AreEqual(1, c.mx.logicalStation);
            var (p, connect) = c.ToPollerConfig("mx", "", 1);
            Assert.AreEqual(100, p.PollMs);
            Assert.AreEqual(0, p.RewriteMs);
            CollectionAssert.AreEqual(new[] { (100, 4), (140, 7) }, p.ReadBlocks);
            Assert.IsNotNull(connect);
        }

        [Test]
        public void ConfigMxSectionAndValidation()
        {
            var (c, err) = TwinConfig.Parse(Json("\"mx\":{\"logicalStation\":5,\"pollMs\":50},"));
            Assert.IsNull(err);
            Assert.AreEqual(5, c.mx.logicalStation);
            Assert.AreEqual(50, c.ToPollerConfig("mx", "", 5).poller.PollMs);
            StringAssert.Contains("logicalStation", TwinConfig.Parse(Json("\"mx\":{\"logicalStation\":2000,\"pollMs\":50},")).error);
            StringAssert.Contains("mx.pollMs", TwinConfig.Parse(Json("\"mx\":{\"logicalStation\":1,\"pollMs\":5},")).error);
        }
    }
}
