using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using PlcTwin.EditorTools;
using PlcTwin.Twin;
using UnityEngine;

namespace PlcTwin.Tests
{
    public class TwinConfigTests
    {
        const string Help = "\"_help\":\"ignored text\",";

        // Builds a config JSON; any argument can be overridden per test.
        static string Json(string transport = "mc3e", string mcIp = "192.168.3.39", int mcPort = 5010, int monitor = 4, int mcPoll = 100,
                           string mbIp = "127.0.0.1", int mbPort = 502, int unit = 1, int mbPoll = 200, int wBase = 0, int rBase = 100,
                           int connect = 3000, int timeout = 1500,
                           string inBits = "D0", string inWords = "D40", string outBits = "D100", string outWords = "D140") =>
            "{" + Help + "\"transport\":\"" + transport + "\"," +
            "\"mc3e\":{\"ip\":\"" + mcIp + "\",\"port\":" + mcPort + ",\"monitorTimer250ms\":" + monitor + ",\"pollMs\":" + mcPoll + "}," +
            "\"modbus\":{\"ip\":\"" + mbIp + "\",\"port\":" + mbPort + ",\"unitId\":" + unit + ",\"pollMs\":" + mbPoll +
            ",\"writeTableBase\":" + wBase + ",\"readTableBase\":" + rBase + "}," +
            "\"connectTimeoutMs\":" + connect + ",\"timeoutMs\":" + timeout + "," +
            "\"map\":{\"inBits\":\"" + inBits + "\",\"inWords\":\"" + inWords + "\",\"outBits\":\"" + outBits + "\",\"outWords\":\"" + outWords + "\"}," +
            "\"panelVisible\":true}";

        static void AssertError(string json, string mustContain)
        {
            var (c, err) = TwinConfig.Parse(json);
            Assert.IsNull(c);
            StringAssert.Contains(mustContain, err);
        }

        [Test]
        public void ValidConfig()
        {
            var (c, err) = TwinConfig.Parse(Json());
            Assert.IsNull(err);
            Assert.AreEqual("mc3e", c.transport);
            Assert.AreEqual("192.168.3.39", c.mc3e.ip);
            Assert.AreEqual(5010, c.mc3e.port);
            Assert.AreEqual(502, c.modbus.port);
            Assert.AreEqual("D140", c.map.outWords);
            Assert.IsTrue(c.panelVisible);
        }

        [Test]
        public void MissingFile()
        {
            var (c, err) = TwinConfig.Load(Path.Combine(Path.GetTempPath(), "no_such_plc_twin_config.json"));
            Assert.IsNull(c);
            Assert.IsNotEmpty(err);
        }

        [Test]
        public void LoadsFromFile()
        {
            var path = Path.GetTempFileName();
            try { File.WriteAllText(path, Json()); Assert.IsNull(TwinConfig.Load(path).error); }
            finally { File.Delete(path); }
        }

        [TestCase("plc.local")]
        [TestCase("")]
        [TestCase("192.168.3")]
        [TestCase("1.2.3.4.5")]
        [TestCase("::1")]
        public void InvalidIpNamesIp(string ip)
        {
            AssertError(Json(mcIp: ip), "mc3e.ip");
            AssertError(Json(mbIp: ip), "modbus.ip");
        }

        [Test]
        public void HelpIsIgnored()
        {
            var (c, err) = TwinConfig.Parse(Json());
            Assert.IsNull(err);
            Assert.IsNotEmpty(c._help);
            var (c2, err2) = TwinConfig.Parse(Json().Replace(Help, ""));
            Assert.IsNull(err2);
            Assert.IsNull(c2._help);
        }

        [Test]
        public void TimeoutTooSmall()
        {
            AssertError(Json(timeout: 1499), "timeoutMs");
            Assert.IsNull(TwinConfig.Parse(Json(timeout: 1500)).error);
        }

        [TestCase(0)]
        [TestCase(65536)]
        public void PortOutOfRange(int port)
        {
            AssertError(Json(mcPort: port), "mc3e.port");
            AssertError(Json(mbPort: port), "modbus.port");
        }

        [TestCase(0)]
        [TestCase(9)]
        public void PollMsTooSmall(int poll)
        {
            AssertError(Json(mcPoll: poll), "mc3e.pollMs");
            AssertError(Json(mbPoll: poll), "modbus.pollMs");
        }

        [Test] public void ConnectTimeoutZero() => AssertError(Json(connect: 0), "connectTimeoutMs");

        [TestCase(0)]
        [TestCase(-1)]
        public void MonitorTimerTooSmall(int monitor) => AssertError(Json(monitor: monitor), "monitorTimer250ms");

        [TestCase(-1)]
        [TestCase(256)]
        public void UnitIdOutOfRange(int unit) => AssertError(Json(unit: unit), "modbus.unitId");

        [Test] public void DefaultsValidate() => Assert.IsNull(TwinConfig.Parse(Json()).error);

        [Test]
        public void InvalidTransport()
        {
            AssertError(Json(transport: "tcp"), "transport");
            AssertError(Json(transport: "MC3E"), "transport");
        }

        [TestCase("M5")]
        [TestCase("Y20")]
        [TestCase("D")]
        [TestCase("")]
        public void MapEntryNotD(string dev) => AssertError(Json(outWords: dev), "map.outWords");

        [Test]
        public void OldFormatFileRejected()
        {
            const string old = "{\"ip\":\"192.168.3.39\",\"port\":5010,\"connectTimeoutMs\":3000,\"timeoutMs\":1500,\"pollMs\":100,\"monitorTimer250ms\":4," +
                               "\"conveyor\":{\"run\":\"Y20\",\"speedWord\":\"D100\",\"speedScale\":0.001,\"defaultSpeed\":0.3}," +
                               "\"tower\":{\"red\":\"Y30\",\"yellow\":\"Y31\",\"green\":\"Y32\"},\"sensor\":{\"write\":\"M100\"}}";
            AssertError(old, "old config format (conveyor/tower/sensor) is no longer supported; see README.txt");
            // New shape plus a leftover old section is also rejected.
            AssertError(Json().Replace("\"panelVisible\":true", "\"sensor\":{\"write\":\"M100\"}"), "old config format");
        }

        [Test]
        public void ModbusBlockOutsideTableRejected()
        {
            AssertError(Json(inWords: "D76"), "map.inWords");      // 76+5 > 80 (write table)
            AssertError(Json(outWords: "D175"), "map.outWords");   // 175+7 > 180 (read table)
            AssertError(Json(wBase: 10), "map.inBits");            // D0 below writeTableBase 10
            Assert.IsNull(TwinConfig.Parse(Json(inWords: "D75", outWords: "D173")).error);
        }

        [Test]
        public void OverlappingBlocksRejected()
        {
            AssertError(Json(inWords: "D2"), "overlap");
            AssertError(Json(outWords: "D102"), "overlap");
            Assert.IsNull(TwinConfig.Parse(Json(inWords: "D4")).error);
        }

        [Test]
        public void WordSpanOver960Rejected()
        {
            // The span check runs before the Modbus table check, so 961 words is reported as a span error; 960 is not.
            AssertError(Json(inBits: "D2000", inWords: "D2050", outBits: "D0", outWords: "D954"), "read span");
            StringAssert.DoesNotContain("read span", TwinConfig.Parse(Json(inBits: "D2000", inWords: "D2050", outBits: "D0", outWords: "D953")).error ?? "");
        }

        [Test]
        public void ToPollerConfig_Mc3e()
        {
            var c = TwinConfig.Parse(Json(mcPoll: 123)).config;
            var (p, connect) = c.ToPollerConfig("mc3e", "10.0.0.5", 6000);
            Assert.AreEqual(123, p.PollMs);
            CollectionAssert.AreEqual(new[] { (100, 4), (140, 7) }, p.ReadBlocks);
            CollectionAssert.AreEqual(new[] { (40, 5), (0, 4) }, p.WriteBlocks);
            Assert.IsNotNull(connect);
        }

        [Test]
        public void ToPollerConfig_Modbus_UsesModbusPollAndMapBases()
        {
            var c = TwinConfig.Parse(Json(mbPoll: 250, inBits: "D8", inWords: "D50", outBits: "D110", outWords: "D150")).config;
            var (p, _) = c.ToPollerConfig("modbus", "127.0.0.1", 502);
            Assert.AreEqual(250, p.PollMs);
            CollectionAssert.AreEqual(new[] { (110, 4), (150, 7) }, p.ReadBlocks);
            CollectionAssert.AreEqual(new[] { (50, 5), (8, 4) }, p.WriteBlocks);
        }

        static void WaitConnections(FakePlcServer f, int n)
        {
            var sw = Stopwatch.StartNew(); // the fake counts in its accept thread, a moment after the client's connect returns
            while (f.Connections < n && sw.ElapsedMilliseconds < 2000) Thread.Sleep(5);
        }

        // Config endpoints point at a closed local port, so only the override can reach the fake.
        static TwinConfig Unreachable() => TwinConfig.Parse(Json(mcIp: "127.0.0.1", mcPort: 1, mbIp: "127.0.0.1", mbPort: 1, connect: 300)).config;

        [Test]
        public void ToPollerConfig_Mc3e_OverrideReachesTheFake()
        {
            using (var fake = new FakeMcServer())
            {
                var (_, connect) = TwinConfig.Parse(Json()).config.ToPollerConfig("mc3e", "127.0.0.1", fake.Port);
                using (connect(new ManualResetEvent(false))) WaitConnections(fake, 1);
                Assert.AreEqual(1, fake.Connections);
            }
        }

        [Test]
        public void ToPollerConfig_Modbus_OverrideReachesTheFakeAndReadsItsData()
        {
            using (var fake = new FakeModbusServer())
            {
                for (int i = 0; i < 4; i++) fake.SetWord(100 + i, (short)(1000 + i));
                var (_, connect) = TwinConfig.Parse(Json()).config.ToPollerConfig("modbus", "127.0.0.1", fake.Port);
                using (var t = connect(new ManualResetEvent(false)))
                {
                    CollectionAssert.AreEqual(new short[] { 1000, 1001, 1002, 1003 }, t.ReadWords(100, 4));
                    WaitConnections(fake, 1);
                }
                Assert.AreEqual(1, fake.Connections);
            }
        }

        [Test]
        public void ToPollerConfig_OverrideBeatsUnreachableConfigEndpoint()
        {
            foreach (var t in new[] { "mc3e", "modbus" })
                using (var fake = t == "modbus" ? (FakePlcServer)new FakeModbusServer() : new FakeMcServer())
                {
                    var (_, connect) = Unreachable().ToPollerConfig(t, "127.0.0.1", fake.Port);
                    using (connect(new ManualResetEvent(false))) WaitConnections(fake, 1);
                    Assert.AreEqual(1, fake.Connections, t);
                }
        }

        [Test]
        public void ToPollerConfig_UnknownTransportThrows()
        {
            var c = TwinConfig.Parse(Json()).config;
            var e = Assert.Throws<ArgumentException>(() => c.ToPollerConfig("modbux", "127.0.0.1", 502));
            StringAssert.Contains("modbux", e.Message);
        }

        [Test]
        public void NegativeTableBasesRejected()
        {
            AssertError(Json(wBase: -1), "writeTableBase");
            AssertError(Json(rBase: -1), "readTableBase");
        }

        [Test]
        public void ShippedConfigIsValid()
        {
            var (c, err) = TwinConfig.Load(Path.Combine(Application.dataPath, "StreamingAssets", "plc_twin_config.json"));
            Assert.IsNull(err);
            Assert.IsNotNull(c);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"ip\":5}")]
        [TestCase("{\"transport\":5}")]
        [TestCase("null")]
        public void GarbageNeverThrows(string json)
        {
            Assert.DoesNotThrow(() => TwinConfig.Parse(json));
            var (c, err) = TwinConfig.Parse(json);
            Assert.IsNull(c);
            Assert.IsNotEmpty(err);
        }
    }
}
