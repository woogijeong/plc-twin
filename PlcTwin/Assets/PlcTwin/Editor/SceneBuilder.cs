using System.IO;
using PlcTwin.Twin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static PlcTwin.Sim.MpsGeometry;
using Object = UnityEngine.Object;

namespace PlcTwin.EditorTools
{
    /// <summary>
    /// Builds the MPS line from primitives (plan Step 5): prefabs in Assets/Prefabs, Assets/Scenes/Main.unity, player
    /// settings and README. Idempotent: materials and prefabs are overwritten in place (GUIDs kept), the scene is rebuilt.
    /// All positions are MPS-local meters (+z up, +y toward the conveyor end), from MpsGeometry / ref §2 and §6.
    /// Run: -executeMethod PlcTwin.EditorTools.SceneBuilder.Build (scene) or .BuildPlayer (scene + Build/PlcTwin.exe).
    /// </summary>
    public static class SceneBuilder
    {
        const string ReadmePath = "Assets/StreamingAssets/README.txt", ScenePath = "Assets/Scenes/Main.unity";
        const string PrefabDir = "Assets/Prefabs", MatDir = "Assets/Prefabs/Materials";
        const float BaseTop = -0.1585f;

        static Material white, mint, lightBlue, yellow, orange, purple, belt, cream, nut, lampOff, lampOn, switchOff, switchOn, metal, nonmetal;

        public static void BuildPlayer()
        {
            Build();
            string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Build/PlcTwin.exe"));
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, exe, BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("SceneBuilder: player build " + report.summary.result + " -> " + exe);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        public static void Build()
        {
            Directory.CreateDirectory("Assets/StreamingAssets");
            File.WriteAllText(ReadmePath, Readme);
            Directory.CreateDirectory(MatDir);
            Materials();

            PlayerSettings.productName = "PlcTwin";
            PlayerSettings.defaultScreenWidth = 1387;
            PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true; // OQ-8: resizable; the layout scales
            PlayerSettings.runInBackground = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            // Ref §5.4 local pose mapped through (x, y, z) -> (x, z, -y) = view preset Default (PlcTwinApp moves it on request).
            var v = PlcTwinApp.Views[0];
            cam.transform.SetPositionAndRotation(v.pos, Quaternion.Euler(v.euler));
            cam.orthographic = true;
            cam.orthographicSize = v.size;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;

            var mps = new GameObject("MPS").transform;
            mps.rotation = Quaternion.Euler(-90, 0, 0); // ref local coordinates used as is
            ImportLine(mps);

            var chipMetal = Chip("ChipMetal", metal);
            var chipNonmetal = Chip("ChipNonmetal", nonmetal);

            var appGo = new GameObject("PlcTwinApp");
            var app = appGo.AddComponent<PlcTwinApp>();
            var panel = appGo.AddComponent<StatusPanel>();
            panel.App = app;
            var view = appGo.AddComponent<MpsView>();
            view.App = app;
            view.Panel = panel;
            view.Root = mps;
            view.ChipMetalPrefab = chipMetal;
            view.ChipNonmetalPrefab = chipNonmetal;
            view.LampOff = lampOff;
            view.LampOn = lampOn;
            view.SwitchOff = switchOff;
            view.SwitchOn = switchOn;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("SceneBuilder: scene, prefabs and README written");
        }

        // ---------------------------------------------------------------- materials (palette of ref §6)

        static void Materials()
        {
            white = Mat("White", C(245, 245, 245));
            mint = Mat("CylinderBody", C(106, 243, 216, 0.52f), true);
            lightBlue = Mat("LightBlue", C(120, 192, 242));
            yellow = Mat("Yellow", C(255, 255, 0));
            orange = Mat("Orange", C(239, 185, 68));
            purple = Mat("Purple", C(190, 159, 227));
            belt = Mat("Belt", C(160, 228, 205));
            cream = Mat("Cream", C(251, 254, 197)); // ref render color (ref §6)
            nut = Mat("Nut", C(33, 138, 33));
            lampOff = Mat("LampOff", C(245, 245, 245));
            lampOn = Mat("LampOn", C(0, 255, 0));
            switchOff = Mat("SwitchOff", C(255, 0, 0));
            switchOn = Mat("SwitchOn", C(255, 255, 255));
            metal = Mat("ChipMetal", C(0, 20, 255));
            nonmetal = Mat("ChipNonmetal", C(204, 0, 0));
            foreach (var s in StylePalette)
            {
                Directory.CreateDirectory(StyleDir + "/" + s.style);
                Mat(s.key, s.c, s.c.a < 1, StyleDir + "/" + s.style, s.metal, s.smooth, s.emit);
            }
        }

        // Style preview palettes (TwinStyle, -style a|b|c). Key = material name; "M_" + name applies on moving parts only.
        // a modern industrial: light grey aluminium, flat faces, orange/teal only on moving parts.
        // b realistic kit: aluminium/chrome metallics, black belt, anodized blue grippers (light/shadows in TwinStyle).
        // c blueprint: translucent blue faces (outlines in TwinStyle), emissive lamps and chips.
        const string StyleDir = "Assets/Resources/Styles";
        static readonly (string style, string key, Color c, float metal, float smooth, float emit)[] StylePalette =
        {
            ("a", "White", C(208, 211, 216), 0, 0.1f, 0), ("a", "CylinderBody", C(182, 187, 194), 0, 0.1f, 0),
            ("a", "LightBlue", C(196, 200, 206), 0, 0.1f, 0), ("a", "Yellow", C(160, 165, 172), 0, 0.1f, 0),
            ("a", "Orange", C(160, 165, 172), 0, 0.1f, 0), ("a", "Purple", C(140, 145, 152), 0, 0.1f, 0),
            ("a", "Belt", C(62, 66, 72), 0, 0.1f, 0), ("a", "Cream", C(226, 226, 220), 0, 0.1f, 0),
            ("a", "Nut", C(120, 125, 132), 0, 0.1f, 0),
            ("a", "M_White", C(0, 150, 145), 0, 0.15f, 0), ("a", "M_CylinderBody", C(0, 150, 145), 0, 0.15f, 0),
            ("a", "M_Nut", C(0, 120, 116), 0, 0.15f, 0), ("a", "M_LightBlue", C(255, 120, 20), 0, 0.15f, 0),
            ("a", "M_Yellow", C(255, 120, 20), 0, 0.15f, 0), ("a", "M_Orange", C(255, 120, 20), 0, 0.15f, 0),

            ("b", "White", C(196, 199, 203), 0.75f, 0.55f, 0), ("b", "CylinderBody", C(200, 204, 210), 0.8f, 0.7f, 0),
            ("b", "LightBlue", C(40, 90, 170), 0.6f, 0.6f, 0), ("b", "Yellow", C(205, 165, 70), 0.8f, 0.6f, 0),
            ("b", "Orange", C(220, 100, 30), 0.2f, 0.5f, 0), ("b", "Purple", C(70, 72, 80), 0.6f, 0.5f, 0),
            ("b", "Belt", C(22, 22, 24), 0, 0.2f, 0), ("b", "Cream", C(205, 190, 160), 0, 0.15f, 0),
            ("b", "Nut", C(150, 152, 158), 1, 0.7f, 0), ("b", "M_White", C(230, 232, 236), 1, 0.85f, 0),

            ("c", "White", C(60, 140, 240, 0.10f), 0, 0, 0), ("c", "CylinderBody", C(60, 140, 240, 0.12f), 0, 0, 0),
            ("c", "LightBlue", C(60, 140, 240, 0.12f), 0, 0, 0), ("c", "Yellow", C(60, 140, 240, 0.14f), 0, 0, 0),
            ("c", "Orange", C(60, 140, 240, 0.14f), 0, 0, 0), ("c", "Purple", C(60, 140, 240, 0.12f), 0, 0, 0),
            ("c", "Belt", C(60, 140, 240, 0.20f), 0, 0, 0), ("c", "Cream", C(60, 140, 240, 0.10f), 0, 0, 0),
            ("c", "Nut", C(60, 140, 240, 0.15f), 0, 0, 0),
            ("c", "M_White", C(120, 220, 255, 0.25f), 0, 0, 0), ("c", "M_LightBlue", C(120, 220, 255, 0.25f), 0, 0, 0),
            ("c", "M_CylinderBody", C(120, 220, 255, 0.18f), 0, 0, 0), ("c", "M_Yellow", C(120, 220, 255, 0.25f), 0, 0, 0),
            ("c", "M_Orange", C(120, 220, 255, 0.25f), 0, 0, 0),
            ("c", "LampOff", C(40, 70, 110), 0, 0, 0), ("c", "LampOn", C(80, 255, 150), 0, 0, 1.5f),
            ("c", "SwitchOff", C(255, 70, 90), 0, 0, 0.6f), ("c", "SwitchOn", C(255, 255, 255), 0, 0, 1.5f),
            ("c", "ChipMetal", C(60, 120, 255), 0, 0, 1f), ("c", "ChipNonmetal", C(255, 70, 70), 0, 0, 1f),
        };

        static Color C(int r, int g, int b, float a = 1) => new Color(r / 255f, g / 255f, b / 255f, a);

        static Material Mat(string name, Color c, bool fade = false, string dir = MatDir, float metallic = 0, float smooth = 0.25f, float emit = 0)
        {
            string path = dir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Standard");
            m.color = c;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smooth);
            m.SetColor("_EmissionColor", c * emit);
            if (emit > 0) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            m.SetFloat("_Mode", fade ? 2 : 0);
            m.SetOverrideTag("RenderType", fade ? "Transparent" : "");
            m.SetInt("_SrcBlend", (int)(fade ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetInt("_DstBlend", (int)(fade ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetInt("_ZWrite", fade ? 0 : 1);
            if (fade) m.EnableKeyword("_ALPHABLEND_ON"); else m.DisableKeyword("_ALPHABLEND_ON");
            m.renderQueue = fade ? (int)RenderQueue.Transparent : -1;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------- primitives

        static Transform Node(Transform p, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(p, false);
            t.localPosition = pos;
            return t;
        }

        static GameObject Prim(PrimitiveType type, Transform p, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material m, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(p, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // only switches and chips are clickable
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }

        static GameObject Box(Transform p, string name, Vector3 pos, Vector3 size, Material m) =>
            Prim(PrimitiveType.Cube, p, name, pos, size, Quaternion.identity, m, false);

        /// <summary>Cylinder of diameter d and length len along the MPS-local axis 'x', 'y' or 'z'.</summary>
        static GameObject Rod(Transform p, string name, Vector3 pos, char axis, float d, float len, Material m, bool collider = false) =>
            Prim(PrimitiveType.Cylinder, p, name, pos, new Vector3(d, len / 2, d),
                 axis == 'z' ? Quaternion.Euler(90, 0, 0) : axis == 'x' ? Quaternion.Euler(0, 0, 90) : Quaternion.identity, m, collider);

        static Transform Module(Transform p, string name, Vector3 pos, Vector3 normal)
        {
            var t = Node(p, name, pos);
            t.localRotation = Quaternion.FromToRotation(Vector3.forward, normal);
            Box(t, "Base", new Vector3(0, 0, 0.0025f), new Vector3(0.02f, 0.02f, 0.005f), white);
            return t;
        }

        /// <summary>Limit lamp (ref §4): white base, ø15 disc; OFF white, ON green.</summary>
        static void Lamp(Transform p, string name, Vector3 pos, Vector3 normal) =>
            Rod(Module(p, "LampModule" + name, pos, normal), "Lamp" + name, new Vector3(0, 0, 0.0065f), 'z', 0.015f, 0.003f, lampOff);

        /// <summary>3D switch (ref §4): white base, red ø15 x 7 button with a collider; red OFF, white while pressed.</summary>
        static void Switch(Transform p, string name, Vector3 pos, Vector3 normal) =>
            Rod(Module(p, "SwitchModule" + name, pos, normal), "Switch" + name, new Vector3(0, 0, 0.0085f), 'z', 0.015f, 0.007f, switchOff, true);

        static readonly Vector3 Up = Vector3.forward, Front = Vector3.up, Left = Vector3.left; // MPS-local +z, +y, -x

        static Vector3 M(int um) => new Vector3(um * 1e-6f, 0, 0);
        static float F(int um) => um * 1e-6f;

        static void Prefab(Transform t) =>
            PrefabUtility.SaveAsPrefabAssetAndConnect(t.gameObject, PrefabDir + "/" + t.name + ".prefab", InteractionMode.AutomatedAction);

        static GameObject Chip(string name, Material m)
        {
            var go = Prim(PrimitiveType.Cylinder, null, name, Vector3.zero, new Vector3(0.04f, 0.005f, 0.04f), Quaternion.Euler(90, 0, 0), m, true);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- the line

        public const string FbxPath = "Assets/Models/mps.fbx";
        const string BlenderOut = "../../tools/blender/out/";

        [System.Serializable] class LayoutItem { public string b, u, p; public float[] l; public bool m; }
        [System.Serializable] class Layout { public LayoutItem[] items; }

        /// <summary>
        /// The Blender model (tools/blender/build_mps.py) under the MPS root. Nodes and mesh pivots come from layout.json
        /// (MPS-local, so MpsView still moves movers along the parent's local axes); the FBX only supplies mesh data, which
        /// is in the Unity frame, hence the Euler(90, 0, 0) that undoes the MPS root rotation. Only the 17 switches are clickable.
        /// </summary>
        static void ImportLine(Transform mps)
        {
            Directory.CreateDirectory("Assets/Models");
            File.Copy(Path.Combine(Application.dataPath, BlenderOut + "mps.fbx"), FbxPath, true);
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var meshes = new System.Collections.Generic.Dictionary<string, MeshRenderer>();
            foreach (var r in fbx.GetComponentsInChildren<MeshRenderer>()) meshes[r.name] = r;
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Path.Combine(Application.dataPath, BlenderOut + "layout.json")));
            var switches = new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Select(MpsView.Switches, s => s.name));
            var made = new System.Collections.Generic.Dictionary<string, Transform> { ["MPS"] = mps };
            foreach (var it in layout.items)
            {
                if (it.p == "" && it.u != "MPS") continue; // ChipMetal / ChipNonmetal: primitive prefabs
                if (it.u == "MPS") continue;
                var g = Node(made[it.p], it.u, new Vector3(it.l[0], it.l[1], it.l[2]));
                made[it.b] = g;
                if (!it.m) continue;
                g.localRotation = Quaternion.Euler(90, 0, 0);
                g.gameObject.AddComponent<MeshFilter>().sharedMesh = meshes[it.b].GetComponent<MeshFilter>().sharedMesh;
                g.gameObject.AddComponent<MeshRenderer>().sharedMaterials =
                    System.Array.ConvertAll(meshes[it.b].sharedMaterials, m => AppMaterial(m.name));
                if (switches.Contains(it.u)) g.gameObject.AddComponent<BoxCollider>();
            }
        }

        // Blender material slot = SceneBuilder material key; "M_x" (mover accent) falls back to x outside a style.
        static Material AppMaterial(string key) =>
            AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + key + ".mat")
            ?? AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + key.Replace("M_", "") + ".mat") ?? white;


        const string Readme =
@"PlcTwin - Mitsubishi digital twin (MC protocol 3E binary over TCP, or Modbus TCP)
================================================================================

Config: plc_twin_config.json (this folder; in a built player: PlcTwin_Data/StreamingAssets/).
Edit it with a text editor and restart the app. An invalid config shows 'CONFIG ERROR' in the state line.
Changes made in the app UI (transport, ip, port) last for the session only; they are never written back to the file.
  transport         mc3e (real PLC, MC protocol 3E) or modbus (Modbus TCP server)
  mc3e              ip (numeric IPv4 only, no hostnames), port (must match the PLC open setting, default 5010),
                    monitorTimer250ms (>= 1, PLC monitoring timer in 250 ms units), pollMs (>= 10)
  modbus            ip, port (default 127.0.0.1:502), unitId (0..255), pollMs (>= 10),
                    writeTableBase / readTableBase (first D number of the server's write table / read table)
  connectTimeoutMs  > 0
  timeoutMs         per request; must be >= mc3e.monitorTimer250ms*250+500
  map               first D word of each block: inBits (4 words), inWords (5), outBits (4), outWords (7)
                    defaults D0, D40, D100, D140. Blocks must not overlap.
  panelVisible      show the status panel at start (the 'Panel' button toggles it; display only)
An old config (conveyor/tower/sensor sections) is no longer supported and is rejected with an error.

Use
  The app starts Disconnected: the red 3D buttons drive the line (hold the left mouse button), Metal/Nonmetal add a
  chip to the magazine, Reset (offline only) puts the line back to its power-on state and removes every chip,
  a click on a chip outside the magazine removes it. Connect: the PLC drives the line and the
  3D buttons only change color. Disconnect or a link error: the servo stops, the vacuum drops, the 3D buttons work again.
  The window is resizable (default 1387 x 1000); the layout scales to fit.
  Look: the default is style a (modern industrial). Start 'PlcTwin.exe -style b' (realistic kit) or '-style c' (blueprint,
  outlines do not work with the Blender model) to try the others. Only the 3D equipment changes.
  The servo starts at D40 = 140 (home); D1.0 (emit sensor) is no longer simulated and stays 0.
  The stopper rests DOWN (D0.8 ON); D100.7 ON raises it (D0.9 ON) and it then holds the chips at the stop point.
  View: the Default / Front / Iso / Top buttons (bottom row, right of Panel) move the camera to a fixed view;
  start in one with 'PlcTwin.exe -view front' (default, front, iso or top; e.g. '-view front -style a').
  The mouse never moves the camera.
  On quit the app writes nothing: D0-D3/D40-D44 keep the last written values.

Device numbering
  D numbers are DECIMAL (D100 = 100). Only D devices are used.

Data direction
  The app WRITES inBits (D0-D3) and inWords (D40-D44) to the PLC; the ladder must not write D0-D3/D40-D44
  (no MOV/OUT/SET into those words).
  The app READS outBits (D100-D103) and outWords (D140-D146), which the ladder writes.
  While the app is connected it rewrites D0-D3/D40-D44 every cycle, so forcing them from GX Works2 (device test)
  is overwritten within one poll; disconnect the app first.

GX Works2 simulator via ModbusServerMitsubishi
  Start GX Simulator2, start ModbusServerMitsubishi (GX-Works2 target). On its Setting tab check the D0000 / D0100
  table bases: they must equal modbus.writeTableBase / readTableBase (defaults 0 / 100). Select Modbus in the app
  (127.0.0.1:502) and press Connect.

Modbus server notes
  - A read returns the whole 80-word table; the app always reads it with start 0.
  - The server IGNORES the start address of a write (FC16): every write lands at the first word of the write table.
    So in Modbus mode every write sends D0 up to D44 in one frame, and D4-D39 are written as 0. Do not use D4-D39
    for anything else in Modbus mode. (mc3e is unaffected: it writes only D0-D3 and D40-D44.)
    Whether the server's write table actually reaches the PLC is not verified yet (user sentinel test).
  - The server serves a single client; do not connect a second tool at the same time.
  - The real server needs about 310 ms per request, so one Modbus cycle (2 writes + 1 read) takes about 0.9 s
    (measured 0.93 s); modbus.pollMs only adds waiting on top of that. Unchanged blocks are resent about once a second.
  - Each table is 80 words: with the defaults the write table is D0-D79 and the read table D100-D179
    (writeTableBase / readTableBase); every map block must lie inside its table.

Port
  Default 5010 for mc3e. Ports in the reserved range (about 5000-5009, and 1388H = 5000 upward) must not be
  used for the open setting. TO BE CONFIRMED against the CPU/module manual for your exact model;
  if needed change mc3e.port in the config and the PLC open setting together.

PLC notes
  - Writing during RUN must be enabled on the PLC (QJ71E71: 'Enable Write at RUN'; built-in Ethernet:
    'Enable online change'). If a write is rejected, the state line shows the end code in hex.

LAN SAFETY
  With write-during-RUN enabled, ANYONE on the same LAN can write to this PLC over MC protocol.
  Use a lab network only. Never connect it to a production network.
";
    }
}
