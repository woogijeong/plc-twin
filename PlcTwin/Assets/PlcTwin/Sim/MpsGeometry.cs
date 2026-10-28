namespace PlcTwin.Sim
{
    /// <summary>
    /// The one home for line numbers, in integer micrometers (µm) and MPS-local coordinates (ref: +z up, +y toward the
    /// conveyor end, +x right/back). Sources: ref §2.1 (cylinders), §2.3-§2.8 (conveyor, sorter, chips), §3 (servo), §6.
    /// Mesh dimensions and colors for the scene are added in Step 5.
    /// </summary>
    public static class MpsGeometry
    {
        public const int SubstepUs = 10000;        // fixed 10 ms model step
        public const int CylinderStepUm = 500;     // 0.05 m/s
        public const int ConveyorStepUm = 1000;    // 0.10 m/s

        // Cylinder rod coordinate along its own axis: retracted (Ret) → extended (Ext). Roots are (x, y) of the unit.
        public const int SupplyRootX = -195000, SupplyRootY = -377000, SupplyRet = 15000, SupplyExt = 100000;         // +x
        public const int EmitRootX = BeltX, EmitRootY = -537000, EmitRet = 15000, EmitExt = 100000;                  // +y, on the belt line
        public const int DrillRootX = 55000, DrillRootY = -399000, DrillUp = 80000, DrillDown = 10000;               // -z
        public const int DischargeRootX = 180000, DischargeRootY = -6000, DischargeRet = -15000, DischargeExt = -115000; // -x
        public const int StopperUp = 65000, StopperDown = 40000;                                                     // -z
        public const int LiftRootX = 172000, LiftRootY = 344000, LiftRet = -30000, LiftExt = -100000;                 // -x (suction plate)
        public const int StockerRootX = -85000, StockerRootY = 414000, StockerRet = -55000, StockerExt = -105000;    // -y

        /// <summary>
        /// Limit lamp thresholds (ref §1.5), compared like the reference (strict > / < for supply, drill and stopper; >= / <=
        /// for the rest). The strict ones equal a stroke end, where the reference lights them only through frame overshoot,
        /// so they are moved LampEndTolUm inward and the lamp lights at the end (documented deviation, AC2).
        /// </summary>
        public const int LampEndTolUm = 500; // ponytail: calibrated, one cylinder substep; replaces the reference's frame overshoot
        public const int SupplyFwdLamp = SupplyExt - LampEndTolUm, SupplyBwdLamp = SupplyRet + LampEndTolUm;   // x > 0.100 / x < 0.015
        public const int EmitFwdLamp = 100000, EmitBwdLamp = 15000;                                           // y >= 0.100 / y <= 0.015
        public const int DrillDownLamp = DrillDown + LampEndTolUm, DrillUpLamp = DrillUp - LampEndTolUm;       // z < 0.010 / z > 0.080
        public const int DischargeFwdLamp = -100000, DischargeBwdLamp = -15000;                               // x <= -0.100 / x >= -0.015
        public const int StopperDownLamp = StopperDown + LampEndTolUm, StopperUpLamp = StopperUp - LampEndTolUm; // z < 0.040 / z > 0.065
        public const int LiftFwdLamp = -100000, LiftBwdLamp = -30000;                                         // x <= -0.100 / x >= -0.030
        public const int StockerFwdLamp = -105000, StockerBwdLamp = -55000;                                   // y <= -0.105 / y >= -0.055

        // Chips: disc ø40 x 10 mm.
        public const int ChipR = 20000, ChipHalfT = 5000;

        // Magazine and drill stage (work table). The supply gripper pushes the bottom chip +x by its 85 mm stroke onto the
        // stage. Stage, emit root and belt share x = BeltX and the table top is the belt top, so the emit push (+y) is
        // collinear with the belt and the chip keeps its x and z from the stage onto the belt (no snap, M3 note L4).
        public const int StageX = BeltX, StageY = -401500;
        public const int MagX = StageX - (SupplyExt - SupplyRet), MagY = StageY;

        // Conveyor (root (0.035, 0.075)): belt 0.88 m long, 0.08 m wide, end point at conveyor y +0.47.
        public const int BeltX = 35000, BeltHalfW = 40000, BeltMinY = 75000 - 440000, BeltEndY = 75000 + 470000;
        /// <summary>The emit gripper's full stroke drops the chip here; it then belongs to the belt.</summary>
        public const int BeltEntryY = StageY + (EmitExt - EmitRet);
        public const int SorterY = 75000 - 211000, SorterHalfY = 30000; // trigger 0.06 m long in y
        public const int StopperY = 75000 + 209000, StopperHalfY = 30000;
        /// <summary>Center of a chip held by the raised stopper.</summary>
        public const int StopY = StopperY - (StopperHalfY + ChipR);
        public const int StopDetectTolUm = 5000;    // ponytail: calibrated, D1.3 window around StopY (OQ-13)
        public const int QueueSpacingUm = 40000;    // ponytail: calibrated, chip pitch in a belt queue (OQ-13)
        public const int DischargeCaptureUm = 30000; // ponytail: calibrated, belt chips within this of the discharge axis y are pushed (OQ-13)

        // Bins (chip centers).
        public const int Box1X = 35000, Box1Y = 560000, Box2X = -45000, Box2Y = -31000;

        // Heights of resting chip centers. Ref §6 gives no z for these roots.
        public const int BeltChipZ = -83000;  // ponytail: calibrated, z unmeasured (belt top)
        public const int TableChipZ = BeltChipZ; // work table top = belt top
        public const int FloorChipZ = -153500; // base plate top (-0.1585) + half chip; also inside the boxes

        // Servo lifter (ref §3): D40 = (z + 50000) / 1000.
        // Start = home = D40 140 (user choice; the reference started at 102000 = D40 152, above the jog limit).
        public const int ServoStartUm = 90000, ServoMinUm = -50000, ServoMaxUm = 100000, ServoOffsetUm = 50000;
        public const int ServoJogUmPerUnit = 50;    // per substep per unit of D146/D140 (0.005 m/s per unit)
        public const int ServoSwitchStepUm = 500;   // offline switch speed, 0.05 m/s
        public const int RunCommand = 9003;

        // Vacuum pad and stocker shelves (lift -> vacuum -> stocker flow never observed, OQ-9).
        // Pad tip = (LiftRootX + suction plate rod + PadDx, LiftRootY + PadDy, servo z + PadDz): with the plate retracted it is
        // over a chip held by the raised stopper (belt center, StopY), and it reaches that chip at D40 = 0 (user answer, M2).
        public const int PadDx = -107000;          // ponytail: calibrated, pad offset unmeasured (OQ-9)
        public const int PadDy = StopY - LiftRootY; // ponytail: calibrated, pick at the stopper (-110 mm), lift sequence unobserved (OQ-9)
        public const int PadDz = -28000;           // ponytail: calibrated, pad offset unmeasured (OQ-9)
        /// <summary>Shelf slot center = (StockerRootX + SlotDx, StockerRootY + stocker rod + SlotDy, StockerZ + ShelfZ[k] + ChipHalfT):
        /// under the forward pad with the stocker forward, and at the pad height for D40 = 140 / 85 / 30.</summary>
        public const int SlotDx = 50000, SlotDy = LiftRootY + PadDy - (StockerRootY + StockerExt), StockerZ = -38000; // ponytail: calibrated, shelf slot geometry unmeasured (OQ-9)
        public static readonly int[] ShelfZ = { 90000, 35000, -20000 };
        public const int PickTolUm = 15000; // ponytail: calibrated, pickup/shelf tolerance per axis, sequence unobserved (OQ-9)
    }
}
