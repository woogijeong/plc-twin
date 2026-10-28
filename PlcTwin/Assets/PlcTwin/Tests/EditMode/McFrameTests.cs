using System;
using System.Linq;
using NUnit.Framework;
using PlcTwin.Mc;

namespace PlcTwin.Tests
{
    public class McFrameTests
    {
        static byte[] H(string hex) =>
            hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToByte(x, 16)).ToArray();

        [Test]
        public void ReadD100x3Word()
        {
            CollectionAssert.AreEqual(H("50 00 00 FF FF 03 00 0C 00 04 00 01 04 00 00 64 00 00 A8 03 00"),
                McFrame.BuildReadWords(DeviceAddress.Parse("D100"), 3, 4));
        }

        [Test]
        public void WriteM100OnBit()
        {
            CollectionAssert.AreEqual(H("50 00 00 FF FF 03 00 0D 00 04 00 01 14 01 00 64 00 00 90 01 00 10"),
                McFrame.BuildWriteBits(DeviceAddress.Parse("M100"), new[] { true }, 4));
        }

        [Test]
        public void WriteD0x4Words_Golden()
        {
            CollectionAssert.AreEqual(H("50 00 00 FF FF 03 00 14 00 04 00 01 14 00 00 00 00 00 A8 04 00 01 00 02 00 03 00 04 00"),
                McFrame.BuildWriteWords(DeviceAddress.Parse("D0"), new short[] { 1, 2, 3, 4 }, 4));
            var neg = McFrame.BuildWriteWords(DeviceAddress.Parse("D40"), new short[] { -1, short.MinValue }, 4);
            CollectionAssert.AreEqual(H("FF FF 00 80"), neg.Skip(neg.Length - 4).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildWriteWords(DeviceAddress.Parse("D0"), new short[961], 4));
        }

        [Test]
        public void ReadY20x1Bit_HexAddressing()
        {
            CollectionAssert.AreEqual(H("50 00 00 FF FF 03 00 0C 00 04 00 01 04 01 00 20 00 00 9D 01 00"),
                McFrame.BuildReadBits(DeviceAddress.Parse("Y20"), 1, 4));
        }

        [Test]
        public void WriteBitsPackHighNibbleFirst()
        {
            var f = McFrame.BuildWriteBits(DeviceAddress.Parse("M0"), new[] { true, false, true }, 4);
            CollectionAssert.AreEqual(new byte[] { 0x10, 0x10 }, f.Skip(f.Length - 2).ToArray());
            Assert.AreEqual(0x0E, f[7]); // 12 + 2 data bytes
        }

        [Test]
        public void WordResponseIsSigned()
        {
            var r = H("D0 00 00 FF FF 03 00 08 00 00 00 E8 03 FF FF 00 00");
            CollectionAssert.AreEqual(new short[] { 1000, -1, 0 }, McFrame.ParseReadWords(r, 3));
        }

        [Test]
        public void EndCodeThrowsMcException_ErrorInfoDiscarded()
        {
            // len 0B = end code (2) + 9 bytes error info (net, pc, io x2, station, cmd x2, sub x2)
            var r = H("D0 00 00 FF FF 03 00 0B 00 59 C0 00 FF FF 03 00 01 04 00 00");
            var ex = Assert.Throws<McException>(() => McFrame.ParseReadWords(r, 3));
            Assert.AreEqual(0xC059, ex.EndCode);
            Assert.AreEqual(9 + 0x0B, McFrame.ResponseTotalSize(r.Take(9).ToArray()));
        }

        [Test]
        public void BitResponseNibbles()
        {
            var r = H("D0 00 00 FF FF 03 00 04 00 00 00 10 10");
            CollectionAssert.AreEqual(new[] { true, false, true, false }, McFrame.ParseReadBits(r, 4));
        }

        [Test]
        public void BitResponseOddPointCountIgnoresPadding()
        {
            var r = H("D0 00 00 FF FF 03 00 04 00 00 00 11 10");
            CollectionAssert.AreEqual(new[] { true, true, true }, McFrame.ParseReadBits(r, 3));
        }

        [Test]
        public void ResponseTotalSizeFromHeader()
        {
            Assert.AreEqual(17, McFrame.ResponseTotalSize(H("D0 00 00 FF FF 03 00 08 00")));
        }

        [Test]
        public void AsciiReplyThrowsWithHint()
        {
            var r = H("D1 00 00 FF FF 03 00 02 00 00 00");
            var ex = Assert.Throws<McException>(() => McFrame.ParseWriteResponse(r));
            StringAssert.Contains("ASCII", ex.Message);
            StringAssert.Contains("D1 00", ex.Message);
            Assert.Throws<McException>(() => McFrame.ResponseTotalSize(r.Take(9).ToArray()));
        }

        [Test]
        public void LimitsEnforced()
        {
            var d = DeviceAddress.Parse("D0");
            Assert.DoesNotThrow(() => McFrame.BuildReadWords(d, 960, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildReadWords(d, 961, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildReadWords(d, 0, 4));
            var m = DeviceAddress.Parse("M0");
            Assert.DoesNotThrow(() => McFrame.BuildReadBits(m, 3584, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildReadBits(m, 3585, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildWriteBits(m, new bool[3585], 4));
        }
    }
}
