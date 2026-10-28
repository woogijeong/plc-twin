using System.Linq;
using NUnit.Framework;
using PlcTwin.Sim;

namespace PlcTwin.Tests
{
    /// <summary>AC11 data part: rects, order and labels of ref §5.2/§5.3 (colors are checked on screenshots in M3).</summary>
    public class PanelLayoutTests
    {
        // Ref §5.2, row by row, as "address label".
        static readonly string[][] RefRows =
        {
            new[] { "D000.0 공급실린더 전진", "D000.1 공급실린더 후진", "D000.2 송출실린더 전진", "D000.3 송출실린더 후진", "D000.4 가공실린더 하강",
                    "D000.5 가공실린더 상승", "D000.6 배출실린더 전진", "D000.7 배출실린더 후진", "D000.8 스톱실린더 하강", "D000.9 스톱실린더 상승",
                    "D000.A 흡착실린더 전진" },
            new[] { "D000.B 흡착실린더 후진", "D000.C 흡착패드 On", "D000.D 창고실린더 전진", "D000.E 창고실린더 후진", "D000.F 공급센서 감지",
                    "D001.1 물품감지 비금속", "D001.2 물품감지 금속", "D001.3 물품감지 스토퍼", "D002.0 DRIVER READY",
                    "D002.8 1축 ERROR" }, // D001.0 송출센서 감지 was removed (no cell, no bit)
            new[] { "D002.C 1축 BUSY", "D003.0 1축 기동완료", "D003.4 1축 위치완료" },
            new[] { "D100.0 공급실린더 전진", "D100.1 공급실린더 후진", "D100.2 송출실린더 전진", "D100.3 송출실린더 후진", "D100.4 가공실린더 하강",
                    "D100.5 배출실린더 전진", "D100.6 배출실린더 후진", "D100.7 스톱실린더 상승", "D100.8 흡착실린더 전진", "D100.9 흡착실린더 후진",
                    "D100.A 흡착패드 On" },
            new[] { "D100.B 창고실린더 전진", "D100.C 창고실린더 후진", "D100.D 가공드릴 On", "D100.E 컨베이어 On", "D102.0 PLC READY",
                    "D102.1 SERVO ON", "D102.4 1축 STOP", "D102.8 1축 JOG+", "D102.C 1축 JOG-", "D103.0 1축 위치기동" },
        };
        static readonly string[] RefWords =
            { "D040 현재이송값", "D042 에러번호", "D043 입력신호", "D044 현재상태", "D140 전송속도", "D142 위치기동", "D143 에러클리어", "D144 현재변경값", "D146 JOG속도" };
        static readonly int[] RefRowY = { 460, 400, 340, 280, 220 };

        [Test]
        public void BitCells_RowsOrderLabelsRects()
        {
            var bits = PanelLayout.Build().Where(c => c.Kind == PanelKind.Bit).ToList();
            Assert.AreEqual(45, bits.Count);
            int k = 0;
            for (int r = 0; r < RefRows.Length; r++)
                for (int i = 0; i < RefRows[r].Length; i++, k++)
                {
                    var c = bits[k];
                    Assert.AreEqual(RefRows[r][i], string.Join(" ", c.Lines), "cell " + k);
                    Assert.AreEqual((-420 + 80 * i, RefRowY[r], 80, 60), (c.Cx, c.Cy, c.W, c.H), RefRows[r][i]);
                    Assert.AreEqual(r < 3, c.Input, RefRows[r][i]);
                }
        }

        [Test]
        public void WordCells_TitleAndValueRects()
        {
            var cells = PanelLayout.Build();
            var titles = cells.Where(c => c.Kind == PanelKind.WordTitle).ToList();
            var values = cells.Where(c => c.Kind == PanelKind.WordValue).ToList();
            Assert.AreEqual(9, titles.Count);
            Assert.AreEqual(9, values.Count);
            for (int i = 0; i < 9; i++)
            {
                Assert.AreEqual(RefWords[i], string.Join(" ", titles[i].Lines));
                Assert.AreEqual((-420 + 80 * i, 170, 80, 40), (titles[i].Cx, titles[i].Cy, titles[i].W, titles[i].H), RefWords[i]);
                Assert.AreEqual((-420 + 80 * i, 135, 80, 30), (values[i].Cx, values[i].Cy, values[i].W, values[i].H), RefWords[i]);
                Assert.AreEqual(titles[i].D, values[i].D);
                Assert.AreEqual(int.Parse(RefWords[i].Substring(1, 3)), values[i].D);
            }
        }

        [Test]
        public void SixtyThreeClassifiedRectsFirst_ThenButtons()
        {
            var cells = PanelLayout.Build();
            Assert.AreEqual(63, cells.Take(63).Count(c => c.Kind == PanelKind.Bit || c.Kind == PanelKind.WordTitle || c.Kind == PanelKind.WordValue));
            var buttons = cells.Where(c => c.Kind == PanelKind.Button).ToList();
            Assert.AreEqual(new[] { "Metal", "Nonmetal", "Connect", "Disconnect" }, buttons.Select(b => b.Lines[0]).ToArray());
            Assert.AreEqual((-577, 465, 160, 50), (buttons[0].Cx, buttons[0].Cy, buttons[0].W, buttons[0].H));
            Assert.AreEqual((-577, 410, 160, 50), (buttons[1].Cx, buttons[1].Cy, buttons[1].W, buttons[1].H));
            Assert.AreEqual((-380, -470, 160, 50), (buttons[2].Cx, buttons[2].Cy, buttons[2].W, buttons[2].H));
            Assert.AreEqual((-210, -470, 160, 50), (buttons[3].Cx, buttons[3].Cy, buttons[3].W, buttons[3].H));
        }

        [Test]
        public void Colors()
        {
            Assert.AreEqual(0x4EE90A, PanelLayout.OnColor);
            Assert.AreEqual(0xFF896D, PanelLayout.OffColor);
            Assert.AreEqual(0xEEC647, PanelLayout.TitleColor);
            Assert.AreEqual(0xFFFBDA, PanelLayout.ValueColor);
            Assert.AreEqual((78 << 16) | (233 << 8) | 10, PanelLayout.ConnectColor);
            Assert.AreEqual((255 << 16) | (137 << 8) | 109, PanelLayout.DisconnectColor);
            Assert.AreEqual((200 << 16) | (200 << 8) | 200, PanelLayout.IdleButtonColor);
            Assert.AreEqual((70 << 16) | (133 << 8) | 209, PanelLayout.PressedColor);
        }

        [Test]
        public void NoOverlap_AllInside1387x1000()
        {
            var cells = PanelLayout.Build();
            for (int i = 0; i < cells.Count; i++)
            {
                var a = cells[i];
                Assert.LessOrEqual(System.Math.Abs(a.Cx) + a.W / 2, 1387 / 2, a.Lines.FirstOrDefault());
                Assert.LessOrEqual(System.Math.Abs(a.Cy) + a.H / 2, 1000 / 2, a.Lines.FirstOrDefault());
                for (int j = i + 1; j < cells.Count; j++)
                {
                    var b = cells[j];
                    bool overlap = 2 * System.Math.Abs(a.Cx - b.Cx) < a.W + b.W && 2 * System.Math.Abs(a.Cy - b.Cy) < a.H + b.H;
                    Assert.IsFalse(overlap, "cells " + i + " and " + j + " overlap");
                }
            }
        }

        [Test]
        public void AddressesFollowMapBases()
        {
            var cells = PanelLayout.Build(inBits: 200, inWords: 240, outBits: 300, outWords: 340);
            Assert.AreEqual("D200.0", cells[0].Lines[0]);
            Assert.AreEqual("D201.3", cells.First(c => c.Kind == PanelKind.Bit && c.D == 201 && c.Bit == 3).Lines[0]);
            Assert.AreEqual("D303.0", cells.Last(c => c.Kind == PanelKind.Bit).Lines[0]);
            Assert.AreEqual("D240", cells.First(c => c.Kind == PanelKind.WordTitle).Lines[0]);
            Assert.AreEqual("D346", cells.Last(c => c.Kind == PanelKind.WordTitle).Lines[0]);
        }
    }
}
