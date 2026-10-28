using System;
using System.IO;

namespace PlcTwin.EditorTools
{
    /// <summary>
    /// In-memory 3E binary server for tests: D words only, 0401/0000 batch read and 1401/0000 batch write.
    /// Cannot model PLC-side behaviour (KeepAlive, OPEN state, write-at-RUN rejection, ladder overwrites, ASCII mode).
    /// </summary>
    public sealed class FakeMcServer : FakePlcServer
    {
        const int Size = 0x10000;
        readonly short[] words = new short[Size]; // D (0xA8)

        /// <summary>Non-zero: every request is answered with this end code.</summary>
        public volatile int ForceEndCode;

        public FakeMcServer(int port = 0) : base(port) { Restart(); }

        public override void ClearMemory() { lock (sync) Array.Clear(words, 0, words.Length); }
        public override short GetWord(int d) { lock (sync) return words[d]; }
        public override void SetWord(int d, short v) { lock (sync) words[d] = v; }
        protected override void Store(int d, short[] w) => Array.Copy(w, 0, words, d, w.Length);

        // head(9) then body: timer(2) cmd(2) sub(2) number(3) code(1) points(2) [data]
        protected override byte[] ReadRequest(Stream s)
        {
            var head = ReadExact(s, 9);
            return ReadExact(s, head[7] | head[8] << 8);
        }

        protected override byte[] Handle(byte[] b)
        {
            int end = ForceEndCode;
            if (end != 0) return Reply(end, new byte[9]); // error info: 9 bytes
            int cmd = b[2] | b[3] << 8, sub = b[4] | b[5] << 8;
            int num = b[6] | b[7] << 8 | b[8] << 16, pts = b[10] | b[11] << 8;
            if (b[9] != 0xA8 || sub != 0x0000) return Reply(0xC059, new byte[9]); // only D words are supported
            if (num + pts > Size) return Reply(0xC056, new byte[9]); // device out of range

            if (cmd == 0x0401)
            {
                var data = new byte[pts * 2];
                lock (sync)
                    for (int i = 0; i < pts; i++) { data[2 * i] = (byte)words[num + i]; data[2 * i + 1] = (byte)(words[num + i] >> 8); }
                return Reply(0, data);
            }
            if (cmd == 0x1401)
            {
                var w = new short[pts];
                for (int i = 0; i < pts; i++) w[i] = (short)(b[12 + 2 * i] | b[13 + 2 * i] << 8);
                ApplyWrite(num, w);
                return Reply(0, new byte[0]);
            }
            return Reply(0xC059, new byte[9]); // command not supported
        }

        static byte[] Reply(int endCode, byte[] data)
        {
            int len = 2 + data.Length;
            var r = new byte[9 + len];
            r[0] = 0xD0; r[1] = 0x00; r[2] = 0x00; r[3] = 0xFF; r[4] = 0xFF; r[5] = 0x03; r[6] = 0x00;
            r[7] = (byte)len; r[8] = (byte)(len >> 8);
            r[9] = (byte)endCode; r[10] = (byte)(endCode >> 8);
            Buffer.BlockCopy(data, 0, r, 11, data.Length);
            return r;
        }
    }
}
