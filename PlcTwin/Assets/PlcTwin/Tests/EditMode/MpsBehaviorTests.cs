using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using PlcTwin.Sim;
using static PlcTwin.Sim.MpsGeometry;
using In = PlcTwin.Sim.PlcMap.In;
using Out = PlcTwin.Sim.PlcMap.Out;

namespace PlcTwin.Tests
{
    /// <summary>Behavior table (AC1-AC6) on the pure-C# model, fixed 10 ms substeps, simulated time only.</summary>
    public class MpsBehaviorTests
    {
        MpsModel m;
        short[] ob, ow; // PLC outputs D100-D103 / D140-D146 as the poller would decode them

        [SetUp]
        public void SetUp()
        {
            m = new MpsModel();
            ob = new short[4];
            ow = new short[7];
        }

        bool I(int bit)
        {
            var b = new short[4];
            m.ComposeInputs(b, new short[5]);
            return PlcMap.Get(b, bit);
        }

        void Online() { m.SetOnline(true); m.SetCommands(ob, ow); }
        void O(int bit, bool on) { PlcMap.Set(ob, bit, on); m.SetCommands(ob, ow); }
        void W(int offset, int v) { ow[offset] = (short)v; m.SetCommands(ob, ow); }

        void Hold(SwitchId s, double sec) { m.SetSwitch(s, true); m.Step(sec); m.SetSwitch(s, false); }
        Chip C(int id) => m.Chips.First(c => c.Id == id);

        /// <summary>Seconds (substep-exact) until cond holds, or -1 within maxSec.</summary>
        double Until(Func<bool> cond, double maxSec)
        {
            for (int i = 1; i <= (int)Math.Round(maxSec * 100); i++)
            {
                m.Step(0.01);
                if (cond()) return i * 0.01;
            }
            return -1;
        }

        // Offline switch sequence: magazine -> stage -> belt start, both grippers back.
        int ChipOnBelt(bool metal)
        {
            int id = m.AddChip(metal);
            Hold(SwitchId.SupplyFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.SupplyBwd, 0.1); m.Step(2.0);
            Hold(SwitchId.EmitFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.EmitBwd, 0.1); m.Step(2.0);
            Assert.AreEqual(ChipPlace.Belt, C(id).Place, "chip did not reach the belt");
            Assert.AreEqual(BeltEntryY, C(id).Y);
            return id;
        }

        // Online: chip from the belt start to the raised stopper (pickup position, calibrated), servo to D40 = 0, vacuum on.
        int ChipHeldAtPad()
        {
            int id = ChipOnBelt(true);
            Online();
            O(Out.StopperDown, true); // D100.7 raises the stopper (rest = down)
            O(Out.Conveyor, true);
            Assert.Greater(Until(() => C(id).Y == StopY, 10), 0, "chip never reached the stopper");
            Assert.IsTrue(I(In.StopperDetect), "D1.3 while the chip waits at the stopper");
            O(Out.Conveyor, false);
            W(Out.Command, RunCommand); W(Out.Target, 0); W(Out.Speed, 100); O(Out.Run, true);
            Assert.Greater(Until(() => m.D40 == 0, 5), 0, "servo never reached D40 = 0");
            O(Out.Run, false);
            Assert.IsFalse(I(In.PadOn));
            O(Out.Vacuum, true);
            Assert.Greater(Until(() => I(In.PadOn), 0.05), 0, "vacuum did not pick the chip");
            Assert.AreEqual(ChipPlace.Held, C(id).Place);
            return id;
        }

        // ---------------- AC3 initial state ----------------

        [Test]
        public void Initial()
        {
            var b = new short[4];
            var w = new short[5];
            m.ComposeInputs(b, w);
            var on = new[] { In.SupplyBwd, In.EmitBwd, In.DrillUp, In.DischargeBwd, In.StopperDown, In.LiftBwd, In.StockerBwd };
            for (int bit = 0; bit < 64; bit++)
                Assert.AreEqual(on.Contains(bit), PlcMap.Get(b, bit), "D" + (bit >> 4) + "." + (bit & 15).ToString("X"));
            CollectionAssert.AreEqual(new short[] { 140, 0, 0, 0, 0 }, w);
            Assert.AreEqual(90000, m.ServoZ);
            Assert.IsFalse(m.Online);
        }

        // ---------------- AC2 cylinders ----------------

        // outBwd < 0: single-acting (drill, stopper). Times from command to lamp ON; return measured after a full stroke.
        [TestCase(Out.SupplyFwd, Out.SupplyBwd, In.SupplyFwd, In.SupplyBwd, 1.70, 1.70, TestName = "Cylinders_Supply")]
        [TestCase(Out.EmitFwd, Out.EmitBwd, In.EmitFwd, In.EmitBwd, 1.70, 1.70, TestName = "Cylinders_Emit")]
        [TestCase(Out.DrillDown, -1, In.DrillDown, In.DrillUp, 1.40, 1.40, TestName = "Cylinders_Drill")]
        [TestCase(Out.DischargeFwd, Out.DischargeBwd, In.DischargeFwd, In.DischargeBwd, 1.70, 2.00, TestName = "Cylinders_Discharge")]
        [TestCase(Out.StopperDown, -1, In.StopperUp, In.StopperDown, 0.50, 0.50, TestName = "Cylinders_Stopper")] // D100.7 raises it; rest = down
        [TestCase(Out.LiftFwd, Out.LiftBwd, In.LiftFwd, In.LiftBwd, 1.40, 1.40, TestName = "Cylinders_SuctionPlate")]
        [TestCase(Out.StockerFwd, Out.StockerBwd, In.StockerFwd, In.StockerBwd, 1.00, 1.00, TestName = "Cylinders_Stocker")]
        public void Cylinders(int outFwd, int outBwd, int inFwd, int inBwd, double fwdSec, double bwdSec)
        {
            Online();
            Assert.IsTrue(I(inBwd));
            Assert.IsFalse(I(inFwd));
            O(outFwd, true);
            int half = (int)Math.Round(fwdSec * 50);
            double t = -1;
            for (int i = 1; i <= 500 && t < 0; i++)
            {
                m.Step(0.01);
                if (i == half) Assert.IsFalse(I(inFwd) || I(inBwd), "a lamp is ON mid-stroke");
                if (I(inFwd)) t = i * 0.01;
            }
            Assert.AreEqual(fwdSec, t, 0.03, "forward lamp");
            Assert.IsFalse(I(inBwd));
            m.Step(1.0); // to the stroke end
            O(outFwd, false);
            if (outBwd >= 0)
            {
                m.Step(1.0);
                Assert.IsTrue(I(inFwd), "double-acting cylinder must hold after the command drops");
                O(outBwd, true);
            }
            Assert.AreEqual(bwdSec, Until(() => I(inBwd), 5), 0.03, "backward lamp after the return stroke");
            Assert.IsFalse(I(inFwd));
        }

        [Test]
        public void Cylinders_DischargeStrokeEnd2s_FwdLampOff030IntoReturn()
        {
            Online();
            O(Out.DischargeFwd, true);
            Assert.AreEqual(2.00, Until(() => m.CylinderPos(Cyl.Discharge) == DischargeExt, 5), 0.03, "stroke end");
            O(Out.DischargeFwd, false);
            O(Out.DischargeBwd, true);
            Assert.AreEqual(0.30, Until(() => !I(In.DischargeFwd), 5), 0.03);
        }

        [Test]
        public void Cylinders_BackwardWinsWhenBothOn()
        {
            Online();
            O(Out.SupplyFwd, true);
            O(Out.SupplyBwd, true);
            m.Step(2.0);
            Assert.AreEqual(SupplyRet, m.CylinderPos(Cyl.Supply));
            O(Out.SupplyBwd, false);
            m.Step(2.0);
            Assert.IsTrue(I(In.SupplyFwd));
            O(Out.SupplyBwd, true); // fwd still ON
            m.Step(2.0);
            Assert.IsTrue(I(In.SupplyBwd));
        }

        [Test]
        public void Switch_SupplyForwardHeld07s_LatchesToFullStroke()
        {
            Hold(SwitchId.SupplyFwd, 0.7); // observed in the reference: released early, still goes all the way
            Assert.AreEqual(1.70 - 0.7, Until(() => I(In.SupplyFwd), 3), 0.03);
            m.Step(2.0);
            Assert.AreEqual(SupplyExt, m.CylinderPos(Cyl.Supply));
        }

        // ---------------- AC4 chips ----------------

        [Test]
        public void ChipFlow_MetalInMagazine_D0FWithin05s()
        {
            Assert.IsFalse(I(In.SupplySensor));
            m.AddChip(true);
            double t = Until(() => I(In.SupplySensor), 0.5);
            Assert.Greater(t, 0);
            Assert.LessOrEqual(t, 0.5);
        }

        [Test]
        public void ChipFlow_SupplyToStage_DrillNoEffect_EmitToBelt()
        {
            int id = m.AddChip(true);
            Hold(SwitchId.SupplyFwd, 0.7);
            m.Step(2.5 - 0.7); // screenshot 03 timing
            Assert.IsFalse(I(In.SupplySensor), "D0.F");
            Assert.AreEqual((ChipPlace.Table, StageX, StageY), (C(id).Place, C(id).X, C(id).Y));

            m.SetSwitch(SwitchId.DrillDown, true);
            m.Step(1.5);
            Assert.IsTrue(I(In.DrillDown), "drill at the bottom while held (screenshot 04)");
            Assert.AreEqual((ChipPlace.Table, StageX, StageY), (C(id).Place, C(id).X, C(id).Y), "drill moved the chip");
            m.SetSwitch(SwitchId.DrillDown, false);
            m.Step(1.5);

            Hold(SwitchId.EmitFwd, 0.2);
            m.Step(2.0 - 0.2); // screenshot 05 timing
            Assert.AreEqual(ChipPlace.Belt, C(id).Place);
            Assert.AreEqual((BeltX, BeltEntryY), (C(id).X, C(id).Y));
        }

        // L4 fix: the emit push line is collinear with the belt, so the chip slides from the stage onto the belt with constant
        // x and z (no snap) and the running belt carries it on.
        [Test]
        public void ChipFlow_EmitPush_ConstantX_NoJump_LandsOnMovingBelt()
        {
            int id = m.AddChip(false);
            Hold(SwitchId.SupplyFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.SupplyBwd, 0.1); m.Step(2.0);
            Assert.AreEqual((ChipPlace.Table, StageX, StageY), (C(id).Place, C(id).X, C(id).Y));
            m.SetSwitch(SwitchId.Conveyor, true);
            m.SetSwitch(SwitchId.EmitFwd, true);
            int x0 = C(id).X, z0 = C(id).Z, py = C(id).Y, onBelt = 0;
            for (int i = 0; i < 300; i++)
            {
                m.Step(0.01);
                var c = C(id);
                Assert.AreEqual((x0, z0), (c.X, c.Z), "x/z changed at substep " + i);
                // y: gripper 0.5 mm or belt 1 mm per substep; both on the hand-off substep (1.5 mm), never a teleport.
                Assert.That(c.Y - py, Is.InRange(0, CylinderStepUm + ConveyorStepUm), "y jump at substep " + i);
                if (c.Place == ChipPlace.Belt) onBelt++;
                py = c.Y;
            }
            Assert.AreEqual(BeltX, x0);
            Assert.AreEqual(ChipPlace.Belt, C(id).Place);
            Assert.Greater(onBelt, 100);
            Assert.Greater(C(id).Y, BeltEntryY + 50000, "the belt carries the chip on");
        }

        [Test]
        public void ChipFlow_SupplyIntoOccupiedStage_Blocked()
        {
            int a = m.AddChip(true), b = m.AddChip(false);
            Hold(SwitchId.SupplyFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.SupplyBwd, 0.1); m.Step(2.0);
            Assert.AreEqual(ChipPlace.Table, C(a).Place);
            Assert.IsTrue(I(In.SupplySensor), "second chip dropped into the slot");

            Hold(SwitchId.SupplyFwd, 0.1); m.Step(2.0);
            Assert.IsTrue(I(In.SupplyFwd), "the rod still extends");
            Assert.AreEqual(ChipPlace.Magazine, C(b).Place, "push into an occupied stage must be blocked");
            Assert.IsTrue(I(In.SupplySensor));
            Assert.AreEqual((StageX, StageY), (C(a).X, C(a).Y));

            // Negative control: with the stage cleared the same push moves the chip.
            Hold(SwitchId.SupplyBwd, 0.1); m.Step(2.0);
            Hold(SwitchId.EmitFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.EmitBwd, 0.1); m.Step(2.0);
            Hold(SwitchId.SupplyFwd, 0.1); m.Step(2.0);
            Assert.AreEqual((ChipPlace.Table, StageX), (C(b).Place, C(b).X));
        }

        [Test]
        public void ChipFlow_ConveyorSpeed010()
        {
            int id = ChipOnBelt(false);
            m.SetSwitch(SwitchId.Conveyor, true);
            m.Step(1.0);
            Assert.AreEqual(100000, C(id).Y - BeltEntryY, 2000);
            Assert.IsTrue(m.ConveyorOn);
            m.SetSwitch(SwitchId.Conveyor, false);
            int y = C(id).Y;
            m.Step(1.0);
            Assert.AreEqual(y, C(id).Y, "belt must stop with the command");
        }

        [TestCase(false, TestName = "ChipFlow_Sorter_Nonmetal")]
        [TestCase(true, TestName = "ChipFlow_Sorter_Metal")]
        public void ChipFlow_Sorter(bool metal)
        {
            ChipOnBelt(metal);
            m.SetSwitch(SwitchId.Conveyor, true);
            int nonmetalOn = 0, metalOn = 0;
            for (int i = 0; i < 600; i++)
            {
                m.Step(0.01);
                bool n = I(In.Nonmetal), me = I(In.Metal);
                if (n) nonmetalOn++;
                if (me) metalOn++;
                if (me) Assert.IsTrue(n, "D1.2 without D1.1");
            }
            Assert.AreEqual(1.0, nonmetalOn * 0.01, 0.1, "D1.1 duration");
            Assert.AreEqual(metal ? nonmetalOn : 0, metalOn, "D1.2");
        }

        [Test]
        public void ChipFlow_StopperRaised_ChipWaitsWithD13_StopperReleased_Passes()
        {
            int id = ChipOnBelt(true);
            m.SetSwitch(SwitchId.StopperDown, true); // raised
            m.SetSwitch(SwitchId.Conveyor, true);
            m.Step(8.0);
            Assert.AreEqual(StopY, C(id).Y, "chip must stop at the raised stopper (decided fix)");
            Assert.IsTrue(I(In.StopperDetect), "D1.3 while waiting");
            m.Step(2.0);
            Assert.AreEqual(StopY, C(id).Y);

            m.SetSwitch(SwitchId.StopperDown, false); // released: back down
            double off = Until(() => !I(In.StopperDetect), 2);
            Assert.Greater(off, 0, "D1.3 must go OFF once the chip passes");
            Assert.IsTrue(I(In.StopperDown));
            Assert.Greater(C(id).Y, StopY);
        }

        [Test]
        public void ChipFlow_D13Window5mm()
        {
            int id = ChipOnBelt(true);
            m.SetSwitch(SwitchId.Conveyor, true);
            var ys = new List<int>();
            for (int i = 0; i < 800; i++)
            {
                m.Step(0.01);
                if (I(In.StopperDetect)) ys.Add(C(id).Y);
            }
            Assert.IsNotEmpty(ys); // belt positions move in 1 mm steps, so the window edges are hit within one step
            Assert.That(ys.Min(), Is.InRange(StopY - StopDetectTolUm, StopY - StopDetectTolUm + ConveyorStepUm));
            Assert.That(ys.Max(), Is.InRange(StopY + StopDetectTolUm - ConveyorStepUm, StopY + StopDetectTolUm));
        }

        [Test]
        public void ChipFlow_BeltEndToBox1()
        {
            int id = ChipOnBelt(true);
            m.SetSwitch(SwitchId.Conveyor, true); // stopper rests down: the chip rides to the end
            Assert.Greater(Until(() => C(id).Place == ChipPlace.Box1, 12), 0);
            Assert.AreEqual((Box1X, Box1Y), (C(id).X, C(id).Y));
        }

        [Test]
        public void ChipFlow_DischargeWithinCaptureWidth_ToBox2()
        {
            int id = ChipOnBelt(true);
            Hold(SwitchId.Conveyor, 3.1); // y = -6500, 0.5 mm from the discharge axis
            Hold(SwitchId.DischargeFwd, 0.1);
            Assert.Greater(Until(() => C(id).Place == ChipPlace.Box2, 2.5), 0);
            Assert.AreEqual((Box2X, Box2Y), (C(id).X, C(id).Y));
        }

        [Test]
        public void ChipFlow_DischargeOutsideCaptureWidth_ChipStays()
        {
            int id = ChipOnBelt(true);
            Hold(SwitchId.Conveyor, 2.79); // y = -37500: 31.5 mm from the axis, just outside ±30 mm
            Hold(SwitchId.DischargeFwd, 0.1);
            m.Step(2.5);
            Assert.AreEqual((ChipPlace.Belt, BeltX), (C(id).Place, C(id).X));
        }

        [Test]
        public void ChipFlow_DischargeReversedMidPush_ChipStillRidesBelt()
        {
            int id = ChipOnBelt(true);
            Hold(SwitchId.Conveyor, 3.1);
            Hold(SwitchId.DischargeFwd, 0.3); // 15 mm push, then back
            Hold(SwitchId.DischargeBwd, 0.1);
            m.Step(2.0);
            Assert.AreEqual(ChipPlace.Belt, C(id).Place);
            Assert.Less(C(id).X, BeltX, "half pushed");
            m.SetSwitch(SwitchId.StopperDown, true); // raised
            m.SetSwitch(SwitchId.Conveyor, true);
            m.Step(5.0);
            Assert.AreEqual(StopY, C(id).Y, "a half-pushed chip must still ride the belt and stop at the stopper");
            Assert.IsTrue(I(In.StopperDetect));
            m.SetSwitch(SwitchId.StopperDown, false);
            Assert.Greater(Until(() => C(id).Place == ChipPlace.Box1, 6), 0, "stranded on the belt");
        }

        [Test]
        public void ChipFlow_AllBeltChipsMove_QueueAt40mm()
        {
            m.SetSwitch(SwitchId.StopperDown, true); // raised: the queue forms at the stopper
            int a = ChipOnBelt(true);
            Hold(SwitchId.Conveyor, 1.0);
            int b = ChipOnBelt(false);
            int ya = C(a).Y;
            Hold(SwitchId.Conveyor, 1.0);
            Assert.AreEqual(ya + 100000, C(a).Y, "front chip moves");
            Assert.AreEqual(BeltEntryY + 100000, C(b).Y, "second chip moves at the same time (reference moves only the last one)");
            int c = ChipOnBelt(true);
            Hold(SwitchId.Conveyor, 10.0);
            Assert.AreEqual(StopY, C(a).Y);
            Assert.AreEqual(StopY - QueueSpacingUm, C(b).Y);
            Assert.AreEqual(StopY - 2 * QueueSpacingUm, C(c).Y);
        }

        [Test]
        public void ChipRemove_ClickRemovesBeltChip_NotMagazineChip()
        {
            int a = ChipOnBelt(true);
            int b = m.AddChip(false);
            Assert.IsFalse(m.RemoveChip(b), "magazine chips cannot be clicked");
            Assert.IsTrue(m.RemoveChip(a));
            Assert.AreEqual(1, m.Chips.Count);
        }

        // ---------------- AC5 servo ----------------

        [Test]
        public void Servo_JogOneWayLimits()
        {
            Hold(SwitchId.ServoUp, 1.0);
            Assert.AreEqual(150, m.D40, "Up from home stops at the limit");
            Hold(SwitchId.ServoDown, 2.0);
            Assert.AreEqual(50, m.D40);
            Hold(SwitchId.ServoUp, 1.0);
            Assert.AreEqual(100, m.D40);
            Hold(SwitchId.ServoDown, 10.0);
            Assert.AreEqual(0, m.D40);
            Assert.AreEqual(ServoMinUm, m.ServoZ);
            Hold(SwitchId.ServoUp, 10.0);
            Assert.AreEqual(150, m.D40);
            Assert.AreEqual(ServoMaxUm, m.ServoZ);
        }

        [Test]
        public void Servo_OnlineJog_50UnitsPerSecond_OneWayLimits()
        {
            Online();
            W(Out.JogSpeed, 10);
            O(Out.JogPlus, true);
            m.Step(1.0);
            Assert.AreEqual(150, m.D40, "JOG+ from home stops at the limit");
            O(Out.JogPlus, false);
            O(Out.JogMinus, true);
            m.Step(1.0);
            Assert.AreEqual(100, m.D40, 1, "50 units/s ±2%");
            O(Out.JogPlus, true); // JOG+ wins over JOG-
            m.Step(0.2);
            Assert.AreEqual(110, m.D40);
        }

        [Test]
        public void Servo_JogBothOn_UpWinsBelowLimit_DownAtLimit()
        {
            m = new MpsModel(); // below the limit (home) JOG+ wins; offline switches use the same precedence
            m.SetSwitch(SwitchId.ServoUp, true);
            m.SetSwitch(SwitchId.ServoDown, true);
            m.Step(0.01);
            Assert.AreEqual(ServoStartUm + 500, m.ServoZ);

            m = new MpsModel(); // above the upper limit (a Run can go there, no soft limit) with both ON the reference goes down
            Online();
            W(Out.Command, RunCommand); W(Out.Target, 152); W(Out.Speed, 10);
            O(Out.Run, true);
            Assert.Greater(Until(() => I(In.StartDone), 3), 0);
            O(Out.Run, false);
            W(Out.JogSpeed, 10);
            O(Out.JogPlus, true);
            O(Out.JogMinus, true);
            m.Step(0.01);
            Assert.AreEqual(102000 - 500, m.ServoZ);
            m.Step(1.0);
            Assert.That(m.D40, Is.InRange(149, 150), "then it chatters at the limit (down below it, up to it)");
            Assert.LessOrEqual(m.ServoZ, ServoMaxUm);
        }

        [Test]
        public void Servo_Run_LandsOnTarget()
        {
            Online();
            W(Out.Command, RunCommand); W(Out.Target, 100); W(Out.Speed, 10);
            O(Out.Run, true);
            Assert.AreEqual(0.80, Until(() => I(In.StartDone), 3), 0.02); // 40 units from home at 50 units/s
            Assert.AreEqual(100, m.D40);
            Assert.IsTrue(I(In.PosDone));
            m.Step(1.0);
            Assert.AreEqual(100, m.D40, "stays on target");
            O(Out.Run, false);
            Assert.IsFalse(I(In.StartDone) || I(In.PosDone), "done bits drop with Run");
        }

        [Test]
        public void Servo_RunSweep_0To150_EveryTargetDone_Monotonic()
        {
            for (int target = 0; target <= 150; target++)
            {
                m = new MpsModel();
                Online();
                W(Out.Command, RunCommand); W(Out.Target, target); W(Out.Speed, 10);
                O(Out.Run, true);
                int prev = Math.Abs(m.D40 - target);
                for (int i = 0; i < 400; i++)
                {
                    m.Step(0.01);
                    Assert.LessOrEqual(Math.Abs(m.D40 - target), prev, "target " + target + ": D40 moved away from the target");
                    prev = Math.Abs(m.D40 - target);
                }
                Assert.AreEqual(target, m.D40, "target " + target);
                Assert.IsTrue(I(In.StartDone), "target " + target + ": D3.0");
            }
        }

        [Test]
        public void Servo_HighSpeedRun_NoOscillation()
        {
            Online();
            W(Out.Command, RunCommand); W(Out.Target, 101); W(Out.Speed, 300); // 15 mm per substep
            O(Out.Run, true);
            Assert.Greater(Until(() => I(In.StartDone), 1), 0);
            m.Step(1.0);
            Assert.AreEqual(101, m.D40);
        }

        [Test]
        public void Servo_Command9002_NoMotion()
        {
            Online();
            W(Out.Command, 9002); W(Out.Target, 100); W(Out.Speed, 10);
            O(Out.Run, true);
            m.Step(2.0);
            Assert.AreEqual(140, m.D40);
            Assert.IsFalse(I(In.StartDone));
        }

        [Test]
        public void Servo_StatusBits()
        {
            Online();
            O(Out.PlcReady, true);
            Assert.IsTrue(I(In.DriverReady), "READY echoes D102.0");
            O(Out.ServoOn, true);
            Assert.IsFalse(I(In.AxisBusy), "SERVO ON alone is not BUSY");
            O(Out.JogMinus, true);
            Assert.IsTrue(I(In.AxisBusy));
            O(Out.JogMinus, false);
            O(Out.Run, true);
            Assert.IsTrue(I(In.AxisBusy));
            O(Out.ServoOn, false);
            Assert.IsFalse(I(In.AxisBusy));
            W(Out.ErrorClear, 1);
            var b = new short[4];
            var w = new short[5];
            m.ComposeInputs(b, w);
            Assert.IsFalse(PlcMap.Get(b, In.AxisError));
            CollectionAssert.AreEqual(new short[] { 0, 0, 0 }, new[] { w[In.ErrorNo], w[In.InputSignal], w[In.Status] });
            Assert.AreEqual(0, w[1], "D41");
        }

        [Test]
        public void Servo_RunSuspendedWhileServoOnAndJog()
        {
            Online();
            W(Out.Command, RunCommand); W(Out.Target, 100); W(Out.Speed, 10); W(Out.JogSpeed, 0); // jog itself moves 0
            O(Out.ServoOn, true);
            O(Out.JogMinus, true);
            O(Out.Run, true);
            m.Step(1.0);
            Assert.AreEqual(140, m.D40, "OQ-12: Run suspended while SERVO ON and a JOG bit are ON");
            O(Out.ServoOn, false);
            m.Step(1.5);
            Assert.AreEqual(100, m.D40, "Run resumes");
        }

        [Test]
        public void Servo_StopHaltsJogAndRunWhileOn()
        {
            Online();
            W(Out.Command, RunCommand); W(Out.Target, 100); W(Out.Speed, 10);
            O(Out.Stop, true);
            O(Out.Run, true);
            m.Step(1.0);
            Assert.AreEqual(140, m.D40, "OQ-3 deviation: STOP halts Run");
            O(Out.Run, false);
            W(Out.JogSpeed, 10);
            O(Out.JogMinus, true);
            m.Step(1.0);
            Assert.AreEqual(140, m.D40, "STOP halts jog");
            O(Out.JogMinus, false);
            O(Out.Stop, false);
            O(Out.Run, true);
            Assert.Greater(Until(() => I(In.StartDone), 2), 0, "Run resumes when STOP drops");
            Assert.AreEqual(100, m.D40);
        }

        // ---------------- AC6 link loss / offline ----------------

        [Test]
        public void LinkLoss_ServoStops_VacuumOff_ChipDrops_SwitchesDriveAgain()
        {
            int id = ChipHeldAtPad();
            W(Out.JogSpeed, 10);
            O(Out.JogPlus, true);
            m.Step(0.3);
            Assert.AreEqual(15, m.D40);
            O(Out.PlcReady, true); O(Out.ServoOn, true); O(Out.Run, true); W(Out.Target, m.D40); // status bits ON at the drop
            var status = new[] { In.DriverReady, In.AxisBusy, In.StartDone, In.PosDone };
            foreach (var bit in status) Assert.IsTrue(I(bit), "before the drop: bit 0x" + bit.ToString("X2"));
            m.SetOnline(false);
            foreach (var bit in status) Assert.IsFalse(I(bit), "after the drop: bit 0x" + bit.ToString("X2"));
            m.Step(0.01);
            short d40 = m.D40;
            Assert.IsFalse(I(In.PadOn), "D0.C");
            Assert.AreNotEqual(ChipPlace.Held, C(id).Place, "held chip must drop");
            m.Step(1.0);
            Assert.AreEqual(d40, m.D40, "servo must stay stopped after link loss");

            Hold(SwitchId.SupplyFwd, 0.1);
            Assert.Greater(Until(() => I(In.SupplyFwd), 2), 0, "3D switches drive the line again");
        }

        [Test]
        public void Online_SwitchesOnlyChangeColor()
        {
            Online();
            m.SetSwitch(SwitchId.SupplyFwd, true);
            m.SetSwitch(SwitchId.Conveyor, true);
            m.Step(2.0);
            Assert.IsTrue(m.Switch(SwitchId.SupplyFwd), "switch state (color) still tracked");
            Assert.AreEqual(SupplyRet, m.CylinderPos(Cyl.Supply));
            Assert.IsFalse(m.ConveyorOn);
        }

        [Test]
        public void Offline_PlcCommandsDropped_LastWordsKeptForPanel()
        {
            Online();
            O(Out.DrillDown, true);
            O(Out.Conveyor, true);
            W(Out.Target, 77);
            m.Step(2.0);
            Assert.IsTrue(I(In.DrillDown));
            Assert.IsTrue(m.ConveyorOn);
            m.SetOnline(false); // OQ-2: Connecting/Error/Disconnect
            Assert.AreEqual(1.40, Until(() => I(In.DrillUp), 3), 0.03, "single-acting drill returns up");
            Assert.IsFalse(m.ConveyorOn);
            var b = new short[4];
            var w = new short[7];
            m.CopyLastCommands(b, w);
            Assert.IsTrue(PlcMap.Get(b, Out.DrillDown));
            Assert.AreEqual(77, w[Out.Target]);
            O(Out.PlcReady, true); // offline: stored for the panel, not applied
            m.Step(2.0);
            Assert.IsFalse(I(In.DrillDown));
            Assert.IsFalse(I(In.DriverReady), "D2.0 must not echo PLC READY while offline");
        }

        [Test]
        public void Vacuum_OfflineForcedOff()
        {
            int id = ChipHeldAtPad();
            m.SetOnline(false);
            Assert.AreEqual(ChipPlace.Belt, C(id).Place, "dropped onto the belt under the pad");
            m.SetSwitch(SwitchId.SuctionPad, true);
            m.Step(1.0);
            Assert.IsFalse(I(In.PadOn), "the suction pad switch does nothing offline");
        }

        [Test]
        public void Vacuum_PickAtStopper_PlaceOnShelf_ShelfRidesStocker()
        {
            int id = ChipHeldAtPad(); // stopper up, chip at StopY with D1.3 ON, pad lowered, vacuum picks it
            Assert.IsFalse(I(In.StopperDetect), "picked chip left the stopper");
            W(Out.Target, 140); O(Out.Run, true); // shelf 1 height (calibrated)
            O(Out.LiftFwd, true);
            O(Out.StockerFwd, true);
            m.Step(3.0);
            Assert.AreEqual(140, m.D40);
            Assert.IsTrue(I(In.PadOn), "chip still held while moving");
            O(Out.Vacuum, false);
            m.Step(0.01);
            Assert.AreEqual((ChipPlace.Shelf, 0), (C(id).Place, C(id).Shelf));
            Assert.IsFalse(I(In.PadOn));
            O(Out.StockerFwd, false);
            O(Out.StockerBwd, true);
            m.Step(1.5);
            Assert.AreEqual(StockerRootY + StockerRet + SlotDy, C(id).Y, "shelf chip moves with the stocker");
        }

        // ---------------- fixed step ----------------

        static MpsModel Drive(int steps, double dt)
        {
            var a = new MpsModel();
            a.AddChip(true);
            a.SetSwitch(SwitchId.SupplyFwd, true);
            a.SetSwitch(SwitchId.ServoDown, true);
            for (int i = 0; i < steps; i++) a.Step(dt);
            return a;
        }

        static string State(MpsModel a)
        {
            var b = new short[4];
            var w = new short[5];
            a.ComposeInputs(b, w);
            return string.Join(",", b) + "|" + string.Join(",", w) + "|" + a.ServoZ + "|"
                 + string.Join(",", Enum.GetValues(typeof(Cyl)).Cast<Cyl>().Select(c => a.CylinderPos(c))) + "|"
                 + string.Join(";", a.Chips.Select(c => c.Place + " " + c.X + " " + c.Y + " " + c.Z));
        }

        [TestCase(1)]
        [TestCase(3)]
        public void FrameRateIndependence_EqualTickCount(int k)
        {
            var a = Drive(33 * k, 0.016);
            var b = Drive(16 * k, 0.033);
            Assert.AreEqual((long)Math.Floor(52.8 * k + 1e-9), a.SubstepCount); // k = 1: 52 substeps, 8000 µs remainder
            Assert.AreEqual(a.SubstepCount, b.SubstepCount);
            Assert.AreEqual(State(a), State(b));
            Assert.AreNotEqual(State(new MpsModel()), State(a), "nothing moved: the comparison would be vacuous");
        }

        [Test]
        public void ServoLamps_HomeAtStart_BottomAtLimit_TopAfterJogUp()
        {
            Assert.IsTrue(m.ServoHome);
            Assert.IsFalse(m.ServoTop || m.ServoBottom);
            m.SetSwitch(SwitchId.ServoDown, true); m.Step(4); m.SetSwitch(SwitchId.ServoDown, false);
            Assert.IsTrue(m.ServoBottom);
            Assert.IsFalse(m.ServoHome);
            m.SetSwitch(SwitchId.ServoUp, true); m.Step(5); m.SetSwitch(SwitchId.ServoUp, false);
            Assert.IsTrue(m.ServoTop);
            Assert.IsFalse(m.ServoBottom || m.ServoHome);
        }

        // ---------------- whole line ----------------

        [Test]
        public void WholeLine_120s_MagazineToBox1_Under1sWall()
        {
            var wall = Stopwatch.StartNew();
            int id = m.AddChip(false);
            Hold(SwitchId.SupplyFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.SupplyBwd, 0.1); m.Step(2.0);
            m.SetSwitch(SwitchId.DrillDown, true);
            Assert.Greater(Until(() => I(In.DrillDown), 2), 0);
            m.SetSwitch(SwitchId.DrillDown, false);
            m.Step(1.5);
            Hold(SwitchId.EmitFwd, 0.1); m.Step(2.0);
            Hold(SwitchId.EmitBwd, 0.1);

            m.SetSwitch(SwitchId.StopperDown, true); // raised
            m.SetSwitch(SwitchId.Conveyor, true);
            bool sorterSeen = false;
            Assert.Greater(Until(() => { sorterSeen |= I(In.Nonmetal); return I(In.StopperDetect) && C(id).Y == StopY; }, 10), 0);
            Assert.IsTrue(sorterSeen, "sorter never saw the chip");
            m.Step(3.0);
            Assert.AreEqual(StopY, C(id).Y);
            m.SetSwitch(SwitchId.StopperDown, false);
            Assert.Greater(Until(() => C(id).Place == ChipPlace.Box1, 10), 0);
            m.Step(120.0 - m.SubstepCount * 0.01);
            wall.Stop();
            Assert.AreEqual(12000, m.SubstepCount);
            Assert.AreEqual(ChipPlace.Box1, C(id).Place);
            Assert.Less(wall.ElapsedMilliseconds, 1000, "120 s of sim time took " + wall.ElapsedMilliseconds + " ms");
        }
    }
}
