using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PlcTwin.Sim;

namespace PlcTwin.Tests
{
    public class PlcMapTests
    {
        [Test]
        public void RoundTrip_EveryBit()
        {
            for (int bit = 0; bit < 64; bit++)
            {
                var w = new short[4];
                PlcMap.Set(w, bit, true);
                for (int i = 0; i < 4; i++)
                    Assert.AreEqual(i == bit >> 4 ? (short)(1 << (bit & 15)) : (short)0, w[i], "bit " + bit + " word " + i);
                for (int other = 0; other < 64; other++)
                    Assert.AreEqual(other == bit, PlcMap.Get(w, other), "set " + bit + ", read " + other);
                PlcMap.Set(w, bit, false);
                CollectionAssert.AreEqual(new short[4], w, "clear " + bit);
            }
        }

        [Test]
        public void SetKeepsOtherBits_Bit15IsNegative()
        {
            var w = new short[4];
            PlcMap.Set(w, 0x0F, true);
            PlcMap.Set(w, 0x00, true);
            Assert.AreEqual(unchecked((short)0x8001), w[0]);
            PlcMap.Set(w, 0x0F, false);
            Assert.AreEqual(1, w[0]);
        }

        static readonly string[] WordOffsets = { "Position", "ErrorNo", "InputSignal", "Status", "Speed", "Command", "ErrorClear", "Target", "JogSpeed" };

        static int[] Constants(System.Type t) =>
            t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && !WordOffsets.Contains(f.Name))
             .Select(f => (int)f.GetRawConstantValue()).ToArray();

        // Two independent transcriptions of ref §1.5/§5.2 (the PlcMap constants and the panel table) must name the same bits.
        [Test]
        public void BitConstants_MatchThePanelCells()
        {
            var cells = PanelLayout.Build().Where(c => c.Kind == PanelKind.Bit).ToList();
            var inPanel = cells.Where(c => c.Input).Select(c => c.D * 16 + c.Bit).OrderBy(x => x).ToArray();
            var outPanel = cells.Where(c => !c.Input).Select(c => (c.D - 100) * 16 + c.Bit).OrderBy(x => x).ToArray();
            var inConst = Constants(typeof(PlcMap.In));
            var outConst = Constants(typeof(PlcMap.Out));
            Assert.AreEqual(24, inConst.Length);
            Assert.AreEqual(21, outConst.Length);
            Assert.AreEqual(inConst.Length, inConst.Distinct().Count(), "duplicate In constant");
            Assert.AreEqual(outConst.Length, outConst.Distinct().Count(), "duplicate Out constant");
            CollectionAssert.AreEqual(inPanel, inConst.OrderBy(x => x).ToArray());
            CollectionAssert.AreEqual(outPanel, outConst.OrderBy(x => x).ToArray());
        }

        [Test]
        public void SpotChecks_RefTable()
        {
            Assert.AreEqual(0x13, PlcMap.In.StopperDetect, "D1.3 물품감지 스토퍼");
            Assert.AreEqual(0x0F, PlcMap.In.SupplySensor, "D0.F 공급센서");
            Assert.AreEqual(0x2C, PlcMap.In.AxisBusy, "D2.C");
            Assert.AreEqual(0x34, PlcMap.In.PosDone, "D3.4");
            Assert.AreEqual(0x0E, PlcMap.Out.Conveyor, "D100.E");
            Assert.AreEqual(0x24, PlcMap.Out.Stop, "D102.4");
            Assert.AreEqual(0x30, PlcMap.Out.Run, "D103.0");
            Assert.AreEqual(new[] { 0, 2, 3, 4, 6 },
                new[] { PlcMap.Out.Speed, PlcMap.Out.Command, PlcMap.Out.ErrorClear, PlcMap.Out.Target, PlcMap.Out.JogSpeed }, "D140/142/143/144/146");
        }
    }
}
