using System;
using System.Net.Sockets;
using System.Threading;

namespace PlcTwin.Mc
{
    /// <summary>Modbus failure. Code is the exception code of a (function | 0x80) reply, or -1 for a malformed reply.</summary>
    public sealed class ModbusException : Exception
    {
        public int Code { get; }
        public ModbusException(int code) : base("exception code " + code) { Code = code; }
        public ModbusException(string message) : base(message) { Code = -1; }
    }

    /// <summary>
    /// Modbus TCP D-word transport for the bundled ModbusServerMitsubishi, framed per tools/out/modbus_probe.txt (2026-10-22):
    /// - Writes: standard FC16; reply = 12-byte echo of the request head. The server IGNORES the FC16 start field: every
    ///   FC16 lands its qty words at word 0 of the write table (tools/out/modbus_partial_fc16.txt). So the transport keeps
    ///   a shadow of the write table and each WriteWords sends ONE FC16 start 0 with shadow words [0, extent), extent =
    ///   the highest register+count written so far on this connection. With the D40 and D0 blocks that is D0..D44, and
    ///   D4..D39 are sent as 0 (user-approved deviation from "no zero-fill", Modbus only). The first D40-block write of a
    ///   connection sends D0..D3 as 0 until the D0 block is written later in the same cycle (accepted).
    /// - Reads: standard FC4 to the read table (register = d - readTableBase). The server ALWAYS answers with
    ///   9 header bytes + byte count 0xA0 + the whole 80-word table, whatever start/qty say, and its MBAP length field
    ///   is wrong (6). So the reply size is 9 + byteCount (never the MBAP length), and we request start 0 qty 80 and slice.
    /// - FC3 gets no reply from that server and is never sent. Only FC4 and FC16 exist.
    /// - The server serves one client at a time. Each request takes about 310 ms there.
    /// Any error (timeout, I/O, TID mismatch, exception reply, malformed reply) closes the socket for good.
    /// </summary>
    public sealed class ModbusTransport : IPlcTransport
    {
        public const int TableWords = 80;
        const byte FcRead = 0x04, FcWrite = 0x10;

        readonly Socket sock;
        readonly object io = new object();
        readonly byte unit;
        readonly int writeBase, readBase;
        readonly short[] shadow = new short[TableWords]; // write table as last sent (guarded by io)
        int extent; // words [0, extent) are sent by every FC16
        ushort tid; // first request uses TID 1

        ModbusTransport(Socket s, byte unitId, int writeTableBase, int readTableBase)
        {
            sock = s; unit = unitId; writeBase = writeTableBase; readBase = readTableBase;
        }

        /// <summary>Same connect semantics as McClient.Connect (BeginConnect + WaitAny(stop), numeric IPv4).</summary>
        public static ModbusTransport Connect(string ip, int port, int connectTimeoutMs, int timeoutMs, byte unitId,
                                              int writeTableBase, int readTableBase, WaitHandle stop) =>
            new ModbusTransport(McClient.ConnectSocket(ip, port, connectTimeoutMs, timeoutMs, stop), unitId, writeTableBase, readTableBase);

        /// <summary>Standard 12-byte FC4 request (MBAP length 6).</summary>
        public static byte[] BuildReadFrame(ushort tid, byte unit, int start, int qty) =>
            new byte[] { (byte)(tid >> 8), (byte)tid, 0, 0, 0, 6, unit, FcRead, (byte)(start >> 8), (byte)start, (byte)(qty >> 8), (byte)qty };

        /// <summary>Standard FC16 request; words big-endian two's complement.</summary>
        public static byte[] BuildWriteFrame(ushort tid, byte unit, int start, short[] values)
        {
            int n = values.Length, len = 7 + 2 * n; // unit + fc + start(2) + qty(2) + byteCount + data
            var f = new byte[6 + len];
            f[0] = (byte)(tid >> 8); f[1] = (byte)tid;
            f[4] = (byte)(len >> 8); f[5] = (byte)len;
            f[6] = unit; f[7] = FcWrite;
            f[8] = (byte)(start >> 8); f[9] = (byte)start;
            f[10] = (byte)(n >> 8); f[11] = (byte)n;
            f[12] = (byte)(2 * n);
            for (int i = 0; i < n; i++) { f[13 + 2 * i] = (byte)(values[i] >> 8); f[14 + 2 * i] = (byte)values[i]; }
            return f;
        }

        public short[] ReadWords(int d, int count)
        {
            int r = d - readBase;
            CheckRange(r, count, "read");
            lock (io)
            {
                var data = Exchange(BuildReadFrame(++tid, unit, 0, TableWords));
                if (data.Length < 2 * (r + count)) Fail("short FC4 reply: " + data.Length + " data bytes");
                var w = new short[count];
                for (int i = 0; i < count; i++) w[i] = (short)(data[2 * (r + i)] << 8 | data[2 * (r + i) + 1]);
                return w;
            }
        }

        public void WriteWords(int d, short[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            int r = d - writeBase;
            CheckRange(r, values.Length, "write");
            lock (io)
            {
                Array.Copy(values, 0, shadow, r, values.Length);
                extent = Math.Max(extent, r + values.Length);
                var words = new short[extent];
                Array.Copy(shadow, words, extent);
                Exchange(BuildWriteFrame(++tid, unit, 0, words)); // server ignores start: always the D0.. prefix
            }
        }

        static void CheckRange(int reg, int count, string table)
        {
            if (count < 1 || reg < 0 || reg + count > TableWords)
                throw new ArgumentOutOfRangeException(nameof(reg), table + " registers " + reg + "+" + count + " outside the " + TableWords + "-word table");
        }

        // Returns the FC4 data bytes, or the 4-byte FC16 echo (start, qty). Caller holds io.
        byte[] Exchange(byte[] req)
        {
            try
            {
                sock.Send(req);
                var h = new byte[8]; // MBAP (7) + function; the MBAP length field is never trusted
                MbRead(h);
                if (h[0] != req[0] || h[1] != req[1])
                    Fail("TID mismatch: sent " + (req[0] << 8 | req[1]) + ", got " + (h[0] << 8 | h[1]));
                if (h[6] != req[6]) Fail("unit id mismatch: sent " + req[6] + ", got " + h[6]);
                if (h[2] != 0 || h[3] != 0) Fail("bad protocol id " + (h[2] << 8 | h[3]));
                byte fc = req[7];
                if (h[7] == (fc | 0x80)) throw new ModbusException(MbRead(new byte[1])[0]);
                if (h[7] != fc) Fail("unexpected function 0x" + h[7].ToString("X2") + " (sent 0x" + fc.ToString("X2") + ")");
                if (fc == FcRead) return MbRead(new byte[MbRead(new byte[1])[0]]); // 9 + byteCount bytes in all
                var echo = MbRead(new byte[4]);
                for (int i = 0; i < 4; i++)
                    if (echo[i] != req[8 + i]) Fail("FC16 echo mismatch");
                return echo;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
            {
                Close();
                throw new TimeoutException("timeout");
            }
            catch
            {
                Close();
                throw;
            }
        }

        byte[] MbRead(byte[] buf)
        {
            McClient.ReadExact(sock, buf, 0, buf.Length);
            return buf;
        }

        static void Fail(string message) => throw new ModbusException(message);

        /// <summary>Safe from any thread; unblocks a pending receive.</summary>
        public void Close()
        {
            try { sock.Close(); } catch (Exception) { }
        }

        public void Dispose() => Close();
    }
}
