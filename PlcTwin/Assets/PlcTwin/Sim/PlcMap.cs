namespace PlcTwin.Sim
{
    /// <summary>
    /// Bit indices of the reference D map (ref §1.5). Index = word offset from the block base * 16 + bit, so 0x13 = D1.3.
    /// In = sim → PLC (D0-D3 bits, D40-D44 words); Out = PLC → sim (D100-D103 bits, D140-D146 words).
    /// </summary>
    public static class PlcMap
    {
        public static class In
        {
            public const int SupplyFwd = 0x00, SupplyBwd = 0x01, EmitFwd = 0x02, EmitBwd = 0x03, DrillDown = 0x04, DrillUp = 0x05,
                DischargeFwd = 0x06, DischargeBwd = 0x07, StopperDown = 0x08, StopperUp = 0x09, LiftFwd = 0x0A, LiftBwd = 0x0B,
                PadOn = 0x0C, StockerFwd = 0x0D, StockerBwd = 0x0E, SupplySensor = 0x0F, Nonmetal = 0x11, // D1.0 (0x10, emit sensor) is not simulated
                Metal = 0x12, StopperDetect = 0x13, DriverReady = 0x20, AxisError = 0x28, AxisBusy = 0x2C, StartDone = 0x30, PosDone = 0x34;
            /// <summary>Word offsets from the inWords base (D40).</summary>
            public const int Position = 0, ErrorNo = 2, InputSignal = 3, Status = 4;
        }

        public static class Out
        {
            public const int SupplyFwd = 0x00, SupplyBwd = 0x01, EmitFwd = 0x02, EmitBwd = 0x03, DrillDown = 0x04, DischargeFwd = 0x05,
                DischargeBwd = 0x06, StopperDown = 0x07, LiftFwd = 0x08, LiftBwd = 0x09, Vacuum = 0x0A, StockerFwd = 0x0B,
                StockerBwd = 0x0C, DrillOn = 0x0D, Conveyor = 0x0E, PlcReady = 0x20, ServoOn = 0x21, Stop = 0x24, JogPlus = 0x28,
                JogMinus = 0x2C, Run = 0x30;
            /// <summary>Word offsets from the outWords base (D140). D141/D145 are read but unused.</summary>
            public const int Speed = 0, Command = 2, ErrorClear = 3, Target = 4, JogSpeed = 6;
        }

        public static bool Get(short[] words, int bit) => (words[bit >> 4] >> (bit & 15) & 1) != 0;

        public static void Set(short[] words, int bit, bool on)
        {
            int mask = 1 << (bit & 15);
            words[bit >> 4] = (short)(on ? words[bit >> 4] | mask : words[bit >> 4] & ~mask);
        }
    }
}
