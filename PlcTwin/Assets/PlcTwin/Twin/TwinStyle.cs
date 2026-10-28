using System.Collections.Generic;
using System.Linq;
using PlcTwin.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlcTwin.Twin
{
    /// <summary>
    /// Style preview (player argument -style a|b|c; no argument = style a, see PlcTwinApp.Awake). Swaps materials for the variants that
    /// SceneBuilder writes to Resources/Styles/{style}/ (key = material name, "M_" + name on moving parts), and sets the
    /// background, light and ambient. Style c also draws box/cylinder outlines; moving parts glow while their cylinder is
    /// out of its retracted position, the belt while it runs. Status panel and layout are unchanged.
    /// </summary>
    public static class TwinStyle
    {
        public static void Apply(string style, PlcTwinApp app, MpsView view, Camera cam)
        {
            if (style != "a" && style != "b" && style != "c") return;
            string dir = "Styles/" + style + "/";
            Material Load(string key) => Resources.Load<Material>(dir + key);

            var movers = new HashSet<Transform>(view.Root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == MpsView.ServoLifter || MpsView.Movers.Any(mv => mv.name == t.name)));
            var outlined = new List<(Renderer r, Transform mover)>();
            foreach (var r in view.Root.GetComponentsInChildren<Renderer>(true))
            {
                bool module = r.transform.parent != null && (r.transform.parent.name.StartsWith("LampModule") || r.transform.parent.name.StartsWith("SwitchModule"));
                Transform mover = null;
                if (!module)
                    for (var t = r.transform; t != null && mover == null; t = t.parent)
                        if (movers.Contains(t)) mover = t;
                var mats = r.sharedMaterials; // Blender parts carry several slots
                for (int i = 0; i < mats.Length; i++)
                {
                    string key = mats[i] != null ? mats[i].name : "";
                    mats[i] = (mover != null ? Load("M_" + key) : null) ?? Load(key) ?? mats[i];
                }
                r.sharedMaterials = mats;
                if (!module) outlined.Add((r, mover));
            }
            view.LampOn = Load("LampOn") ?? view.LampOn;
            view.LampOff = Load("LampOff") ?? view.LampOff;
            view.SwitchOn = Load("SwitchOn") ?? view.SwitchOn;
            view.SwitchOff = Load("SwitchOff") ?? view.SwitchOff;
            foreach (var p in new[] { view.ChipMetalPrefab, view.ChipNonmetalPrefab })
            {
                var r = p.GetComponent<Renderer>(); // player only: the in-memory prefab, never saved
                r.sharedMaterial = Load(r.sharedMaterial.name) ?? r.sharedMaterial;
            }

            var light = Object.FindAnyObjectByType<Light>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (style == "a") // modern industrial: bright flat studio, faint shadows
            {
                cam.backgroundColor = new Color(0.90f, 0.91f, 0.92f);
                Ambient(new Color(0.62f, 0.63f, 0.65f), new Color(0.55f, 0.56f, 0.58f), new Color(0.45f, 0.45f, 0.47f));
                light.intensity = 0.9f; light.color = Color.white; light.shadows = LightShadows.Soft; light.shadowStrength = 0.3f;
            }
            else if (style == "b") // realistic kit: neutral studio grey, warm key light, full soft shadows
            {
                cam.backgroundColor = new Color(0.30f, 0.31f, 0.33f);
                Ambient(new Color(0.55f, 0.57f, 0.62f), new Color(0.38f, 0.38f, 0.40f), new Color(0.18f, 0.18f, 0.19f));
                light.intensity = 1.25f; light.color = new Color(1f, 0.96f, 0.90f); light.shadows = LightShadows.Soft; light.shadowStrength = 0.85f;
                RenderSettings.reflectionIntensity = 1f;
            }
            else // blueprint: deep navy, no shadows, outlines
            {
                cam.backgroundColor = new Color(0.03f, 0.07f, 0.17f);
                Ambient(new Color(0.30f, 0.40f, 0.60f), new Color(0.25f, 0.32f, 0.50f), new Color(0.15f, 0.20f, 0.32f));
                light.intensity = 0.5f; light.shadows = LightShadows.None;
                var lines = new Material(Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
                lines.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                lines.SetInt("_DstBlend", (int)BlendMode.One); // additive: overlaps get brighter, like a glow
                lines.SetInt("_Cull", (int)CullMode.Off);
                lines.SetInt("_ZWrite", 0);
                Camera.onPostRender += c => { if (c == cam) Outlines(lines, outlined, app.Model); };
            }
        }

        static void Ambient(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
        }

        static readonly Color Line = new Color(0.45f, 0.75f, 1f, 0.55f), Mover = new Color(0.55f, 0.90f, 1f, 0.9f), Powered = new Color(1f, 0.92f, 0.45f, 1f);

        // 12 edges per cube, two 16-gon rims and 4 sides per cylinder (Unity primitive meshes: unit cube, cylinder r 0.5 h 2).
        static void Outlines(Material mat, List<(Renderer r, Transform mover)> items, MpsModel m)
        {
            mat.SetPass(0);
            GL.Begin(GL.LINES);
            foreach (var (r, mover) in items)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                GL.Color(mover == null ? (r.name == "ConveyorBelt" && m.ConveyorOn ? Powered : Line) : IsPowered(mover, m) ? Powered : Mover);
                var w = r.transform.localToWorldMatrix;
                void L(Vector3 a, Vector3 b) { GL.Vertex(w.MultiplyPoint3x4(a)); GL.Vertex(w.MultiplyPoint3x4(b)); }
                if (mesh.name == "Cube") // Unity primitives only; the Blender meshes have no outline
                    for (int i = 0; i < 8; i++)
                        for (int k = 0; k < 3; k++)
                        {
                            int j = i ^ (1 << k);
                            if (j > i) L(Corner(i), Corner(j));
                        }
                else if (mesh.name == "Cylinder")
                    for (int i = 0; i < 16; i++)
                    {
                        float a0 = i * Mathf.PI / 8, a1 = (i + 1) * Mathf.PI / 8;
                        Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 0.5f, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 0.5f;
                        L(p0 + Vector3.up, p1 + Vector3.up);
                        L(p0 - Vector3.up, p1 - Vector3.up);
                        if (i % 4 == 0) L(p0 + Vector3.up, p0 - Vector3.up);
                    }
            }
            GL.End();
        }

        static Vector3 Corner(int i) => new Vector3((i & 1) - 0.5f, (i >> 1 & 1) - 0.5f, (i >> 2 & 1) - 0.5f);

        static readonly MpsModel Rest = new MpsModel();

        // Out of the rest position = driven.
        static bool IsPowered(Transform mover, MpsModel m)
        {
            if (mover.name == MpsView.ServoLifter) return m.ServoZ != Rest.ServoZ;
            foreach (var mv in MpsView.Movers)
                if (mv.name == mover.name) return m.CylinderPos(mv.cyl) != Rest.CylinderPos(mv.cyl);
            return false;
        }
    }
}
