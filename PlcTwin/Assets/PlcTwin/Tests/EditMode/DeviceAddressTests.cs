using System;
using NUnit.Framework;
using PlcTwin.Mc;

namespace PlcTwin.Tests
{
    public class DeviceAddressTests
    {
        [TestCase("X1A", 'X', 0x9C, 26)]
        [TestCase("Y2F", 'Y', 0x9D, 47)]
        [TestCase("M8000", 'M', 0x90, 8000)]
        [TestCase("D0", 'D', 0xA8, 0)]
        [TestCase("Y20", 'Y', 0x9D, 32)]
        public void ParsesValid(string s, char type, int code, int number)
        {
            var d = DeviceAddress.Parse(s);
            Assert.AreEqual(type, d.Type);
            Assert.AreEqual((byte)code, d.Code);
            Assert.AreEqual(number, d.Number);
        }

        [TestCase("Q5")]
        [TestCase("M1F")]
        [TestCase("")]
        public void InvalidThrowsFormatExceptionWithInput(string s)
        {
            var ex = Assert.Throws<FormatException>(() => DeviceAddress.Parse(s));
            StringAssert.Contains(s, ex.Message);
        }
    }
}
