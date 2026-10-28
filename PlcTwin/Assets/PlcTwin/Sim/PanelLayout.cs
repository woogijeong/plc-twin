using System.Collections.Generic;

namespace PlcTwin.Sim
{
    public enum PanelKind { Bit, WordTitle, WordValue, Button, Extra }

    /// <summary>One rect in screen pixels, center-anchored (cx, cy from the screen center, +y up), w x h.</summary>
    public sealed class PanelCell
    {
        public PanelKind Kind;
        public int Cx, Cy, W, H;
        /// <summary>Device word number (Bit/WordTitle/WordValue); -1 for buttons and extras.</summary>
        public int D = -1;
        /// <summary>0..15 for Bit cells, else -1.</summary>
        public int Bit = -1;
        /// <summary>True for sim → PLC cells (D0-D3, D40-D44).</summary>
        public bool Input;
        /// <summary>Text lines: Bit = address / label / label; WordTitle = address / label; buttons and extras = name.</summary>
        public string[] Lines;
    }

    /// <summary>
    /// The status panel of ref §5.2 (45 bit cells, no D001.0 cell, 9 word cells = title + value rects, 64 rects), the buttons of ref §5.3 and
    /// our extra widgets, as one table so the IMGUI panel and the screenshot masks share it. Addresses come from the map bases.
    /// </summary>
    public static class PanelLayout
    {
        // Colors 0xRRGGBB (ref §5.2/§5.3).
        public const int OnColor = 0x4EE90A, OffColor = 0xFF896D, TitleColor = 0xEEC647, ValueColor = 0xFFFBDA;
        public const int MetalColor = 0x4380C9, NonmetalColor = 0xF58469, ConnectColor = 0x4EE90A, DisconnectColor = 0xFF896D,
                         IdleButtonColor = 0xC8C8C8, PressedColor = 0x4685D1;

        public const int BitW = 80, BitH = 60, Pitch = 80, FirstX = -420, TitleH = 40, ValueH = 30;
        public static readonly int[] RowY = { 460, 400, 340, 280, 220, 170, 135 };

        // (block, word offset, bit, label). block: 0 = inBits (D0), 1 = outBits (D100). Rows of ref §5.2 in order.
        static readonly (int block, int word, int bit, string label)[][] BitRows =
        {
            new[] { (0, 0, 0x0, "공급실린더 전진"), (0, 0, 0x1, "공급실린더 후진"), (0, 0, 0x2, "송출실린더 전진"), (0, 0, 0x3, "송출실린더 후진"),
                    (0, 0, 0x4, "가공실린더 하강"), (0, 0, 0x5, "가공실린더 상승"), (0, 0, 0x6, "배출실린더 전진"), (0, 0, 0x7, "배출실린더 후진"),
                    (0, 0, 0x8, "스톱실린더 하강"), (0, 0, 0x9, "스톱실린더 상승"), (0, 0, 0xA, "흡착실린더 전진") },
            new[] { (0, 0, 0xB, "흡착실린더 후진"), (0, 0, 0xC, "흡착패드 On"), (0, 0, 0xD, "창고실린더 전진"), (0, 0, 0xE, "창고실린더 후진"),
                    (0, 0, 0xF, "공급센서 감지"), (0, 1, 0x1, "물품감지 비금속"), (0, 1, 0x2, "물품감지 금속"),
                    (0, 1, 0x3, "물품감지 스토퍼"), (0, 2, 0x0, "DRIVER READY"), (0, 2, 0x8, "1축 ERROR") },
            new[] { (0, 2, 0xC, "1축 BUSY"), (0, 3, 0x0, "1축 기동완료"), (0, 3, 0x4, "1축 위치완료") },
            new[] { (1, 0, 0x0, "공급실린더 전진"), (1, 0, 0x1, "공급실린더 후진"), (1, 0, 0x2, "송출실린더 전진"), (1, 0, 0x3, "송출실린더 후진"),
                    (1, 0, 0x4, "가공실린더 하강"), (1, 0, 0x5, "배출실린더 전진"), (1, 0, 0x6, "배출실린더 후진"), (1, 0, 0x7, "스톱실린더 상승"),
                    (1, 0, 0x8, "흡착실린더 전진"), (1, 0, 0x9, "흡착실린더 후진"), (1, 0, 0xA, "흡착패드 On") },
            new[] { (1, 0, 0xB, "창고실린더 전진"), (1, 0, 0xC, "창고실린더 후진"), (1, 0, 0xD, "가공드릴 On"), (1, 0, 0xE, "컨베이어 On"),
                    (1, 2, 0x0, "PLC READY"), (1, 2, 0x1, "SERVO ON"), (1, 2, 0x4, "1축 STOP"), (1, 2, 0x8, "1축 JOG+"),
                    (1, 2, 0xC, "1축 JOG-"), (1, 3, 0x0, "1축 위치기동") },
        };

        // (block, word offset, label). block: 0 = inWords (D40), 1 = outWords (D140).
        static readonly (int block, int word, string label)[] Words =
        {
            (0, 0, "현재이송값"), (0, 2, "에러번호"), (0, 3, "입력신호"), (0, 4, "현재상태"),
            (1, 0, "전송속도"), (1, 2, "위치기동"), (1, 3, "에러클리어"), (1, 4, "현재변경값"), (1, 6, "JOG속도"),
        };

        public const string Metal = "Metal", Nonmetal = "Nonmetal", Connect = "Connect", Disconnect = "Disconnect",
                            Reset = "Reset", PanelToggle = "Panel", StateLine = "State", Transport = "Transport", Ip = "IP", Port = "Port";
        /// <summary>Camera preset buttons, in the order of PlcTwinApp.Views.</summary>
        public static readonly string[] Views = { "Default", "Front", "Iso", "Top" };

        /// <summary>All rects: 45 bit cells, then 9 word titles, 9 word values, 4 reference buttons, our extra widgets.</summary>
        public static List<PanelCell> Build(int inBits = 0, int inWords = 40, int outBits = 100, int outWords = 140)
        {
            var cells = new List<PanelCell>();
            for (int r = 0; r < BitRows.Length; r++)
                for (int i = 0; i < BitRows[r].Length; i++)
                {
                    var (block, word, bit, label) = BitRows[r][i];
                    int d = (block == 0 ? inBits : outBits) + word;
                    int sp = label.IndexOf(' ');
                    cells.Add(new PanelCell
                    {
                        Kind = PanelKind.Bit, Cx = FirstX + Pitch * i, Cy = RowY[r], W = BitW, H = BitH, D = d, Bit = bit, Input = block == 0,
                        Lines = new[] { "D" + d.ToString("000") + "." + bit.ToString("X"), label.Substring(0, sp), label.Substring(sp + 1) },
                    });
                }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < Words.Length; i++)
                {
                    var (block, word, label) = Words[i];
                    int d = (block == 0 ? inWords : outWords) + word;
                    bool title = pass == 0;
                    cells.Add(new PanelCell
                    {
                        Kind = title ? PanelKind.WordTitle : PanelKind.WordValue, Cx = FirstX + Pitch * i, Cy = RowY[title ? 5 : 6],
                        W = BitW, H = title ? TitleH : ValueH, D = d, Input = block == 0,
                        Lines = title ? new[] { "D" + d.ToString("000"), label } : new string[0],
                    });
                }
            cells.Add(Widget(PanelKind.Button, Metal, -577, 465, 160, 50)); // top left, beside the bit grid
            cells.Add(Widget(PanelKind.Button, Nonmetal, -577, 410, 160, 50));
            cells.Add(Widget(PanelKind.Button, Connect, -380, -470, 160, 50));            cells.Add(Widget(PanelKind.Button, Disconnect, -210, -470, 160, 50));
            // Ours (not in the reference; masked in the screenshot comparison). OQ-5: Panel toggle right of Disconnect.
            cells.Add(Widget(PanelKind.Extra, Reset, -577, 355, 160, 50)); // below Nonmetal
            cells.Add(Widget(PanelKind.Extra, PanelToggle, -40, -470, 160, 50));
            for (int i = 0; i < Views.Length; i++) cells.Add(Widget(PanelKind.Extra, Views[i], 70 + 54 * i, -470, 50, 30)); // view presets
            cells.Add(Widget(PanelKind.Extra, Transport, 345, -470, 160, 50));
            cells.Add(Widget(PanelKind.Extra, Ip, 515, -470, 160, 30));
            cells.Add(Widget(PanelKind.Extra, Port, 645, -470, 80, 30));
            cells.Add(Widget(PanelKind.Extra, StateLine, 0, -425, 860, 24));
            return cells;
        }

        static PanelCell Widget(PanelKind kind, string name, int cx, int cy, int w, int h) =>
            new PanelCell { Kind = kind, Cx = cx, Cy = cy, W = w, H = h, Lines = new[] { name } };
    }
}
