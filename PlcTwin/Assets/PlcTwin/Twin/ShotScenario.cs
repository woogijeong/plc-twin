using System.Collections;
using System.IO;
using System.Linq;
using PlcTwin.Sim;
using UnityEngine;

namespace PlcTwin.Twin
{
    /// <summary>
    /// Scripted screenshots (plan Step 6), active only with -mpsShot dir (added by PlcTwinApp). Drives fresh models with a
    /// fixed dt (no Time.deltaTime, no mouse), captures dir/NN.png, writes dir/rects.txt (PanelLayout in pixels, for
    /// tools/compare_shots.ps1), logs D40 per capture and quits.
    /// </summary>
    public sealed class ShotScenario : MonoBehaviour
    {
        public string Dir;
        PlcTwinApp app;

        IEnumerator Start()
        {
            app = GetComponent<PlcTwinApp>();
            app.Frozen = true;
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(Path.Combine(Dir, "rects.txt"), PanelLayout.Build().Select(c =>
            {
                var r = StatusPanel.ScreenRect(c);
                string name = c.Kind == PanelKind.Bit ? c.Lines[0] : c.Kind == PanelKind.WordTitle ? c.Lines[0] + "T"
                            : c.Kind == PanelKind.WordValue ? "D" + c.D.ToString("000") + "V" : c.Lines[0];
                return c.Kind + " " + name + " " + Mathf.RoundToInt(r.x) + " " + Mathf.RoundToInt(r.y) + " " + Mathf.RoundToInt(r.width) + " " + Mathf.RoundToInt(r.height);
            }));
            yield return null;

            yield return Shot("01", new MpsModel());

            var m = new MpsModel();
            m.AddChip(true);
            Run(m, 1.0);
            yield return Shot("02", m);

            yield return Shot("03", State03());

            m = State03();
            m.SetSwitch(SwitchId.DrillDown, true); // captured while still held
            Run(m, 1.5);
            yield return Shot("04", m);

            m = State03();
            Hold(m, SwitchId.EmitFwd, 0.2);
            Run(m, 1.8); // 2.0 s after the press
            yield return Shot("05", m);

            m = new MpsModel();
            yield return Shot("06a", m);
            Hold(m, SwitchId.ServoDown, 2.0);
            yield return Shot("06b", m);
            Hold(m, SwitchId.ServoUp, 1.0);
            yield return Shot("06c", m);

            yield return Shot("07", LiftPick()); // extra (style preview): not in the reference set

            Debug.Log("shots: done");
            Application.Quit();
        }

        // Chip on the belt waiting at the raised stopper, pad lowered to D40 = 0, vacuum on (needs Online), lifted 60 mm.
        static MpsModel LiftPick()
        {
            var m = new MpsModel();
            m.AddChip(true);
            foreach (var s in new[] { SwitchId.SupplyFwd, SwitchId.SupplyBwd, SwitchId.EmitFwd, SwitchId.EmitBwd })
            {
                Hold(m, s, 0.1);
                Run(m, 2.0);
            }
            m.SetSwitch(SwitchId.StopperDown, true); // stopper raised (D100.7 raises it; rest = down) so the chip waits
            Hold(m, SwitchId.Conveyor, 6.0);
            Hold(m, SwitchId.ServoDown, 3.1);
            short[] bits = new short[4], words = new short[7];
            m.SetOnline(true);
            PlcMap.Set(bits, PlcMap.Out.StopperDown, true);
            PlcMap.Set(bits, PlcMap.Out.Vacuum, true);
            m.SetCommands(bits, words);
            Run(m, 0.1);
            PlcMap.Set(bits, PlcMap.Out.JogPlus, true);
            words[PlcMap.Out.JogSpeed] = 20;
            m.SetCommands(bits, words);
            Run(m, 0.6);
            PlcMap.Set(bits, PlcMap.Out.JogPlus, false);
            m.SetCommands(bits, words);
            return m;
        }

        // Metal, supply-forward switch held 0.7 s and released, 2.5 s in all: chip on the drill stage.
        static MpsModel State03()
        {
            var m = new MpsModel();
            m.AddChip(true);
            Hold(m, SwitchId.SupplyFwd, 0.7);
            Run(m, 1.8);
            return m;
        }

        static void Run(MpsModel m, double s)
        {
            for (int i = 0; i < (int)System.Math.Round(s / 0.01); i++) m.Step(0.01);
        }

        static void Hold(MpsModel m, SwitchId id, double s)
        {
            m.SetSwitch(id, true);
            Run(m, s);
            m.SetSwitch(id, false);
        }

        IEnumerator Shot(string name, MpsModel m)
        {
            app.Model = m;
            var panel = GetComponent<StatusPanel>();
            int r0 = panel.Repaints;
            yield return null;
            yield return null;
            string path = Path.Combine(Dir, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            if (panel.Repaints == r0)
            {
                // The desktop is not presented (locked or disconnected remote session): no frame and no OnGUI, ScreenCapture
                // would be blank. Render the camera offscreen instead; the IMGUI panel is missing from this capture.
                int w = Screen.width, h = Screen.height;
                var rt = RenderTexture.GetTemporary(w, h, 24);
                var cam = Camera.main;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                Debug.Log("shot " + name + ": offscreen 3D only (desktop not presented, no panel)");
            }
            else ScreenCapture.CaptureScreenshot(path);
            for (int i = 0; i < 200 && !File.Exists(path); i++) yield return null;
            yield return null;
            Debug.Log("shot " + name + " D40=" + m.D40 + " screen=" + Screen.width + "x" + Screen.height);
        }
    }
}
