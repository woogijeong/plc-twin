using System;
using System.IO;

namespace PlcTwin.EditorTools
{
    /// <summary>
    /// Fake of the bundled ModbusServerMitsubishi, shaped like the probe captures (tools/out/modbus_probe.txt, 2026-10-22):
    /// two 80-word tables (write table D[writeTableBase..+80) for FC16, read table D[readTableBase..+80) for FC4).
    /// - FC4: always a 169-byte reply = TID echo, proto 0, MBAP length 6 (wrong, as captured), unit, 04, byte count 0xA0,
    ///   then the whole read table big-endian, whatever start/qty say.
    /// - FC16: like the real server (tools/out/modbus_partial_fc16.txt) the start field is IGNORED: the qty words land at
    ///   write-table word 0 (logged as a write at writeTableBase); reply = the first 12 bytes of the request.
    /// - FC3 and any other function: no reply at all.
    /// - One client served at a time; a second connection is accepted but waits unserved until the first is gone.
    /// Requests are framed by their MBAP length (our client sends only standard frames; the reference 256-byte frames
    /// are not modeled). Not observed on the real server, test-only: ForceExceptionCode and the out-of-table exception 2.
    /// </summary>
    public sealed class FakeModbusServer : FakePlcServer
    {
        public const int TableWords = 80;
        readonly short[] writeTable = new short[TableWords], readTable = new short[TableWords];
        readonly int writeBase, readBase;

        /// <summary>Non-zero: every FC4/FC16 request is answered with an exception reply (function | 0x80) carrying this code.</summary>
        public volatile int ForceExceptionCode;

        /// <summary>Test hook: 0..255 = answer FC4/FC16 requests with this unit id instead of the request's; -1 = echo it.</summary>
        public volatile int ReplyUnitOverride = -1;

        protected override bool SingleClient => true;

        public FakeModbusServer(int port = 0, int writeTableBase = 0, int readTableBase = 100) : base(port)
        {
            writeBase = writeTableBase;
            readBase = readTableBase;
            Restart();
        }

        public override void ClearMemory()
        {
            lock (sync) { Array.Clear(writeTable, 0, TableWords); Array.Clear(readTable, 0, TableWords); }
        }

        public override short GetWord(int d) { lock (sync) { var t = Table(d, out int i); return t[i]; } }
        public override void SetWord(int d, short v) { lock (sync) { var t = Table(d, out int i); t[i] = v; } }
        protected override void Store(int d, short[] w) => Array.Copy(w, 0, writeTable, d - writeBase, w.Length);

        short[] Table(int d, out int i)
        {
            i = d - writeBase;
            if (i >= 0 && i < TableWords) return writeTable;
            i = d - readBase;
            if (i >= 0 && i < TableWords) return readTable;
            throw new ArgumentOutOfRangeException(nameof(d), "D" + d + " is in neither table");
        }

        protected override byte[] ReadRequest(Stream s)
        {
            var h = ReadExact(s, 7);
            int len = h[4] << 8 | h[5]; // counts the unit byte already read
            if (len < 2 || len > 260) throw new IOException("bad MBAP length " + len);
            var rest = ReadExact(s, len - 1);
            var req = new byte[7 + rest.Length];
            Buffer.BlockCopy(h, 0, req, 0, 7);
            Buffer.BlockCopy(rest, 0, req, 7, rest.Length);
            return req;
        }

        protected override byte[] Handle(byte[] q)
        {
            var r = HandleCore(q);
            if (r != null && ReplyUnitOverride >= 0) r[6] = (byte)ReplyUnitOverride;
            return r;
        }

        byte[] HandleCore(byte[] q)
        {
            byte fc = q[7];
            if (fc != 0x04 && fc != 0x10) return null; // FC3 and unknown functions: silence, as captured
            int code = ForceExceptionCode;
            if (code != 0) return Exception(q, code);
            if (fc == 0x04)
            {
                var r = new byte[9 + 2 * TableWords];
                r[0] = q[0]; r[1] = q[1]; r[5] = 6; r[6] = q[6]; r[7] = 0x04; r[8] = 2 * TableWords;
                lock (sync)
                    for (int i = 0; i < TableWords; i++) { r[9 + 2 * i] = (byte)(readTable[i] >> 8); r[10 + 2 * i] = (byte)readTable[i]; }
                return r;
            }
            if (q.Length < 13) return Exception(q, 3);
            int qty = q[10] << 8 | q[11]; // start (q[8..9]) ignored, as on the real server
            if (qty < 1 || qty > TableWords || q[12] != 2 * qty || q.Length < 13 + 2 * qty) return Exception(q, 2);
            var w = new short[qty];
            for (int i = 0; i < qty; i++) w[i] = (short)(q[13 + 2 * i] << 8 | q[14 + 2 * i]);
            ApplyWrite(writeBase, w);
            var echo = new byte[12];
            Buffer.BlockCopy(q, 0, echo, 0, 12);
            return echo;
        }

        static byte[] Exception(byte[] q, int code) =>
            new byte[] { q[0], q[1], 0, 0, 0, 3, q[6], (byte)(q[7] | 0x80), (byte)code };
    }
}
