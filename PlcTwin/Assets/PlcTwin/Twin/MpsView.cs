using System.Collections.Generic;
using System.Linq;
using PlcTwin.Sim;
using UnityEngine;
using In = PlcTwin.Sim.PlcMap.In;

namespace PlcTwin.Twin
{
    /// <summary>
    /// Renders MpsModel (plan Step 5): moving parts, lamp and switch colors, pooled chips. Binds scene objects by name.
    /// Also forwards clicks: a 3D switch is ON while the left button is held (momentary, dragging off keeps it ON), a click on
    /// a chip removes it. No logic beyond that.
    /// </summary>
    public sealed class MpsView : MonoBehaviour
    {
        public PlcTwinApp App;
        public StatusPanel Panel;
        public Transform Root; // "MPS"
        public GameObject ChipMetalPrefab, ChipNonmetalPrefab;
        public Material LampOff, LampOn, SwitchOff, SwitchOn;

        /// <summary>Moving parts: (name, cylinder, MPS-local axis 0 = x, 1 = y, 2 = z). Built at the model's initial position.</summary>
        public static readonly (string name, Cyl cyl, int axis)[] Movers =
        {
            ("CylinerSupplyInner", Cyl.Supply, 0), ("CylinderEmitInner", Cyl.Emit, 1), ("DrillUnit", Cyl.Drill, 2),
            ("CylinderDischargeInner", Cyl.Discharge, 0), ("StopperUnit", Cyl.Stopper, 2), ("CylinderLiftInner", Cyl.Lift, 0),
            ("CylinderStockerInner", Cyl.Stocker, 1),
        };
        public const string ServoLifter = "ServoLifter", ConveyorLamp = "LampConveyorLamp",
                            ServoUpLamp = "LampServoUp", ServoHomeLamp = "LampServoHome", ServoDownLamp = "LampServoDown";

        /// <summary>Lamps showing a D0-D3 bit (ref §1.5). The conveyor lamp shows the belt drive; the servo lamps have no logic.</summary>
        public static readonly (string name, int bit)[] Lamps =
        {
            ("LampSupplyForward", In.SupplyFwd), ("LampSupplyBackward", In.SupplyBwd), ("LampEmitForward", In.EmitFwd),
            ("LampEmitBackward", In.EmitBwd), ("LampDrillDown", In.DrillDown), ("LampDrillUp", In.DrillUp),
            ("LampDischargeForward", In.DischargeFwd), ("LampDischargeBackward", In.DischargeBwd), ("LampStopperDown", In.StopperDown),
            ("LampStopperUp", In.StopperUp), ("LampLiftForward", In.LiftFwd), ("LampLiftBackward", In.LiftBwd),
            ("LampSuctionPad", In.PadOn), ("LampStockerForward", In.StockerFwd), ("LampStockerBackward", In.StockerBwd),
            ("LampSupplySensor", In.SupplySensor), ("LampNonmetal", In.Nonmetal),
            ("LampMetal", In.Metal), ("LampStopperDetect", In.StopperDetect),
        };

        public static readonly (string name, SwitchId id)[] Switches =
        {
            ("SwitchSupplyForward", SwitchId.SupplyFwd), ("SwitchSupplyBackward", SwitchId.SupplyBwd),
            ("SwitchEmitForward", SwitchId.EmitFwd), ("SwitchEmitBackward", SwitchId.EmitBwd), ("SwitchDrillDown", SwitchId.DrillDown),
            ("SwitchDischargeForward", SwitchId.DischargeFwd), ("SwitchDischargeBackward", SwitchId.DischargeBwd),
            ("SwitchStopperDown", SwitchId.StopperDown), ("SwitchConveyor", SwitchId.Conveyor),
            ("SwitchLiftForward", SwitchId.LiftFwd), ("SwitchLiftBackward", SwitchId.LiftBwd),
            ("SwitchServoUp", SwitchId.ServoUp), ("SwitchServoDown", SwitchId.ServoDown),
            ("SwitchStockerForward", SwitchId.StockerFwd), ("SwitchStockerBackward", SwitchId.StockerBwd),
            ("SwitchSuctionPad", SwitchId.SuctionPad), ("SwitchWorkDrill", SwitchId.WorkDrill),
        };

        /// <summary>Every name this view binds (SceneBindingTests).</summary>
        public static IEnumerable<string> BoundNames =>
            Movers.Select(m => m.name).Concat(Lamps.Select(l => l.name)).Concat(Switches.Select(s => s.name))
                  .Concat(new[] { ServoLifter, ConveyorLamp, ServoUpLamp, ServoHomeLamp, ServoDownLamp });

        readonly List<(Transform t, Cyl cyl, int axis, Vector3 basePos, int basePosUm)> movers = new List<(Transform, Cyl, int, Vector3, int)>();
        readonly List<(Renderer r, int bit)> lamps = new List<(Renderer, int)>();
        readonly List<(Renderer r, SwitchId id)> switches = new List<(Renderer, SwitchId)>();
        readonly Dictionary<string, SwitchId> switchByName = Switches.ToDictionary(s => s.name, s => s.id);
        readonly Dictionary<int, GameObject> chips = new Dictionary<int, GameObject>();
        readonly short[] inBits = new short[4], inWords = new short[5];
        MpsModel chipModel;
        Transform servo;
        Vector3 servoBase;
        Renderer conveyorLamp, servoUpLamp, servoHomeLamp, servoDownLamp;
        Camera cam;
        SwitchId? held;

        void Awake()
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in Root.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            var initial = new MpsModel();
            foreach (var (name, cyl, axis) in Movers)
                movers.Add((byName[name], cyl, axis, byName[name].localPosition, initial.CylinderPos(cyl)));
            foreach (var (name, bit) in Lamps) lamps.Add((byName[name].GetComponent<Renderer>(), bit));
            foreach (var (name, id) in Switches) switches.Add((byName[name].GetComponent<Renderer>(), id));
            servo = byName[ServoLifter];
            servoBase = servo.localPosition - new Vector3(0, 0, MpsGeometry.ServoStartUm * 1e-6f);
            conveyorLamp = byName[ConveyorLamp].GetComponent<Renderer>();
            servoUpLamp = byName[ServoUpLamp].GetComponent<Renderer>();
            servoHomeLamp = byName[ServoHomeLamp].GetComponent<Renderer>();
            servoDownLamp = byName[ServoDownLamp].GetComponent<Renderer>();
            cam = Camera.main;
        }

        void Update()
        {
            var m = App.Model;
            if (Input.GetMouseButtonDown(0) && !Panel.IsOverUi(Input.mousePosition)
                && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 100f))
            {
                string n = hit.collider.name;
                if (switchByName.TryGetValue(n, out var id)) { held = id; m.SetSwitch(id, true); }
                else if (n.StartsWith("Chip:") && int.TryParse(n.Substring(5), out int chipId)) m.RemoveChip(chipId);
            }
            if (Input.GetMouseButtonUp(0) && held.HasValue)
            {
                m.SetSwitch(held.Value, false);
                held = null;
            }
        }

        void LateUpdate()
        {
            var m = App.Model;
            foreach (var (t, cyl, axis, basePos, baseUm) in movers)
            {
                var p = basePos;
                p[axis] += (m.CylinderPos(cyl) - baseUm) * 1e-6f;
                t.localPosition = p;
            }
            servo.localPosition = servoBase + new Vector3(0, 0, m.ServoZ * 1e-6f);
            m.ComposeInputs(inBits, inWords);
            foreach (var (r, bit) in lamps) r.sharedMaterial = PlcMap.Get(inBits, bit) ? LampOn : LampOff;
            conveyorLamp.sharedMaterial = m.ConveyorOn ? LampOn : LampOff;
            servoUpLamp.sharedMaterial = m.ServoTop ? LampOn : LampOff;
            servoHomeLamp.sharedMaterial = m.ServoHome ? LampOn : LampOff;
            servoDownLamp.sharedMaterial = m.ServoBottom ? LampOn : LampOff;
            foreach (var (r, id) in switches) r.sharedMaterial = m.Switch(id) ? SwitchOn : SwitchOff;
            Chips(m);
        }

        // Pool by chip id; binned chips are hidden, removed chips (or chips of a replaced model) are destroyed.
        void Chips(MpsModel m)
        {
            if (m != chipModel) // Reset / replaced model: ids restart at 1, so no pooled chip may be reused
            {
                foreach (var go in chips.Values) Destroy(go);
                chips.Clear();
                chipModel = m;
            }
            var alive = new HashSet<int>();
            foreach (var c in m.Chips)
            {
                alive.Add(c.Id);
                if (!chips.TryGetValue(c.Id, out var go))
                {
                    go = Instantiate(c.Metal ? ChipMetalPrefab : ChipNonmetalPrefab, Root, false);
                    go.name = "Chip:" + c.Id;
                    chips[c.Id] = go;
                }
                go.SetActive(c.Place != ChipPlace.Box1 && c.Place != ChipPlace.Box2);
                go.transform.localPosition = new Vector3(c.X, c.Y, c.Z) * 1e-6f;
            }
            foreach (var id in chips.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                Destroy(chips[id]);
                chips.Remove(id);
            }
        }
    }
}
