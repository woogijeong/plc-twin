using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using PlcTwin.EditorTools;
using PlcTwin.Mc;
using PlcTwin.Sim;
using PlcTwin.Twin;
using Debug = UnityEngine.Debug;

namespace PlcTwin.Tests
{
    [TestFixture, Timeout(15000)]
    public class McLineIntegrationTests : LineIntegrationTests
    {
        protected override string Transport => "mc3e";
        protected override FakePlcServer NewFake() => new FakeMcServer();
    }

    [TestFixture, Timeout(15000)]
    public class ModbusLineIntegrationTests : LineIntegrationTests
    {
        protected override string Transport => "modbus";
        protected override FakePlcServer NewFake() => new FakeModbusServer();
    }

    /// <summary>
    /// AC7 end to end, real time: MpsModel + PlcPoller (built by the shipped config's ToPollerConfig) + fake server, glued by
    /// TwinTick exactly as PlcTwinApp.Update will. Same body for both transports.
    /// </summary>
    public abstract class LineIntegrationTests
    {
        const int PollMs = 50; // test speed: 50 cycles fit the 15 s timeout (shipped: 100 MC, 200 Modbus)

        protected abstract string Transport { get; }
        protected abstract FakePlcServer NewFake();

        FakePlcServer fake;
        PlcPoller poller;
        MpsModel model;
        Stopwatch clock;
        double last;

        [SetUp] public void SetUp() { fake = NewFake(); }

        [TearDown]
        public void TearDown()
        {
            poller?.Stop();
            poller = null;
            fake?.Stop();
            fake = null;
        }

        void Pump()
        {
            double now = clock.Elapsed.TotalSeconds;
            TwinTick.Tick(model, poller, now - last);
            last = now;
        }

        bool PumpUntil(Func<bool> cond, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Pump();
                if (cond()) return true;
                Thread.Sleep(5);
            }
            return cond();
        }

        // Words nobody owns: neither our write blocks (D0-D3, D40-D44) nor the PLC-owned read blocks (D100-D103, D140-D146).
        static bool Sentinel(int d) => (d >= 4 && d < 40) || (d >= 45 && d < 80) || (d >= 104 && d < 140) || (d >= 147 && d < 180);
        bool ZeroFilled(int d) => Transport == "modbus" && d >= 4 && d < 40; // user-approved Modbus deviation (server ignores FC16 start)

        [Test]
        public void AC7_EndToEnd_SupplyServoWriteOrderSentinel()
        {
            var (cfg, err) = TwinConfig.Load(Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "plc_twin_config.json"));
            Assert.IsNull(err, err);
            var (pc, connect) = cfg.ToPollerConfig(Transport, "127.0.0.1", fake.Port);
            pc.PollMs = PollMs;
            for (int d = 0; d < 180; d++) if (Sentinel(d)) fake.SetWord(d, 0x5A5A);

            model = new MpsModel();
            poller = new PlcPoller(pc, connect);
            clock = Stopwatch.StartNew();
            TwinTick.Tick(model, poller, 0); // composes and sets both write blocks before Start
            Assert.IsFalse(model.Online);
            poller.Start();
            Assert.IsTrue(PumpUntil(() =>
            {
                // Connected is never left in this phase, so a state seen after a pump held during it.
                if (poller.State != PlcState.Connected) Assert.IsFalse(model.Online, "model online while " + poller.State);
                return poller.State == PlcState.Connected;
            }, 3000), "not Connected: " + poller.LastError);
            long connectedAt = clock.ElapsedMilliseconds;

            var sw = Stopwatch.StartNew();
            fake.SetWord(100, 1); // D100.0 supply forward
            int bound = 1700 + 2 * PollMs + 300;
            Assert.IsTrue(PumpUntil(() => (fake.GetWord(0) & 3) == 1, bound), "fake D0.0/D0.1 not 1/0 within " + bound + " ms");
            Debug.Log(Transport + ": D0.0 after " + sw.ElapsedMilliseconds + " ms");
            Assert.IsTrue(model.Online);

            sw.Restart();
            fake.SetWord(140, 10);
            fake.SetWord(142, 9003);
            fake.SetWord(144, 100);
            fake.SetWord(103, 1); // D103.0 Run
            bound = 1040 + 2 * PollMs + 300;
            Assert.IsTrue(PumpUntil(() => fake.GetWord(40) == 100, bound), "fake D40 = " + fake.GetWord(40) + ", not 100 within " + bound + " ms");
            Debug.Log(Transport + ": D40 = 100 after " + sw.ElapsedMilliseconds + " ms");
            Assert.IsTrue(PumpUntil(() => (fake.GetWord(3) & 1) == 1, 2 * PollMs + 300), "D3.0");

            PumpUntil(() => clock.ElapsedMilliseconds - connectedAt >= 50 * PollMs, 5000); // at least 50 poll cycles

            // Link loss while the servo moves: it stops in place (AC6 through the real glue).
            fake.SetWord(144, 0);
            Assert.IsTrue(PumpUntil(() => model.D40 < 95, 2000), "servo did not start toward D144 = 0");
            fake.Stop();
            Assert.IsTrue(PumpUntil(() => !model.Online, 3000), "model still online after the server stopped: " + poller.State);
            short d40 = model.D40;
            PumpUntil(() => false, 1000);
            Assert.AreEqual(d40, model.D40, "servo moved after link loss");
            Assert.Greater(d40, 0);
            poller.Stop();

            var log = fake.WriteLog();
            Assert.Greater(log.Length, 2);
            var mem = new short[180];
            int doneFrames = 0; // rising edges of D3.0 in the fake
            bool wasDone = false;
            for (int i = 0; i < log.Length; i++)
            {
                var w = log[i];
                Assert.LessOrEqual(w.D + w.Words.Length, 100, "frame " + i + " writes into D100-D179");
                if (Transport == "mc3e")
                    Assert.AreEqual(i % 2 == 0 ? (40, 5) : (0, 4), (w.D, w.Words.Length), "frame " + i + ": D40 block must precede the D0 block");
                else
                    Assert.AreEqual((0, 45), (w.D, w.Words.Length), "frame " + i + ": Modbus frames are the D0..D44 prefix");
                Array.Copy(w.Words, 0, mem, w.D, w.Words.Length);
                // The done bit must never come ON before its D40. (Its falling edge lags D40 by one frame on both transports:
                // the D40 block is written first, and on Modbus that frame re-sends the previous D0-D3 from the shadow.)
                bool done = (mem[3] & 1) != 0;
                if (done && !wasDone)
                {
                    doneFrames++;
                    Assert.AreEqual(100, mem[40], "frame " + i + " turned D3.0 ON before D40 = D144 reached the fake");
                }
                wasDone = done;
            }
            Assert.Greater(doneFrames, 0, "no frame carried D3.0 = 1");

            for (int d = 0; d < 180; d++)
                if (ZeroFilled(d)) Assert.AreEqual(0, fake.GetWord(d), "D" + d + " (Modbus zero-fill)");
                else if (Sentinel(d)) Assert.AreEqual(0x5A5A, fake.GetWord(d), "sentinel D" + d);
        }
    }
}
