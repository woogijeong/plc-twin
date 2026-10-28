using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using PlcTwin.EditorTools;
using PlcTwin.Mc;
using Debug = UnityEngine.Debug;

namespace PlcTwin.Tests
{
    [TestFixture, Timeout(10000)]
    public class McPollerIntegrationTests : PollerIntegrationTests
    {
        FakeMcServer Mc => (FakeMcServer)fake;
        protected override FakePlcServer NewFake() => new FakeMcServer();
        protected override IPlcTransport Connect(string ip, int port, int connectTimeoutMs, WaitHandle stop) =>
            McTransport.Connect(ip, port, connectTimeoutMs, TimeoutMs, 4, stop);
        protected override void ForceError(bool on) => Mc.ForceEndCode = on ? 0xC059 : 0;
        protected override string ErrorText => "end code C059";

        [Test] public void AC4_EndCodeC059_Reported() => ErrorReplyReported();
        [Test] public void WriteRecovery_iii_EndCodeErrorThenWritesResume() => ErrorReplyThenWritesResume();
    }

    [TestFixture, Timeout(10000)]
    public class ModbusPollerIntegrationTests : PollerIntegrationTests
    {
        FakeModbusServer Mb => (FakeModbusServer)fake;
        protected override FakePlcServer NewFake() => new FakeModbusServer();
        protected override IPlcTransport Connect(string ip, int port, int connectTimeoutMs, WaitHandle stop) =>
            ModbusTransport.Connect(ip, port, connectTimeoutMs, TimeoutMs, 1, 0, 100, stop);
        protected override void ForceError(bool on) => Mb.ForceExceptionCode = on ? 4 : 0;
        protected override string ErrorText => "exception code 4";
        // The server ignores the FC16 start: every write is the D0..D44 prefix, D4..D39 sent as 0 (user-approved).
        protected override (int D, int Len)[] CycleFrames => new[] { (0, 45), (0, 45) };
        protected override bool ZeroFilled(int d) => d >= 4 && d < 40;

        [Test]
        public void ReplyWithWrongUnitId_IsAnErrorAndTheSocketIsDiscarded()
        {
            Mb.ReplyUnitOverride = 2;
            using (var t = ModbusTransport.Connect("127.0.0.1", fake.Port, ConnectTimeoutMs, TimeoutMs, 1, 0, 100, new ManualResetEvent(false)))
            {
                var e = Assert.Throws<ModbusException>(() => t.ReadWords(100, 4));
                StringAssert.Contains("unit id", e.Message);
                Assert.Throws<ObjectDisposedException>(() => t.ReadWords(100, 4)); // socket closed for good
            }
        }

        [Test] public void AC4_ExceptionReply_Reported() => ErrorReplyReported();
        [Test] public void WriteRecovery_iii_ExceptionReplyThenWritesResume() => ErrorReplyThenWritesResume();

        // The real server takes about 310 ms per request (probe), so one cycle (2 writes + 1 read) is about 0.9 s.
        [Test]
        public void RealServerLatency300ms_ConnectedDataAndStopUnder1s()
        {
            fake.ReplyDelayMs = 300;
            fake.SetWord(100, 123);
            fake.SetWord(146, -2);
            NewPoller(pollMs: 200, set: false);
            poller.SetWords((40, new short[] { 1, 2, 3, 4, 5 }), (0, new short[] { 6, 7, 8, 9 }));
            var sw = Stopwatch.StartNew();
            poller.Start();
            WaitState(PlcState.Connected, 3000);
            Debug.Log("Modbus 300 ms latency: Connected after " + sw.ElapsedMilliseconds + " ms");
            Assert.AreEqual(123, poller.GetWord(100));
            Assert.AreEqual(-2, poller.GetWord(146));
            Assert.AreEqual(1, fake.GetWord(40));
            Assert.AreEqual(5, fake.GetWord(44));
            Assert.AreEqual(6, fake.GetWord(0));
            Assert.AreEqual(9, fake.GetWord(3));
            Thread.Sleep(150); // mid-request
            AssertStopFast("300 ms reply latency");
        }
    }

    /// <summary>Same bodies for both transports (plan Step 2 disposition table). Every wait is bounded; TearDown stops all.</summary>
    public abstract class PollerIntegrationTests
    {
        protected const int ConnectTimeoutMs = 500;
        protected const int TimeoutMs = 1500; // >= MonitorTimer250ms(4)*250+500
        const int PollMs = 50;
        static readonly short[] Z4 = new short[4], Z5 = new short[5];

        protected FakePlcServer fake;
        protected PlcPoller poller;

        protected abstract FakePlcServer NewFake();
        protected abstract IPlcTransport Connect(string ip, int port, int connectTimeoutMs, WaitHandle stop);
        protected abstract void ForceError(bool on);
        protected abstract string ErrorText { get; }

        /// <summary>Write frames of one full cycle as the fake logs them, in send order: (start D, word count). MC: one per block.</summary>
        protected virtual (int D, int Len)[] CycleFrames => new[] { (40, 5), (0, 4) };
        /// <summary>Words outside the owned blocks that the transport writes as 0 (Modbus only).</summary>
        protected virtual bool ZeroFilled(int d) => false;

        static bool Covers(FakeWrite w, int d) => d >= w.D && d < w.D + w.Words.Length;
        static short At(FakeWrite w, int d) => w.Words[d - w.D];

        void AssertFrame(FakeWrite w, int i)
        {
            Assert.AreEqual(CycleFrames[i % 2], (w.D, w.Words.Length), "frame " + i + " start/length");
            for (int d = w.D; d < w.D + w.Words.Length; d++)
                if (ZeroFilled(d)) Assert.AreEqual(0, At(w, d), "frame " + i + ": zero-filled D" + d);
        }

        [SetUp] public void SetUp() { fake = NewFake(); }

        [TearDown]
        public void TearDown()
        {
            poller?.Stop();
            poller = null;
            fake?.Stop();
            fake = null;
        }

        /// <summary>set: both write blocks are set to zero (so Start does not throw).</summary>
        protected PlcPoller NewPoller(int pollMs = PollMs, string ip = "127.0.0.1", int port = -1, int connectTimeoutMs = ConnectTimeoutMs, bool set = true)
        {
            poller?.Stop();
            int p = port < 0 ? fake.Port : port;
            poller = new PlcPoller(new PlcPollerConfig { PollMs = pollMs }, stop => Connect(ip, p, connectTimeoutMs, stop));
            if (set) poller.SetWords((40, Z5), (0, Z4));
            return poller;
        }

        // Consistent pair: D40 = n and D3 = n, in one batch call.
        void SetPair(short n) => poller.SetWords((40, new short[] { n, 0, 0, 0, 0 }), (0, new short[] { 0, 0, 0, n }));
        bool FakeHasPair(short n) => fake.GetWord(40) == n && fake.GetWord(3) == n;

        protected static bool WaitUntil(Func<bool> cond, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(1);
            }
            return cond();
        }

        void StartConnected()
        {
            poller.Start();
            Assert.IsTrue(WaitUntil(() => poller.State == PlcState.Connected, 3000), "not Connected: " + poller.State + " " + poller.LastError);
        }

        protected void WaitState(PlcState s, int ms) =>
            Assert.IsTrue(WaitUntil(() => poller.State == s, ms), "expected " + s + " within " + ms + " ms, got " + poller.State + " " + poller.LastError);

        // ---- Reads ----

        [Test]
        public void D100VisibleInSnapshotWithin500ms_10Runs()
        {
            NewPoller(pollMs: 100);
            StartConnected();
            long max = 0;
            for (int run = 0; run < 10; run++)
            {
                fake.SetWord(100, 0);
                Assert.IsTrue(WaitUntil(() => poller.GetWord(100) == 0, 1000), "reset run " + run);
                var sw = Stopwatch.StartNew();
                fake.SetWord(100, 1);
                Assert.IsTrue(WaitUntil(() => poller.GetWord(100) == 1, 500), "run " + run);
                max = Math.Max(max, sw.ElapsedMilliseconds);
            }
            Debug.Log("D100 max latency over 10 runs: " + max + " ms");
            Assert.Less(max, 500);
        }

        [Test]
        public void D100ReadBackSigned()
        {
            NewPoller(pollMs: 100);
            fake.SetWord(100, -5);
            fake.SetWord(146, short.MinValue);
            StartConnected();
            Assert.AreEqual(-5, poller.GetWord(100));
            Assert.AreEqual(short.MinValue, poller.GetWord(146));
            fake.SetWord(100, 1000);
            Assert.IsTrue(WaitUntil(() => poller.GetWord(100) == 1000, 500), "got " + poller.GetWord(100));
        }

        [Test]
        public void ReadBeforeData_ZeroAndSnapshotTimeNull()
        {
            fake.SetWord(100, 7);
            fake.SetWord(147, 9); // outside the read span D100..D146
            NewPoller();
            Assert.IsNull(poller.SnapshotTime);
            Assert.AreEqual(0, poller.GetWord(100));

            fake.Silent = true; // connected socket, but no write ever completes: still no data
            poller.Start();
            Thread.Sleep(300);
            Assert.AreNotEqual(PlcState.Connected, poller.State);
            Assert.IsNull(poller.SnapshotTime);
            Assert.AreEqual(0, poller.GetWord(100));

            fake.Silent = false;
            NewPoller();
            StartConnected();
            Assert.IsNotNull(poller.SnapshotTime);
            Assert.AreEqual(7, poller.GetWord(100));
            Assert.AreEqual(0, poller.GetWord(147));
            Assert.AreEqual(0, poller.GetWord(200));
        }

        // ---- Writes ----

        [Test]
        public void SetWords_LandsWithin300ms()
        {
            NewPoller(pollMs: 100);
            StartConnected();
            var sw = Stopwatch.StartNew();
            poller.SetWords((40, new short[] { 1, 2, 3, 4, -5 }), (0, new short[] { -1, 2, 3, 4 }));
            Assert.IsTrue(WaitUntil(() => fake.GetWord(40) == 1 && fake.GetWord(44) == -5 && fake.GetWord(0) == -1 && fake.GetWord(3) == 4, 300));
            Debug.Log("SetWords write latency: " + sw.ElapsedMilliseconds + " ms");
        }

        [Test]
        public void SetWords_RejectsNonOwnedStartOrWrongLength()
        {
            NewPoller();
            Assert.Throws<ArgumentException>(() => poller.SetWords((100, Z4)));
            Assert.Throws<ArgumentException>(() => poller.SetWords((41, Z4)));
            Assert.Throws<ArgumentException>(() => poller.SetWords((40, Z4)));
            Assert.Throws<ArgumentException>(() => poller.SetWords((0, Z5)));
        }

        [Test]
        public void FirstCycle_OverwritesStaleFakeWords()
        {
            fake.SetWord(0, 0x7777); // stale values from a crashed run
            fake.SetWord(44, 0x7777);
            NewPoller();
            StartConnected(); // Connected only after the first full write + read
            Assert.AreEqual(0, fake.GetWord(0));
            Assert.AreEqual(0, fake.GetWord(44));
        }

        [Test]
        public void ChangeDuringInFlightWrite_LandsNextCycle()
        {
            NewPoller();
            StartConnected();
            const int delay = 200;
            fake.WriteDelayMs = delay;
            SetPair(1);
            Assert.IsTrue(fake.WriteStarted.WaitOne(1000), "delayed write never started");
            SetPair(2);
            var sw = Stopwatch.StartNew();
            int bound = delay + PollMs + 300;
            Assert.IsTrue(WaitUntil(() => FakeHasPair(2), bound), "pair 2 not written within " + bound + " ms");
            Debug.Log("in-flight change landed after " + sw.ElapsedMilliseconds + " ms");
            Thread.Sleep(PollMs * 4);
            Assert.IsTrue(FakeHasPair(2), "later cycle overwrote with an older value");
        }

        [Test]
        public void Start_UnsetWriteBlock_ThrowsAndNoWrites()
        {
            NewPoller(set: false);
            StringAssert.Contains("D40", Assert.Throws<InvalidOperationException>(() => poller.Start()).Message);
            poller.SetWords((40, Z5));
            StringAssert.Contains("D0", Assert.Throws<InvalidOperationException>(() => poller.Start()).Message);
            Thread.Sleep(300);
            Assert.AreEqual(PlcState.Disconnected, poller.State);
            Assert.AreEqual(0, fake.Connections);
            Assert.AreEqual(0, fake.WriteLog().Length);
        }

        [Test]
        public void Connected_OnlyAfterFirstWriteAndRead()
        {
            fake.WriteDelayMs = 400; // holds the first write frame
            NewPoller();
            poller.Start();
            Assert.IsTrue(fake.WriteStarted.WaitOne(2000), "first write never arrived");
            Thread.Sleep(200);
            Assert.AreEqual(PlcState.Connecting, poller.State);
            Assert.IsNull(poller.SnapshotTime);
            Assert.IsTrue(WaitUntil(() => poller.State == PlcState.Connected, 3000), "not Connected: " + poller.LastError);
            var log = fake.WriteLog();
            Assert.GreaterOrEqual(log.Length, 2);
            AssertFrame(log[0], 0); // the D40 block's frame
            AssertFrame(log[1], 1); // the D0 block's frame
            Assert.IsNotNull(poller.SnapshotTime);
        }

        [Test]
        public void WriteOrder_D40BeforeD0_EveryCycle()
        {
            NewPoller(pollMs: 20);
            StartConnected();
            Assert.IsTrue(WaitUntil(() => fake.WriteLog().Length >= 40, 5000));
            var log = fake.WriteLog();
            for (int i = 0; i < log.Length; i++) AssertFrame(log[i], i);
        }

        [Test]
        public void RewriteMs_SkipsUnchangedBlocks_SendsChangesAtOnce_ResendsAfterInterval()
        {
            poller = new PlcPoller(new PlcPollerConfig { PollMs = 20, RewriteMs = 600 }, stop => Connect("127.0.0.1", fake.Port, ConnectTimeoutMs, stop));
            poller.SetWords((40, Z5), (0, Z4));
            StartConnected();
            Thread.Sleep(350); // ~17 cycles, all unchanged
            Assert.AreEqual(2, fake.WriteLog().Length, "first cycle writes both blocks, then nothing until RewriteMs");
            poller.SetWords((0, new short[] { 7, 0, 0, 0 }));
            Assert.IsTrue(WaitUntil(() => fake.GetWord(0) == 7, 300), "a changed block is sent next cycle");
            Assert.AreEqual(3, fake.WriteLog().Length, "only the changed block was sent");
            Assert.IsTrue(WaitUntil(() => fake.WriteLog().Length >= 5, 1500), "unchanged blocks are resent after RewriteMs");
        }

        [Test]
        public void WriteSnapshot_AtomicPerCycle()
        {
            // Every D40-block frame is held 30 ms while the writer keeps changing the pair (Modbus: every frame starts at D0, so both are held).
            fake.WriteDelayMsFor(CycleFrames[0].D, 30);
            NewPoller(pollMs: 10);
            StartConnected();
            int n = 0;
            bool stop = false;
            var writer = new Thread(() =>
            {
                while (!Volatile.Read(ref stop)) { SetPair((short)Interlocked.Increment(ref n)); Thread.SpinWait(5000); }
            }) { IsBackground = true };
            writer.Start();
            Thread.Sleep(1500);
            Volatile.Write(ref stop, true);
            writer.Join(1000);

            var log = fake.WriteLog();
            int cycles = 0;
            var seen = new System.Collections.Generic.HashSet<short>();
            for (int i = 0; i + 1 < log.Length; i += 2)
            {
                AssertFrame(log[i], i);
                AssertFrame(log[i + 1], i + 1);
                Assert.AreEqual(At(log[i], 40), At(log[i + 1], 3), "cycle " + cycles + ": D3 in the D0 frame differs from D40 of the same cycle");
                if (Covers(log[i + 1], 40)) // Modbus: the D0-block frame carries D40 of the same cycle too
                    Assert.AreEqual(At(log[i], 40), At(log[i + 1], 40), "cycle " + cycles + ": D40 changed between the two frames of one cycle");
                seen.Add(At(log[i], 40));
                cycles++;
            }
            Debug.Log("atomic capture: " + cycles + " cycles, " + seen.Count + " distinct pairs, writer reached n=" + n);
            Assert.GreaterOrEqual(cycles, 10);
            Assert.GreaterOrEqual(seen.Count, 5, "writer did not change the pair across cycles");
            Assert.Greater(n, cycles * 2, "writer did not change the pair during in-flight cycles");
        }

        [Test]
        public void TableCleared_RewrittenWithin2Cycles_NoReconnect()
        {
            NewPoller(pollMs: 100, set: false);
            poller.SetWords((40, new short[] { 9, 9, 9, 9, 9 }), (0, new short[] { 9, 9, 9, 9 }));
            StartConnected();
            int conns = fake.Connections;
            fake.ClearMemory();
            var sw = Stopwatch.StartNew();
            int bound = 2 * 100 + 300;
            Assert.IsTrue(WaitUntil(() => fake.GetWord(40) == 9 && fake.GetWord(44) == 9 && fake.GetWord(0) == 9 && fake.GetWord(3) == 9, bound),
                "not rewritten within " + bound + " ms");
            Debug.Log("table clear: rewritten after " + sw.ElapsedMilliseconds + " ms");
            Assert.AreEqual(conns, fake.Connections, "rewrite needed a reconnect");
            Assert.AreEqual(PlcState.Connected, poller.State);
        }

        static bool IsSentinelWord(int d) => (d >= 4 && d < 40) || (d >= 45 && d < 80) || (d >= 100 && d < 180);

        [Test]
        public void Sentinel_OutsideOwnedBlocks_UnchangedAfter50Cycles()
        {
            for (int d = 0; d < 180; d++) if (IsSentinelWord(d)) fake.SetWord(d, 0x5A5A);
            NewPoller(pollMs: 20);
            StartConnected();
            Assert.IsTrue(WaitUntil(() => fake.WriteLog().Length >= 100, 8000), "fewer than 50 cycles");
            for (int d = 0; d < 180; d++)
                if (ZeroFilled(d)) Assert.AreEqual(0, fake.GetWord(d), "D" + d + " must be zero-filled (Modbus)");
                else if (IsSentinelWord(d)) Assert.AreEqual(0x5A5A, fake.GetWord(d), "D" + d);
            Assert.AreEqual(0x5A5A, poller.GetWord(100));
        }

        [Test]
        public void NoWritesToReadBlocks()
        {
            NewPoller(pollMs: 20);
            StartConnected();
            Assert.IsTrue(WaitUntil(() => fake.WriteLog().Length >= 40, 5000));
            foreach (var w in fake.WriteLog())
                Assert.IsFalse(w.D < 180 && w.D + w.Words.Length > 100, "write to D" + w.D + "x" + w.Words.Length);
            Assert.AreEqual(0, fake.WriteCount(100));
            Assert.AreEqual(0, fake.WriteCount(140));
            Assert.Throws<ArgumentException>(() => poller.SetWords((100, Z4)));
        }

        [Test]
        public void Reconnect_NoStaleSnapshot()
        {
            fake.SetWord(100, 5);
            NewPoller();
            StartConnected();
            Assert.AreEqual(5, poller.GetWord(100));
            fake.Stop();
            fake.SetWord(100, 7);
            Assert.IsTrue(WaitUntil(() => poller.SnapshotTime == null && poller.GetWord(100) == 0, 2000), "snapshot not cleared between connections");
            fake.Restart();
            int connectedObservations = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3500 && connectedObservations < 200)
            {
                if (poller.State == PlcState.Connected)
                {
                    Assert.AreEqual(7, poller.GetWord(100), "stale value while Connected");
                    Assert.IsNotNull(poller.SnapshotTime);
                    connectedObservations++;
                }
                Thread.Sleep(1);
            }
            Assert.Greater(connectedObservations, 0, "never reconnected: " + poller.LastError);
        }

        // ---- Write recovery ----

        [Test]
        public void WriteRecovery_i_SilentThenRestore()
        {
            NewPoller();
            StartConnected();
            fake.Silent = true;
            SetPair(5);
            WaitState(PlcState.Error, TimeoutMs + PollMs + 500);
            Assert.IsFalse(FakeHasPair(5));
            fake.Silent = false;
            WaitState(PlcState.Connected, 3000);
            Assert.IsTrue(FakeHasPair(5), "pair not written before Connected");
        }

        [Test]
        public void WriteRecovery_ii_ClearMemoryAndRestart_ReAsserts()
        {
            NewPoller();
            SetPair(5);
            StartConnected();
            Assert.IsTrue(FakeHasPair(5), "connect-time write must land before Connected");
            fake.Stop();
            WaitState(PlcState.Error, 2000);
            fake.ClearMemory();
            Assert.IsFalse(FakeHasPair(5));
            fake.Restart();
            WaitState(PlcState.Connected, 3000);
            Assert.IsTrue(FakeHasPair(5), "pair not re-asserted before Connected");
        }

        protected void ErrorReplyThenWritesResume()
        {
            NewPoller();
            StartConnected();
            ForceError(true);
            WaitState(PlcState.Error, 1000);
            ForceError(false);
            WaitState(PlcState.Connected, 3000);
            SetPair(3);
            Assert.IsTrue(WaitUntil(() => FakeHasPair(3), 300));
        }

        protected void ErrorReplyReported()
        {
            ForceError(true);
            NewPoller();
            poller.Start();
            WaitState(PlcState.Error, 1000);
            StringAssert.Contains(ErrorText, poller.LastError);
        }

        // ---- AC8 (rev 5 AC4) ----

        [Test]
        public void AC4_UnreachableHost_ErrorWithinConnectTimeout()
        {
            NewPoller(ip: "192.0.2.1", port: 5010);
            var sw = Stopwatch.StartNew();
            poller.Start();
            WaitState(PlcState.Error, ConnectTimeoutMs + 500);
            Debug.Log("AC4 unreachable: Error after " + sw.ElapsedMilliseconds + " ms: " + poller.LastError);
        }

        [Test]
        public void AC4_ClosedPort_ErrorNeverConnected()
        {
            int port = fake.Port;
            fake.Stop();
            NewPoller(port: port);
            bool seenConnected = false;
            var sw = Stopwatch.StartNew();
            poller.Start();
            bool err = WaitUntil(() =>
            {
                var s = poller.State;
                if (s == PlcState.Connected) seenConnected = true;
                return s == PlcState.Error;
            }, ConnectTimeoutMs + 500);
            Debug.Log("AC4 closed port: " + poller.State + " after " + sw.ElapsedMilliseconds + " ms: " + poller.LastError);
            Assert.IsTrue(err, "no Error within bound: " + poller.State);
            Assert.IsFalse(seenConnected);
            Assert.IsFalse(WaitUntil(() => poller.State == PlcState.Connected, 500), "closed port reported Connected");
        }

        // Windows retries SYN after RST for ~2 s, so with the 500 ms test timeout the closed port above ends in
        // "connect timeout". This variant gives the refusal time to arrive, exercising the EndConnect SocketException path.
        [Test]
        public void AC4_ClosedPort_RefusedViaEndConnect_NeverConnected()
        {
            int port = fake.Port;
            fake.Stop();
            NewPoller(port: port, connectTimeoutMs: 3000);
            bool seenConnected = false;
            var sw = Stopwatch.StartNew();
            poller.Start();
            bool err = WaitUntil(() =>
            {
                var s = poller.State;
                if (s == PlcState.Connected) seenConnected = true;
                return s == PlcState.Error;
            }, 3000 + 500);
            Debug.Log("AC4 closed port (3000 ms connect timeout): " + poller.State + " after " + sw.ElapsedMilliseconds + " ms: " + poller.LastError);
            Assert.IsTrue(err, "no Error within bound: " + poller.State);
            Assert.IsFalse(seenConnected);
            Assert.AreNotEqual("connect timeout", poller.LastError, "refusal should surface from EndConnect");
        }

        [Test]
        public void AC4_Silent_ErrorTimeout()
        {
            fake.Silent = true;
            NewPoller();
            var sw = Stopwatch.StartNew();
            poller.Start();
            WaitState(PlcState.Error, TimeoutMs + 500);
            Debug.Log("AC4 silent: Error after " + sw.ElapsedMilliseconds + " ms: " + poller.LastError);
            StringAssert.Contains("timeout", poller.LastError);
        }

        [Test]
        public void AC4_ServerRestart_ConnectedWithin3s()
        {
            NewPoller();
            StartConnected();
            fake.Stop();
            WaitState(PlcState.Error, 2000);
            var sw = Stopwatch.StartNew();
            fake.Restart();
            WaitState(PlcState.Connected, 3000);
            Debug.Log("AC4 restart: Connected " + sw.ElapsedMilliseconds + " ms after restart");
        }

        // ---- Stop ----

        protected void AssertStopFast(string phase)
        {
            Debug.Log("Stop latency (" + phase + "): state at Stop = " + poller.State + " lastError=" + poller.LastError);
            var sw = Stopwatch.StartNew();
            poller.Stop();
            long ms = sw.ElapsedMilliseconds;
            Debug.Log("Stop latency (" + phase + "): " + ms + " ms");
            Assert.Less(ms, 1000);
            Assert.AreEqual(PlcState.Disconnected, poller.State, "poll thread did not exit");
        }

        [Test]
        public void Stop_DuringConnect_Under1s()
        {
            NewPoller(ip: "192.0.2.1", port: 5010, connectTimeoutMs: 3000);
            poller.Start();
            Thread.Sleep(300);
            if (poller.State != PlcState.Connecting)
                Debug.Log("Stop latency (connect): host reported failure at once (" + poller.LastError + "); case degrades to backoff");
            else
                Assert.AreEqual(PlcState.Connecting, poller.State);
            AssertStopFast("connect");
        }

        [Test]
        public void Stop_DuringSilentFirstWrite_Under1s()
        {
            fake.Silent = true; // silent from the first frame: the first write blocks in receive
            NewPoller();
            poller.Start();
            Assert.IsFalse(WaitUntil(() => poller.State == PlcState.Connected, 300), "Connected published before the first write + read");
            Assert.AreEqual(PlcState.Connecting, poller.State, "should still be blocked in the first write");
            AssertStopFast("silent first write");
        }

        [Test]
        public void Stop_DuringBackoff_Under1s()
        {
            ForceError(true);
            NewPoller();
            poller.Start();
            WaitState(PlcState.Error, 1000);
            Thread.Sleep(200);
            Assert.AreEqual(PlcState.Error, poller.State);
            AssertStopFast("backoff");
        }

        [Test]
        public void StartAfterStop_Throws_StartTwiceIsFine()
        {
            NewPoller();
            poller.Start();
            Assert.DoesNotThrow(() => poller.Start());
            poller.Stop();
            var e = Assert.Throws<InvalidOperationException>(() => poller.Start());
            StringAssert.Contains("single-use", e.Message);
        }

        [Test]
        public void Stop_TwiceAndBeforeStart_NoThrow()
        {
            NewPoller();
            Assert.DoesNotThrow(() => poller.Stop()); // never started
            NewPoller();
            StartConnected();
            Assert.DoesNotThrow(() => poller.Stop());
            Assert.DoesNotThrow(() => poller.Stop());
        }
    }
}
