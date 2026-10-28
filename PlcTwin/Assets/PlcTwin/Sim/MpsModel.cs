using System;
using System.Collections.Generic;
using static PlcTwin.Sim.MpsGeometry;
using In = PlcTwin.Sim.PlcMap.In;
using Out = PlcTwin.Sim.PlcMap.Out;

namespace PlcTwin.Sim
{
    public enum Cyl { Supply, Emit, Drill, Discharge, Stopper, Lift, Stocker }

    /// <summary>3D switches (ref §4). Momentary; they drive the line only while offline. SuctionPad and WorkDrill do nothing.</summary>
    public enum SwitchId
    {
        SupplyFwd, SupplyBwd, EmitFwd, EmitBwd, DrillDown, DischargeFwd, DischargeBwd, StopperDown, Conveyor,
        LiftFwd, LiftBwd, ServoUp, ServoDown, StockerFwd, StockerBwd, SuctionPad, WorkDrill
    }

    /// <summary>Magazine stack, work table (supply/emit path), belt, vacuum pad, stocker shelf, base plate, end box, discharge box.</summary>
    public enum ChipPlace { Magazine, Table, Belt, Held, Shelf, Floor, Box1, Box2 }

    public sealed class Chip
    {
        public int Id { get; internal set; }
        public bool Metal { get; internal set; }
        public ChipPlace Place { get; internal set; }
        /// <summary>Center in µm, MPS-local.</summary>
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Z { get; internal set; }
        /// <summary>Shelf index 0..2 while Place == Shelf.</summary>
        public int Shelf { get; internal set; }
    }

    /// <summary>
    /// The whole MPS line as a deterministic kinematic model (plan Step 4): fixed 10 ms substeps from an integer-µs
    /// accumulator, integer µm positions, rules instead of physics. Commands come from the PLC words while Online, else from
    /// the 3D switches. Unity only renders this state.
    /// </summary>
    public sealed class MpsModel
    {
        sealed class Cylinder
        {
            public readonly int Ret, Ext, FwdLamp, BwdLamp;
            readonly bool single, strict; // strict: the reference compares with > / < (supply, drill, stopper)
            public int Pos, Moved; // Moved: signed displacement of the last substep
            public bool Extended;

            public Cylinder(int ret, int ext, int fwdLamp, int bwdLamp, bool singleActing, bool strictLamps)
            {
                Ret = ret; Ext = ext; FwdLamp = fwdLamp; BwdLamp = bwdLamp; single = singleActing; strict = strictLamps; Pos = ret;
            }

            int Dir => Math.Sign(Ext - Ret);
            public bool FwdOn => strict ? (Pos - FwdLamp) * Dir > 0 : (Pos - FwdLamp) * Dir >= 0;
            public bool BwdOn => strict ? (Pos - BwdLamp) * Dir < 0 : (Pos - BwdLamp) * Dir <= 0;
            public bool Extending => Moved * Dir > 0;

            // Double-acting: level latch, backward wins (equivalent to the reference edge logic). Single-acting: follows fwd.
            public void Step(bool fwd, bool bwd)
            {
                if (single) Extended = fwd;
                else if (bwd) Extended = false;
                else if (fwd) Extended = true;
                Moved = Math.Max(-CylinderStepUm, Math.Min(CylinderStepUm, (Extended ? Ext : Ret) - Pos));
                Pos += Moved;
            }
        }

        readonly Cylinder[] cyl =
        {
            new Cylinder(SupplyRet, SupplyExt, SupplyFwdLamp, SupplyBwdLamp, false, true),
            new Cylinder(EmitRet, EmitExt, EmitFwdLamp, EmitBwdLamp, false, false),
            new Cylinder(DrillUp, DrillDown, DrillDownLamp, DrillUpLamp, true, true),
            new Cylinder(DischargeRet, DischargeExt, DischargeFwdLamp, DischargeBwdLamp, false, false),
            new Cylinder(StopperDown, StopperUp, StopperUpLamp, StopperDownLamp, true, true), // rest = down; the command raises it
            new Cylinder(LiftRet, LiftExt, LiftFwdLamp, LiftBwdLamp, false, false),
            new Cylinder(StockerRet, StockerExt, StockerFwdLamp, StockerBwdLamp, false, false),
        };
        readonly bool[] sw = new bool[Enum.GetValues(typeof(SwitchId)).Length];
        readonly short[] cmdBits = new short[4], cmdWords = new short[7]; // effective PLC commands; all 0 while offline
        readonly short[] lastBits = new short[4], lastWords = new short[7]; // last decoded words, kept offline for the panel
        readonly List<Chip> chips = new List<Chip>();
        Chip held;
        bool slotFilled; // a chip sits in the magazine's bottom slot (D0.F)
        int nextId = 1;
        long accUs;

        public bool Online { get; private set; }
        public long SubstepCount { get; private set; }
        public int ServoZ { get; private set; } = ServoStartUm;
        public bool ConveyorOn { get; private set; }
        // Servo tower lamps (display only, no PLC bit): home = D40 140 (the start position), top/bottom = the jog limits.
        public bool ServoHome => D40 == (ServoStartUm + ServoOffsetUm) / 1000;
        public bool ServoTop => ServoZ >= ServoMaxUm;
        public bool ServoBottom => ServoZ <= ServoMinUm;
        public IReadOnlyList<Chip> Chips => chips;
        public short D40 => (short)((ServoZ + ServoOffsetUm) / 1000); // truncates toward zero, like the reference

        public int CylinderPos(Cyl c) => cyl[(int)c].Pos;
        public bool Switch(SwitchId s) => sw[(int)s];
        public void SetSwitch(SwitchId s, bool on) => sw[(int)s] = on;

        /// <summary>Last decoded PLC words (D100-D103, D140-D146); kept while offline (panel only).</summary>
        public void CopyLastCommands(short[] bits4, short[] words7)
        {
            Array.Copy(lastBits, bits4, 4);
            Array.Copy(lastWords, words7, 7);
        }

        /// <summary>
        /// Going offline (Disconnect, or any poller state other than Connected, OQ-2) clears the PLC commands (servo jog/Run
        /// stop at once, no Q6 bug), turns the vacuum off and drops a held chip. The 3D switches drive the line again.
        /// </summary>
        public void SetOnline(bool on)
        {
            if (Online && !on)
            {
                Array.Clear(cmdBits, 0, 4);
                Array.Clear(cmdWords, 0, 7);
                Release();
            }
            Online = on;
        }

        /// <summary>Decoded D100-D103 (bits) and D140-D146 (words). Effective only while Online.</summary>
        public void SetCommands(short[] outBits4, short[] outWords7)
        {
            Array.Copy(outBits4, lastBits, 4);
            Array.Copy(outWords7, lastWords, 7);
            if (!Online) return;
            Array.Copy(outBits4, cmdBits, 4);
            Array.Copy(outWords7, cmdWords, 7);
        }

        /// <summary>Spawns a chip at the magazine top (no limit, ref §2.8). Returns its id.</summary>
        public int AddChip(bool metal)
        {
            int n = chips.FindAll(c => c.Place == ChipPlace.Magazine).Count;
            chips.Add(new Chip { Id = nextId, Metal = metal, Place = ChipPlace.Magazine, X = MagX, Y = MagY, Z = TableChipZ + n * 2 * ChipHalfT });
            return nextId++;
        }

        /// <summary>Click removal. Chips inside the magazine cannot be clicked (its walls hide them, ref §2.8).</summary>
        public bool RemoveChip(int id)
        {
            var c = chips.Find(x => x.Id == id);
            if (c == null || c.Place == ChipPlace.Magazine) return false;
            if (c == held) held = null;
            return chips.Remove(c);
        }

        /// <summary>Runs whole 10 ms substeps for dt, carrying the remainder in integer µs.</summary>
        public void Step(double dt)
        {
            if (!(dt > 0)) return;
            accUs += (long)Math.Round(dt * 1e6);
            while (accUs >= SubstepUs)
            {
                Substep();
                accUs -= SubstepUs;
            }
        }

        bool C(int outBit) => PlcMap.Get(cmdBits, outBit);
        bool Cmd(SwitchId s, int outBit) => Online ? C(outBit) : sw[(int)s];

        void Substep()
        {
            SubstepCount++;
            cyl[(int)Cyl.Supply].Step(Cmd(SwitchId.SupplyFwd, Out.SupplyFwd), Cmd(SwitchId.SupplyBwd, Out.SupplyBwd));
            cyl[(int)Cyl.Emit].Step(Cmd(SwitchId.EmitFwd, Out.EmitFwd), Cmd(SwitchId.EmitBwd, Out.EmitBwd));
            cyl[(int)Cyl.Drill].Step(Cmd(SwitchId.DrillDown, Out.DrillDown), false);
            cyl[(int)Cyl.Discharge].Step(Cmd(SwitchId.DischargeFwd, Out.DischargeFwd), Cmd(SwitchId.DischargeBwd, Out.DischargeBwd));
            cyl[(int)Cyl.Stopper].Step(Cmd(SwitchId.StopperDown, Out.StopperDown), false);
            cyl[(int)Cyl.Lift].Step(Cmd(SwitchId.LiftFwd, Out.LiftFwd), Cmd(SwitchId.LiftBwd, Out.LiftBwd));
            cyl[(int)Cyl.Stocker].Step(Cmd(SwitchId.StockerFwd, Out.StockerFwd), Cmd(SwitchId.StockerBwd, Out.StockerBwd));
            ConveyorOn = Cmd(SwitchId.Conveyor, Out.Conveyor);
            Servo();
            Transfers();
            Conveyor();
            Vacuum();
        }

        // Ref §3 in integer µm with one-way jog limits and a Run step that lands on the target (OQ-11).
        void Servo()
        {
            if (!Online)
            {
                Jog(sw[(int)SwitchId.ServoUp], sw[(int)SwitchId.ServoDown], ServoSwitchStepUm);
                return;
            }
            if (C(Out.Stop)) return; // OQ-3 deviation: STOP halts jog and Run while ON
            bool jogP = C(Out.JogPlus), jogN = C(Out.JogMinus);
            Jog(jogP, jogN, ServoJogUmPerUnit * cmdWords[Out.JogSpeed]); // moves regardless of SERVO ON (reference)
            bool jogBlock = C(Out.ServoOn) && (jogP || jogN); // OQ-12: reference condition kept
            int run = ServoJogUmPerUnit * cmdWords[Out.Speed];
            if (!jogBlock && C(Out.Run) && cmdWords[Out.Command] == RunCommand && run > 0)
                ServoZ += Math.Max(-run, Math.Min(run, (cmdWords[Out.Target] - 50) * 1000 - ServoZ)); // no soft limit (reference)
        }

        // Reference precedence: up only while below the upper limit, else down (so at the top with both ON it goes down).
        // One-way limits: a limit stops motion past it but never pulls back a position already outside it.
        void Jog(bool up, bool down, int step)
        {
            if (up && ServoZ < ServoMaxUm) ServoZ = Math.Max(ServoZ, Math.Min(ServoZ + step, ServoMaxUm));
            else if (down) ServoZ = Math.Min(ServoZ, Math.Max(ServoZ - step, ServoMinUm));
        }

        void Transfers()
        {
            var supply = cyl[(int)Cyl.Supply];
            if (supply.Pos - supply.Moved == SupplyRet) // gripper was back at the start of this substep: the stack drops
                slotFilled = chips.Exists(c => c.Place == ChipPlace.Magazine);
            int z = TableChipZ;
            foreach (var c in chips)
                if (c.Place == ChipPlace.Magazine) { c.Z = z; z += 2 * ChipHalfT; }

            if (supply.Extending)
            {
                // OQ-13: a push into an occupied stage is blocked; the chip stays in the slot, the rod still extends.
                if (slotFilled && !chips.Exists(c => c.Place == ChipPlace.Table))
                {
                    var b = chips.Find(c => c.Place == ChipPlace.Magazine);
                    b.Place = ChipPlace.Table;
                    slotFilled = false;
                }
                int gx = MagX + (supply.Pos - SupplyRet); // gripper pushes, never pulls
                foreach (var c in chips)
                    if (c.Place == ChipPlace.Table && c.Y == MagY) c.X = Math.Max(c.X, gx);
            }

            var emit = cyl[(int)Cyl.Emit];
            if (emit.Extending)
            {
                int gy = StageY + (emit.Pos - EmitRet);
                foreach (var c in chips)
                {
                    if (c.Place != ChipPlace.Table || c.X <= StageX - ChipR) continue;
                    c.Y = Math.Max(c.Y, gy);
                    if (c.Y >= BeltEntryY) c.Place = ChipPlace.Belt; // stage x/z = belt x/z: no snap
                }
            }

            var dis = cyl[(int)Cyl.Discharge];
            if (dis.Extending)
            {
                int gx = BeltX + (dis.Pos - DischargeRet);
                foreach (var c in chips)
                {
                    if (c.Place != ChipPlace.Belt || Math.Abs(c.Y - DischargeRootY) > DischargeCaptureUm) continue;
                    c.X = Math.Min(c.X, gx);
                    if (c.X < BeltX - BeltHalfW) { c.Place = ChipPlace.Box2; c.X = Box2X; c.Y = Box2Y; c.Z = FloorChipZ; }
                }
            }
        }

        // OQ-4: every belt chip moves; a chip keeps QueueSpacingUm behind the one ahead and stops at StopY unless the stopper
        // is fully down (its rest position; the command raises it). A chip half pushed by the discharge gripper (X != BeltX) still rides the belt.
        void Conveyor()
        {
            if (!ConveyorOn) return;
            var belt = chips.FindAll(c => c.Place == ChipPlace.Belt);
            belt.Sort((a, b) => b.Y.CompareTo(a.Y)); // front first
            bool blocked = !cyl[(int)Cyl.Stopper].BwdOn; // anything but fully down blocks
            int ahead = int.MaxValue;
            foreach (var c in belt)
            {
                int limit = ahead == int.MaxValue ? BeltEndY : Math.Min(BeltEndY, ahead - QueueSpacingUm);
                if (blocked && c.Y <= StopY) limit = Math.Min(limit, StopY);
                c.Y = Math.Max(c.Y, Math.Min(c.Y + ConveyorStepUm, limit));
                if (c.Y >= BeltEndY) { c.Place = ChipPlace.Box1; c.X = Box1X; c.Y = Box1Y; c.Z = FloorChipZ; continue; }
                ahead = c.Y;
            }
        }

        (int x, int y, int z) Pad() => (LiftRootX + cyl[(int)Cyl.Lift].Pos + PadDx, LiftRootY + PadDy, ServoZ + PadDz - ChipHalfT);
        (int x, int y, int z) Slot(int k) => (StockerRootX + SlotDx, StockerRootY + cyl[(int)Cyl.Stocker].Pos + SlotDy, StockerZ + ShelfZ[k] + ChipHalfT);

        static bool Near(Chip c, (int x, int y, int z) p) =>
            Math.Abs(c.X - p.x) <= PickTolUm && Math.Abs(c.Y - p.y) <= PickTolUm && Math.Abs(c.Z - p.z) <= PickTolUm;

        static void MoveTo(Chip c, (int x, int y, int z) p) { c.X = p.x; c.Y = p.y; c.Z = p.z; }

        // Online only (offline the vacuum is forced off, ref §1.6).
        void Vacuum()
        {
            bool vac = Online && C(Out.Vacuum);
            if (!vac) Release();
            var pad = Pad();
            if (vac && held == null)
            {
                held = chips.Find(c => (c.Place == ChipPlace.Table || c.Place == ChipPlace.Belt || c.Place == ChipPlace.Floor
                                        || c.Place == ChipPlace.Shelf) && Near(c, pad));
                if (held != null) held.Place = ChipPlace.Held;
            }
            if (held != null) MoveTo(held, pad);
            foreach (var c in chips)
                if (c.Place == ChipPlace.Shelf) MoveTo(c, Slot(c.Shelf)); // shelf chips ride the stocker
        }

        // Vacuum off: onto the shelf slot under the chip (within tolerance), else onto the belt if above it, else the base plate.
        void Release()
        {
            var c = held;
            if (c == null) return;
            held = null;
            for (int k = 0; k < ShelfZ.Length; k++)
                if (Near(c, Slot(k))) { c.Place = ChipPlace.Shelf; c.Shelf = k; MoveTo(c, Slot(k)); return; }
            if (Math.Abs(c.X - BeltX) <= BeltHalfW && c.Y >= BeltMinY && c.Y < BeltEndY) { c.Place = ChipPlace.Belt; c.X = BeltX; c.Z = BeltChipZ; }
            else { c.Place = ChipPlace.Floor; c.Z = FloorChipZ; }
        }

        /// <summary>Builds the D0-D3 bits and D40-D44 words from the current state (whole words, ref §1.5).</summary>
        public void ComposeInputs(short[] inBits4, short[] inWords5)
        {
            Array.Clear(inBits4, 0, 4);
            Array.Clear(inWords5, 0, 5);
            void S(int bit, bool on) => PlcMap.Set(inBits4, bit, on);
            void Lamps(Cyl c, int fwdBit, int bwdBit) { S(fwdBit, cyl[(int)c].FwdOn); S(bwdBit, cyl[(int)c].BwdOn); }
            Lamps(Cyl.Supply, In.SupplyFwd, In.SupplyBwd);
            Lamps(Cyl.Emit, In.EmitFwd, In.EmitBwd);
            Lamps(Cyl.Drill, In.DrillDown, In.DrillUp);
            Lamps(Cyl.Discharge, In.DischargeFwd, In.DischargeBwd);
            Lamps(Cyl.Stopper, In.StopperUp, In.StopperDown);
            Lamps(Cyl.Lift, In.LiftFwd, In.LiftBwd);
            Lamps(Cyl.Stocker, In.StockerFwd, In.StockerBwd);
            S(In.PadOn, held != null);
            S(In.SupplySensor, slotFilled);
            foreach (var c in chips)
            {
                if (c.Place != ChipPlace.Belt) continue;
                if (Math.Abs(c.Y - SorterY) < SorterHalfY + ChipR) { S(In.Nonmetal, true); if (c.Metal) S(In.Metal, true); }
                if (Math.Abs(c.Y - StopY) <= StopDetectTolUm) S(In.StopperDetect, true);
            }
            // Servo status (ref §3). Offline every command is 0, so these are 0. ERROR, D42-D44 stay 0.
            bool run = C(Out.Run);
            S(In.DriverReady, C(Out.PlcReady));
            S(In.AxisBusy, C(Out.ServoOn) && (C(Out.JogPlus) || C(Out.JogMinus) || run));
            bool done = run && D40 == cmdWords[Out.Target];
            S(In.StartDone, done);
            S(In.PosDone, done);
            inWords5[In.Position] = D40;
        }
    }
}
