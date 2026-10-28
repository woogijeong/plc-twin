using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PlcTwin.EditorTools
{
    /// <summary>One applied write frame: start D number, ms since the server was created, and the written words.</summary>
    public readonly struct FakeWrite
    {
        public readonly int D;
        public readonly long TimeMs;
        public readonly short[] Words;
        public FakeWrite(int d, long timeMs, short[] words) { D = d; TimeMs = timeMs; Words = words; }
    }

    /// <summary>
    /// Shared TCP plumbing and test controls of FakeMcServer and FakeModbusServer (127.0.0.1 only). Memory is addressed by
    /// D number. Writes are applied (memory, counter, log) when their reply is about to be sent, after any write delay.
    /// </summary>
    public abstract class FakePlcServer : IDisposable
    {
        protected readonly object sync = new object();
        readonly Dictionary<int, int> writeCounts = new Dictionary<int, int>();
        readonly Dictionary<int, int> delayFor = new Dictionary<int, int>();
        readonly List<FakeWrite> log = new List<FakeWrite>();
        readonly List<TcpClient> clients = new List<TcpClient>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly object serveGate = new object();
        TcpListener listener;
        int writeDelayMs, connections;

        public int Port { get; private set; }
        /// <summary>Accept connections and read requests but never reply (requests are not processed).</summary>
        public volatile bool Silent;
        /// <summary>Every reply is held this many ms (models server latency; the real Modbus server takes about 310 ms).</summary>
        public volatile int ReplyDelayMs;
        /// <summary>Set when a delayed write request arrives (before its reply is held).</summary>
        public readonly ManualResetEvent WriteStarted = new ManualResetEvent(false);

        /// <summary>One-shot: holds the reply of the next write frame for this many ms.</summary>
        public int WriteDelayMs
        {
            get { lock (sync) return writeDelayMs; }
            set { lock (sync) writeDelayMs = value; }
        }

        /// <summary>Persistent: every write frame whose start device is Dd is held ms until reset with ms = 0.</summary>
        public void WriteDelayMsFor(int d, int ms)
        {
            lock (sync) { if (ms > 0) delayFor[d] = ms; else delayFor.Remove(d); }
        }

        /// <summary>TCP connections accepted since creation (across restarts).</summary>
        public int Connections { get { lock (sync) return connections; } }

        /// <summary>True: like the real Modbus server, only one client is served at a time; others wait unserved.</summary>
        protected virtual bool SingleClient => false;

        protected FakePlcServer(int port) { Port = port; }

        /// <summary>(Re)starts listening on the same port (port 0 picks a free one the first time).</summary>
        public void Restart()
        {
            var l = new TcpListener(IPAddress.Loopback, Port);
            l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            l.Start();
            Port = ((IPEndPoint)l.LocalEndpoint).Port;
            lock (sync) listener = l;
            new Thread(() => AcceptLoop(l)) { IsBackground = true, Name = GetType().Name + ".accept" }.Start();
        }

        /// <summary>Stops listening and drops every connection. Memory and logs are kept.</summary>
        public void Stop()
        {
            lock (sync)
            {
                listener?.Stop();
                listener = null;
                foreach (var c in clients) c.Close();
                clients.Clear();
            }
        }

        public void Dispose() => Stop();

        public abstract void ClearMemory();
        public abstract short GetWord(int d);
        public abstract void SetWord(int d, short v);

        /// <summary>Number of applied write frames whose start device is Dd.</summary>
        public int WriteCount(int d)
        {
            lock (sync) return writeCounts.TryGetValue(d, out int n) ? n : 0;
        }

        /// <summary>Copy of the write-order log (every applied write frame, in order).</summary>
        public FakeWrite[] WriteLog()
        {
            lock (sync) return log.ToArray();
        }

        /// <summary>Reads one request; throws on a closed stream.</summary>
        protected abstract byte[] ReadRequest(Stream s);
        /// <summary>Returns the reply, or null for no reply at all.</summary>
        protected abstract byte[] Handle(byte[] request);
        /// <summary>Stores words at D numbers d.. (caller holds sync).</summary>
        protected abstract void Store(int d, short[] words);

        /// <summary>Applies a write frame after the one-shot and per-device delays.</summary>
        protected void ApplyWrite(int d, short[] words)
        {
            int delay;
            lock (sync)
            {
                delay = writeDelayMs + (delayFor.TryGetValue(d, out int p) ? p : 0);
                writeDelayMs = 0; // one-shot
            }
            if (delay > 0)
            {
                WriteStarted.Set();
                Thread.Sleep(delay);
            }
            lock (sync)
            {
                Store(d, words);
                writeCounts[d] = WriteCount(d) + 1;
                log.Add(new FakeWrite(d, clock.ElapsedMilliseconds, (short[])words.Clone()));
            }
        }

        void AcceptLoop(TcpListener l)
        {
            try
            {
                while (true)
                {
                    var c = l.AcceptTcpClient();
                    lock (sync)
                    {
                        if (listener != l) { c.Close(); return; }
                        clients.Add(c);
                        connections++;
                    }
                    new Thread(() => Serve(c)) { IsBackground = true, Name = GetType().Name + ".client" }.Start();
                }
            }
            catch (Exception) { } // listener stopped
        }

        void Serve(TcpClient c)
        {
            try
            {
                if (SingleClient) { lock (serveGate) Loop(c.GetStream()); }
                else Loop(c.GetStream());
            }
            catch (Exception) { } // client gone or server stopped
            finally
            {
                lock (sync) clients.Remove(c);
                c.Close();
            }
        }

        void Loop(Stream s)
        {
            while (true)
            {
                var req = ReadRequest(s);
                if (Silent) continue;
                int d = ReplyDelayMs;
                if (d > 0) Thread.Sleep(d);
                var reply = Handle(req);
                if (reply != null) s.Write(reply, 0, reply.Length);
            }
        }

        protected static byte[] ReadExact(Stream s, int count)
        {
            var buf = new byte[count];
            for (int off = 0; off < count;)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) throw new IOException("closed");
                off += n;
            }
            return buf;
        }
    }
}
