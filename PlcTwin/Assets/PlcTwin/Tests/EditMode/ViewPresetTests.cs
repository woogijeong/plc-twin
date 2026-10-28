using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlcTwin.Sim;
using PlcTwin.Twin;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PlcTwin.Tests
{
    /// <summary>
    /// Camera view presets: every preset frames the model (corners of the renderer bounds in Main.unity, rest pose) at 1387x1000
    /// and 1000x700 with the same ortho math as PlcTwinApp. Every preset keeps it between the word rows and the bottom row of the
    /// panel and off Metal/Nonmetal.
    /// </summary>
    public class ViewPresetTests
    {
        List<Vector3> corners;
        UnityEngine.SceneManagement.Scene scene;

        [OneTimeSetUp]
        public void Open()
        {
            scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Additive);
            corners = new List<Vector3>();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                    corners.Add(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
            }
        }

        [OneTimeTearDown] public void Close() => EditorSceneManager.CloseScene(scene, true);

        [Test]
        public void NamesMatchPanelButtons() =>
            Assert.AreEqual(PanelLayout.Views, PlcTwinApp.Views.Select(v => v.name).ToArray());

        [TestCase(1387, 1000)]
        [TestCase(1000, 700)]
        public void EveryPresetFramesTheModel(int w, int h)
        {
            Assert.Greater(corners.Count, 100);
            var buttons = PanelLayout.Build().Where(c => (c.Kind == PanelKind.Button || c.Lines.FirstOrDefault() == PanelLayout.Reset) && c.Cy > 0).ToList(); // Metal, Nonmetal, Reset
            for (int k = 0; k < PlcTwinApp.Views.Length; k++)
            {
                var v = PlcTwinApp.Views[k];
                var inv = Quaternion.Inverse(Quaternion.Euler(v.euler));
                float size = v.size * Mathf.Max(1f, PlcTwinApp.RefW / PlcTwinApp.RefH / ((float)w / h));
                float s = Mathf.Min(w / PlcTwinApp.RefW, h / PlcTwinApp.RefH); // panel scale
                float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
                foreach (var p in corners)
                {
                    var q = inv * (p - v.pos);
                    Assert.Greater(q.z, 0.3f,v.name + ": in front of the near plane");
                    // Pixels from the screen center, then reference (panel layout) units.
                    float x = q.x * (h / 2f) / size / s, y = q.y * (h / 2f) / size / s;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                    foreach (var c in buttons)
                            Assert.IsFalse(2 * Mathf.Abs(x - c.Cx) < c.W && 2 * Mathf.Abs(y - c.Cy) < c.H, v.name + " under " + c.Lines[0]);
                }
                string ext = v.name + " x " + x0.ToString("0") + ".." + x1.ToString("0") + " y " + y0.ToString("0") + ".." + y1.ToString("0");
                Assert.LessOrEqual(Mathf.Max(-x0, x1), PlcTwinApp.RefW / 2, ext);
                Assert.LessOrEqual(Mathf.Max(-y0, y1), PlcTwinApp.RefH / 2, ext);
                Assert.GreaterOrEqual(y0, -413f, ext + " (bottom row / state line)");
                Assert.LessOrEqual(y1, 120f, ext + " (word value row)");
                Assert.Greater(x1 - x0, 700f, ext + " (fills the view)");
            }
        }
    }
}
