using System;

namespace PlcTwin.Mc
{
    /// <summary>MC protocol 3E binary frames: batch read (0401) and batch write (1401, bits and words). No I/O here.</summary>
    public static class McFrame
    {
        public const int HeaderSize = 9; // D0 00 | net | pc | io(2) | station | len(2)
        public const int MaxWords = 960;
        // ponytail: 3584 bits is a conservative limit; raise per CPU manual if needed
        public const int MaxBits = 3584;

        public static byte[] BuildReadBits(DeviceAddress dev, int points, int monitorTimer250ms)
        {
            CheckPoints(points, MaxBits, "bits");
            return Build(0x0401, 0x0001, dev, points, monitorTimer250ms, 0);
        }

        public static byte[] BuildReadWords(DeviceAddress dev, int points, int monitorTimer250ms)
        {
            CheckPoints(points, MaxWords, "words");
            return Build(0x0401, 0x0000, dev, points, monitorTimer250ms, 0);
        }

        public static byte[] BuildWriteBits(DeviceAddress dev, bool[] values, int monitorTimer250ms)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            CheckPoints(values.Length, MaxBits, "bits");
            int n = (values.Length + 1) / 2;
            var f = Build(0x1401, 0x0001, dev, values.Length, monitorTimer250ms, n);
            int o = f.Length - n;
            for (int i = 0; i < values.Length; i++)
                if (values[i]) f[o + i / 2] |= (byte)(i % 2 == 0 ? 0x10 : 0x01); // 2 points per byte, high nibble first
            return f;
        }

        /// <summary>1401/0000 batch word write, little-endian words.</summary>
        public static byte[] BuildWriteWords(DeviceAddress dev, short[] values, int monitorTimer250ms)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            CheckPoints(values.Length, MaxWords, "words");
            var f = Build(0x1401, 0x0000, dev, values.Length, monitorTimer250ms, values.Length * 2);
            int o = f.Length - values.Length * 2;
            for (int i = 0; i < values.Length; i++) { f[o + 2 * i] = (byte)values[i]; f[o + 2 * i + 1] = (byte)(values[i] >> 8); }
            return f;
        }

        /// <summary>Total response size (header + len) from the first 9 bytes; lets the client read exactly len more bytes.</summary>
        public static int ResponseTotalSize(byte[] header)
        {
            if (header == null || header.Length < HeaderSize) throw new McException("short response header");
            CheckSubheader(header);
            return HeaderSize + (header[7] | header[8] << 8);
        }

        public static short[] ParseReadWords(byte[] resp, int points)
        {
            int o = DataOffset(resp);
            if (resp.Length - o < points * 2) throw new McException("short response data");
            var r = new short[points];
            for (int i = 0; i < points; i++) r[i] = (short)(resp[o + 2 * i] | resp[o + 2 * i + 1] << 8);
            return r;
        }

        public static bool[] ParseReadBits(byte[] resp, int points)
        {
            int o = DataOffset(resp);
            if (resp.Length - o < (points + 1) / 2) throw new McException("short response data");
            var r = new bool[points];
            for (int i = 0; i < points; i++)
            {
                byte b = resp[o + i / 2];
                r[i] = ((i % 2 == 0 ? b >> 4 : b) & 0x0F) != 0;
            }
            return r;
        }

        /// <summary>Validates a write reply (throws McException on bad subheader or non-zero end code).</summary>
        public static void ParseWriteResponse(byte[] resp) { DataOffset(resp); }

        static void CheckSubheader(byte[] b)
        {
            if (b[0] != 0xD0 || b[1] != 0x00)
                throw new McException("bad subheader " + b[0].ToString("X2") + " " + b[1].ToString("X2") + " (PLC may be set to ASCII)");
        }

        // Returns the offset of the data after the end code. On error replies len also covers
        // 9 bytes of error info; we never read it, so data length is never assumed.
        static int DataOffset(byte[] resp)
        {
            if (resp == null || resp.Length < HeaderSize) throw new McException("short response");
            CheckSubheader(resp);
            int len = resp[7] | resp[8] << 8;
            if (len < 2 || resp.Length < HeaderSize + len) throw new McException("short response");
            int end = resp[9] | resp[10] << 8;
            if (end != 0) throw new McException(end);
            return HeaderSize + 2;
        }

        static void CheckPoints(int points, int max, string unit)
        {
            if (points < 1 || points > max)
                throw new ArgumentOutOfRangeException("points", points, "1.." + max + " " + unit);
        }

        static byte[] Build(int cmd, int sub, DeviceAddress dev, int points, int timer, int dataBytes)
        {
            int len = 12 + dataBytes; // counted from the monitoring timer
            var f = new byte[9 + len];
            f[0] = 0x50; f[1] = 0x00; f[2] = 0x00; f[3] = 0xFF; f[4] = 0xFF; f[5] = 0x03; f[6] = 0x00;
            f[7] = (byte)len; f[8] = (byte)(len >> 8);
            f[9] = (byte)timer; f[10] = (byte)(timer >> 8);
            f[11] = (byte)cmd; f[12] = (byte)(cmd >> 8);
            f[13] = (byte)sub; f[14] = (byte)(sub >> 8);
            f[15] = (byte)dev.Number; f[16] = (byte)(dev.Number >> 8); f[17] = (byte)(dev.Number >> 16);
            f[18] = dev.Code;
            f[19] = (byte)points; f[20] = (byte)(points >> 8);
            return f;
        }
    }
}
