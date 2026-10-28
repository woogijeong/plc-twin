using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: InternalsVisibleTo("PlcTwin.Tests")]

namespace PlcTwin.Mc
{
    public enum PlcState { Disconnected, Connecting, Connected, Error }

    /// <summary>
    /// Poller settings. Plain C#, no Unity types. Blocks are (D start, word count). Defaults are the reference map.
    /// WriteBlocks are written in list order every cycle (D40 block before D0 block). Read blocks are read as one span.
    /// </summary>
    public sealed class PlcPollerConfig
    {
        public int PollMs = 100;
        /// <summary>0 = write every block every cycle. >0 = skip a block whose words are unchanged until this many ms since its last write.</summary>
        public int RewriteMs = 0;
        public List<(int d, int count)> ReadBlocks = new List<(int d, int count)> { (100, 4), (140, 7) };
        public List<(int d, int count)> WriteBlocks = new List<(int d, int count)> { (40, 5), (0, 4) };
    }

    /// <summary>
    /// One background thread: connect (via the transport factory), then every cycle: copy all write blocks under one
    /// lock, write them in list order, read the read span, publish the snapshot, wait PollMs - elapsed.
    /// The snapshot is cleared on every new connection and on every error; Connected is published only after the first
    /// full write + read on a connection. Any error: state Error, transport discarded, 2 s backoff, reconnect.
    /// </summary>
    public sealed class PlcPoller
    {
        readonly PlcPollerConfig cfg;
        readonly Func<WaitHandle, IPlcTransport> connect;
        readonly short[][] desired; // per write block; null = never set. Also the lock for desired state.
        readonly int readFirst, readCount;
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        readonly object sync = new object(); // state, error, snapshot, transport
        Thread thread;
        IPlcTransport transport;
        short[] snapshot; // immutable once published; covers readFirst .. readFirst+readCount-1
        PlcState state = PlcState.Disconnected;
        string lastError;
        int rttMs;
        DateTime? snapshotTime;

        /// <param name="connect">Opens a transport; must return early (throw) when the stop handle is set.</param>
        public PlcPoller(PlcPollerConfig config, Func<WaitHandle, IPlcTransport> connect)
        {
            cfg = config ?? throw new ArgumentNullException(nameof(config));
            this.connect = connect ?? throw new ArgumentNullException(nameof(connect));
            if (cfg.ReadBlocks.Count == 0 || cfg.WriteBlocks.Count == 0) throw new ArgumentException("ReadBlocks and WriteBlocks must not be empty");
            desired = new short[cfg.WriteBlocks.Count][];
            readFirst = cfg.ReadBlocks.Min(b => b.d);
            readCount = cfg.ReadBlocks.Max(b => b.d + b.count) - readFirst;
        }

        public PlcPollerConfig Config => cfg;
        public PlcState State { get { lock (sync) return state; } }
        public string LastError { get { lock (sync) return lastError; } }
        public int RttMs { get { lock (sync) return rttMs; } }
        /// <summary>UTC time of the last published snapshot; null = no data on the current connection.</summary>
        public DateTime? SnapshotTime { get { lock (sync) return snapshotTime; } }

        /// <summary>Throws InvalidOperationException naming the first owned write block that was never set.</summary>
        public void Start()
        {
            lock (desired)
                for (int i = 0; i < desired.Length; i++)
                    if (desired[i] == null)
                        throw new InvalidOperationException("write block D" + cfg.WriteBlocks[i].d + " was never set (call SetWords before Start)");
            lock (sync)
            {
                if (stopEvent.WaitOne(0)) throw new InvalidOperationException("poller is single-use");
                if (thread != null) return;
                state = PlcState.Connecting;
                thread = new Thread(Run) { IsBackground = true, Name = "PlcPoller" };
            }
            thread.Start();
        }

        /// <summary>Idempotent. Sets the stop event, closes the transport (unblocks a receive), joins up to 900 ms.</summary>
        public void Stop()
        {
            stopEvent.Set();
            Thread t;
            lock (sync) { transport?.Dispose(); t = thread; }
            t?.Join(900);
        }

        /// <summary>
        /// Replaces the desired values of the given whole write blocks under one lock (all or none). Each d must be an owned
        /// write block start and each array exactly that block's length. Callable before Start(). Lands in the next cycle.
        /// </summary>
        public void SetWords(params (int d, short[] v)[] blocks)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            var idx = new int[blocks.Length];
            for (int k = 0; k < blocks.Length; k++)
            {
                var (d, v) = blocks[k];
                idx[k] = cfg.WriteBlocks.FindIndex(b => b.d == d);
                if (idx[k] < 0) throw new ArgumentException("D" + d + " is not the start of an owned write block");
                if (v == null || v.Length != cfg.WriteBlocks[idx[k]].count)
                    throw new ArgumentException("write block D" + d + " needs " + cfg.WriteBlocks[idx[k]].count + " words, got " + (v?.Length ?? 0));
            }
            lock (desired)
                for (int k = 0; k < blocks.Length; k++) desired[idx[k]] = (short[])blocks[k].v.Clone();
        }

        /// <summary>The latest published read span (never mutated; starts at the lowest ReadBlocks d); null = no data.</summary>
        public short[] Snapshot() { lock (sync) return snapshot; }

        /// <summary>Latest polled value (signed); 0 outside the read span and while SnapshotTime is null.</summary>
        public short GetWord(int d)
        {
            short[] s;
            lock (sync) s = snapshot;
            int i = d - readFirst;
            return s != null && i >= 0 && i < s.Length ? s[i] : (short)0;
        }

        void Run()
        {
            while (!stopEvent.WaitOne(0))
            {
                try
                {
                    SetState(PlcState.Connecting, null);
                    var t = connect(stopEvent);
                    lock (sync) transport = t;
                    if (stopEvent.WaitOne(0)) break; // Stop() may have run before the transport was visible

                    bool first = true;
                    var lastSent = new short[cfg.WriteBlocks.Count][]; // per connection: nothing sent yet
                    var lastSentAt = new long[cfg.WriteBlocks.Count];
                    var clock = Stopwatch.StartNew();
                    while (true)
                    {
                        var sw = Stopwatch.StartNew();
                        short[][] copy; // one atomic copy of all blocks; a SetWords during the cycle lands next cycle
                        lock (desired) copy = desired.ToArray(); // arrays are replaced, never mutated, by SetWords
                        for (int i = 0; i < copy.Length; i++)
                        {
                            // Slow links (Modbus bridge, ~310 ms/request) skip unchanged blocks until RewriteMs has passed.
                            if (cfg.RewriteMs > 0 && lastSent[i] != null && lastSent[i].SequenceEqual(copy[i])
                                && clock.ElapsedMilliseconds - lastSentAt[i] < cfg.RewriteMs) continue;
                            int d = cfg.WriteBlocks[i].d;
                            Timed(() => { t.WriteWords(d, copy[i]); return 0; });
                            lastSent[i] = copy[i];
                            lastSentAt[i] = clock.ElapsedMilliseconds;
                        }
                        var words = Timed(() => t.ReadWords(readFirst, readCount));
                        lock (sync)
                        {
                            snapshot = words;
                            snapshotTime = DateTime.UtcNow;
                            if (first) state = PlcState.Connected; // only after the first full write + read
                        }
                        first = false;
                        if (stopEvent.WaitOne(Math.Max(0, cfg.PollMs - (int)sw.ElapsedMilliseconds))) break;
                    }
                }
                catch (Exception e)
                {
                    if (stopEvent.WaitOne(0)) break;
                    CloseTransport();
                    SetState(PlcState.Error, e.Message);
                    stopEvent.WaitOne(2000); // backoff, interruptible
                }
            }
            CloseTransport();
            SetState(PlcState.Disconnected, null);
        }

        T Timed<T>(Func<T> request)
        {
            var sw = Stopwatch.StartNew();
            var r = request();
            lock (sync) rttMs = (int)sw.ElapsedMilliseconds;
            return r;
        }

        void CloseTransport()
        {
            lock (sync) { transport?.Dispose(); transport = null; }
        }

        // Every state except Connected means "no data on this connection": the snapshot is cleared.
        void SetState(PlcState s, string error)
        {
            lock (sync)
            {
                state = s;
                if (error != null) lastError = error;
                snapshot = null;
                snapshotTime = null;
            }
        }
    }
}
