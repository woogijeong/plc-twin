using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using PlcTwin.Mc;

namespace PlcTwin.Tests
{
    /// <summary>
    /// Goldens marked "probe" are real captures from the bundled ModbusServerMitsubishi: tools/out/modbus_probe.txt,
    /// probe run 2026-10-22 08:38:43 (the server answered without a PLC, so nothing here is IL-derived).
    /// Replies are replayed by a scripted socket server with the request's TID echoed, as the real server does.
    /// </summary>
    [TestFixture, Timeout(10000)]
    public class ModbusFrameTests
    {
        static byte[] H(string hex) =>
            hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToByte(x, 16)).ToArray();

        // probe F2: standard FC4 read start 0 qty 47 (request) and its 169-byte reply (header; the 160 data bytes were all 00).
        static readonly byte[] F2Request = H("00 01 00 00 00 06 01 04 00 00 00 2F");
        static readonly byte[] F2ReplyHeader = H("00 01 00 00 00 06 01 04 A0"); // MBAP length 6 is wrong: 160 data bytes follow
        // probe F5a / F5b: standard FC16 partial writes and their 12-byte echo replies.
        static readonly byte[] F5aRequest = H("00 05 00 00 00 0F 01 10 00 00 00 04 08 11 11 11 12 11 13 11 14");
        static readonly byte[] F5aReply = H("00 05 00 00 00 0F 01 10 00 00 00 04");
        static readonly byte[] F5bRequest = H("00 06 00 00 00 11 01 10 00 28 00 05 0A 22 22 22 23 22 24 22 25 22 26");
        static readonly byte[] F5bReply = H("00 06 00 00 00 11 01 10 00 28 00 05");
        // probe F4: full-table FC16 (173 bytes) head.
        static readonly byte[] F4RequestHead = H("00 03 00 00 00 A7 01 10 00 00 00 50 A0");

        static byte[] CapturedFc4Reply(byte[] data160)
        {
            var r = new byte[169];
            Buffer.BlockCopy(F2ReplyHeader, 0, r, 0, 9);
            if (data160 != null) Buffer.BlockCopy(data160, 0, r, 9, 160);
            return r;
        }

        static byte[] WithTid(byte[] reply, byte[] req)
        {
            var r = (byte[])reply.Clone();
            r[0] = req[0]; r[1] = req[1];
            return r;
        }

        /// <summary>One-client scripted server: records each request and sends the chunks respond() returns (null = no reply).</summary>
        sealed class Script : IDisposable
        {
            readonly TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            readonly List<byte[]> requests = new List<byte[]>();
            TcpClient client;
            public int Port => ((IPEndPoint)l.LocalEndpoint).Port;

            public Script(Func<byte[], byte[][]> respond)
            {
                l.Start();
                new Thread(() =>
                {
                    try
                    {
                        client = l.AcceptTcpClient();
                        var s = client.GetStream();
                        while (true)
                        {
                            var h = Read(s, 7);
                            var req = h.Concat(Read(s, (h[4] << 8 | h[5]) - 1)).ToArray();
                            lock (requests) requests.Add(req);
                            var chunks = respond(req);
                            if (chunks == null) continue;
                            for (int i = 0; i < chunks.Length; i++)
                            {
                                if (i > 0) Thread.Sleep(50); // separate TCP segments
                                s.Write(chunks[i], 0, chunks[i].Length);
                                s.Flush();
                            }
                        }
                    }
                    catch (Exception) { } // closed
                }) { IsBackground = true }.Start();
            }

            static byte[] Read(Stream s, int n)
            {
                var b = new byte[n];
                for (int o = 0; o < n;)
                {
                    int k = s.Read(b, o, n - o);
                    if (k <= 0) throw new IOException("closed");
                    o += k;
                }
                return b;
            }

            public byte[][] Requests { get { lock (requests) return requests.ToArray(); } }

            public void Dispose()
            {
                l.Stop();
                client?.Close();
            }
        }

        static ModbusTransport Open(Script s, int timeoutMs = 1500) =>
            ModbusTransport.Connect("127.0.0.1", s.Port, 1000, timeoutMs, 1, 0, 100, new ManualResetEvent(false));

        // ---- request goldens ----

        [Test]
        public void RequestGoldens_FromProbe()
        {
            CollectionAssert.AreEqual(F2Request, ModbusTransport.BuildReadFrame(1, 1, 0, 47));
            CollectionAssert.AreEqual(F5aRequest, ModbusTransport.BuildWriteFrame(5, 1, 0, new short[] { 0x1111, 0x1112, 0x1113, 0x1114 }));
            CollectionAssert.AreEqual(F5bRequest, ModbusTransport.BuildWriteFrame(6, 1, 40, new short[] { 0x2222, 0x2223, 0x2224, 0x2225, 0x2226 }));
            var f4 = ModbusTransport.BuildWriteFrame(3, 1, 0, new short[80]);
            Assert.AreEqual(173, f4.Length); // probe: REQ len=173
            CollectionAssert.AreEqual(F4RequestHead, f4.Take(13).ToArray());
        }

        [Test]
        public void NegativeValues_TwosComplementBigEndian()
        {
            var f = ModbusTransport.BuildWriteFrame(1, 1, 0, new short[] { -1, short.MinValue, 1, -2 });
            CollectionAssert.AreEqual(H("FF FF 80 00 00 01 FF FE"), f.Skip(13).ToArray());
        }

        // The transport always asks for the whole table (start 0, qty 80): the server returns all 80 words regardless
        // (probe F1 qty 0 and F2 qty 47 both got 160 data bytes). Only start 0 was probed.
        [Test]
        public void ReadWords_SendsStandardFc4Start0Qty80_AndParsesCapturedReply()
        {
            using (var s = new Script(req => new[] { WithTid(CapturedFc4Reply(null), req) }))
            using (var t = Open(s))
            {
                CollectionAssert.AreEqual(new short[47], t.ReadWords(100, 47));
                CollectionAssert.AreEqual(H("00 01 00 00 00 06 01 04 00 00 00 50"), s.Requests[0]);
                t.ReadWords(140, 7);
                CollectionAssert.AreEqual(H("00 02 00 00 00 06 01 04 00 00 00 50"), s.Requests[1]); // TID increments
            }
        }

        [Test]
        public void Fc4Reply_SplitAcrossTcpReads_SlicedFromWholeTable()
        {
            var data = new byte[160];
            for (int i = 0; i < 80; i++) { short w = (short)(i % 2 == 0 ? i + 1 : -(i + 1)); data[2 * i] = (byte)(w >> 8); data[2 * i + 1] = (byte)w; }
            using (var s = new Script(req =>
            {
                var r = WithTid(CapturedFc4Reply(data), req);
                return new[] { r.Take(5).ToArray(), r.Skip(5).Take(60).ToArray(), r.Skip(65).ToArray() }; // split inside MBAP and data
            }))
            using (var t = Open(s))
            {
                CollectionAssert.AreEqual(new short[] { 1, -2, 3, -4 }, t.ReadWords(100, 4));
                CollectionAssert.AreEqual(new short[] { 41, -42, 43, -44, 45, -46, 47 }, t.ReadWords(140, 7));
            }
        }

        [Test]
        public void WriteWords_StandardFc16_AcceptsCapturedEcho()
        {
            // Replays the captured F5a echo reply (TID echoed, as the server does).
            using (var s = new Script(req => new[] { WithTid(F5aReply, req) }))
            using (var t = Open(s))
            {
                t.WriteWords(0, new short[] { 0x1111, 0x1112, 0x1113, 0x1114 });
                CollectionAssert.AreEqual(F5aRequest.Skip(2), s.Requests[0].Skip(2)); // equal to the probe frame except the TID
            }
            // An echo whose start/qty differ from the request is rejected.
            using (var s = new Script(req => new[] { WithTid(F5bReply, req) }))
            using (var t = Open(s))
                StringAssert.Contains("echo", Assert.Throws<ModbusException>(() => t.WriteWords(0, new short[4])).Message);
        }

        // The server ignores the FC16 start (tools/out/modbus_partial_fc16.txt), so every write is one FC16 start 0
        // carrying the D0.. prefix up to the highest word written so far; unowned words in between go out as 0.
        [Test]
        public void WriteWords_SendsD0PrefixToExtent_Start0()
        {
            short[] d0 = { 0x1111, 0x1112, 0x1113, 0x1114 }, d40 = { 0x2222, 0x2223, 0x2224, 0x2225, 0x2226 };
            var both = new short[45];
            d0.CopyTo(both, 0);
            d40.CopyTo(both, 40);
            var only40 = new short[45];
            d40.CopyTo(only40, 40);
            using (var s = new Script(req => new[] { req.Take(12).ToArray() })) // 12-byte echo, as the server does
            using (var t = Open(s))
            {
                t.WriteWords(0, d0);
                t.WriteWords(40, d40);
                var r = s.Requests;
                CollectionAssert.AreEqual(ModbusTransport.BuildWriteFrame(1, 1, 0, d0), r[0]);
                CollectionAssert.AreEqual(H("00 00 00 2D 5A"), r[1].Skip(8).Take(5)); // start 0, qty 45, 90 bytes
                CollectionAssert.AreEqual(ModbusTransport.BuildWriteFrame(2, 1, 0, both), r[1]);
            }
            // Poller order (D40 block first on a new connection): D0..D3 go out as 0 until the D0 block is written.
            using (var s = new Script(req => new[] { req.Take(12).ToArray() }))
            using (var t = Open(s))
            {
                t.WriteWords(40, d40);
                t.WriteWords(0, d0);
                var r = s.Requests;
                CollectionAssert.AreEqual(ModbusTransport.BuildWriteFrame(1, 1, 0, only40), r[0]);
                CollectionAssert.AreEqual(ModbusTransport.BuildWriteFrame(2, 1, 0, both), r[1]);
            }
        }

        [Test]
        public void ExceptionReply_ThrowsAndDiscardsSocket()
        {
            using (var s = new Script(req => new[] { new byte[] { req[0], req[1], 0, 0, 0, 3, 1, 0x84, 0x02 } }))
            using (var t = Open(s))
            {
                var ex = Assert.Throws<ModbusException>(() => t.ReadWords(100, 4));
                Assert.AreEqual(2, ex.Code);
                Assert.AreEqual("exception code 2", ex.Message);
                Assert.Catch<Exception>(() => t.ReadWords(100, 4), "socket must be discarded after an error");
            }
        }

        [Test]
        public void TidMismatch_ThrowsAndDiscardsSocket()
        {
            using (var s = new Script(req => new[] { WithTid(CapturedFc4Reply(null), new byte[] { 0x00, 0xFF }) }))
            using (var t = Open(s))
            {
                StringAssert.Contains("TID mismatch", Assert.Throws<ModbusException>(() => t.ReadWords(100, 4)).Message);
                Assert.Catch<Exception>(() => t.ReadWords(100, 4), "socket must be discarded after an error");
            }
        }

        [Test]
        public void ReceiveTimeout_ThrowsTimeoutException()
        {
            using (var s = new Script(req => null)) // like the real server's answer to FC3: nothing
            using (var t = Open(s, timeoutMs: 300))
            {
                var sw = Stopwatch.StartNew();
                var ex = Assert.Throws<TimeoutException>(() => t.WriteWords(0, new short[4]));
                Assert.AreEqual("timeout", ex.Message);
                Assert.Less(sw.ElapsedMilliseconds, 300 + 500);
                Assert.Catch<Exception>(() => t.WriteWords(0, new short[4]), "socket must be discarded after an error");
            }
        }

        [Test]
        public void OutOfTableRange_Rejected()
        {
            using (var s = new Script(req => null))
            using (var t = Open(s))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => t.ReadWords(99, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => t.ReadWords(140, 41));
                Assert.Throws<ArgumentOutOfRangeException>(() => t.WriteWords(78, new short[3]));
                Assert.AreEqual(0, s.Requests.Length);
            }
        }
    }
}
