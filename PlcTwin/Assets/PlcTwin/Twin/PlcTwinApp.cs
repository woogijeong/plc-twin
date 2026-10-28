using System;
using System.IO;
using PlcTwin.Mc;
using PlcTwin.Sim;
using UnityEngine;

namespace PlcTwin.Twin
{
    /// <summary>
    /// App glue (plan Step 5): loads plc_twin_config.json, owns the model and the poller, starts Disconnected (offline: the
    /// 3D switches drive the line). Update runs TwinTick. Transport/IP/port edits are session-only (OQ-6). On quit the
    /// poller stops and nothing is written (OQ-7: D0-D3/D40-D44 keep the last written values).
    /// Style preview: -style a|b|c (TwinStyle). Start view: -view default|front|iso|top (Views; the panel buttons switch it). Test-only arguments: -viewCam px,py,pz,ex,ey,ez,size (camera preview), -mpsShot dir (ShotScenario), -mpsAutoConnect transport:ip:port, -mpsQuitAfter seconds.
    /// </summary>
    public sealed class PlcTwinApp : MonoBehaviour
    {
        /// <summary>The reference layout size; the camera and the panel scale to keep it in view (OQ-8: resizable window).</summary>
        public const float RefW = 1387, RefH = 1000;

        /// <summary>Camera presets (panel buttons, -view): position, Euler angles, ortho size at the reference size. All orthographic;
        /// every preset frames the whole line between the word rows and the bottom row of the panel (ViewPresetTests).</summary>
        public static readonly (string name, Vector3 pos, Vector3 euler, float size)[] Views =
        {
            ("Default", new Vector3(-0.852f, 0.628f, -0.460f), new Vector3(25, 65, 0), 0.64f), // front 3/4 (chosen from view_preview2)
            ("Front", new Vector3(-1.169f, 0.435f, 0.006f), new Vector3(15, 90, 0), 0.446f),
            ("Iso", new Vector3(-0.766f, 0.896f, -0.766f), new Vector3(35, 45, 0), 0.774f),
            ("Top", new Vector3(0.146f, 1.154f, 0.006f), new Vector3(90, 90, 0), 0.52f),
        };
        [NonSerialized] public int View;

        public MpsModel Model = new MpsModel();
        public PlcPoller Poller { get; private set; }
        public TwinConfig Config { get; private set; }
        public string ConfigError { get; private set; }
        [NonSerialized] public string Transport = "mc3e", Ip = "", PortText = "";
        [NonSerialized] public string UiError; // bad port text etc.
        [NonSerialized] public bool PanelVisible = true;
        /// <summary>ShotScenario steps the model itself with a fixed dt.</summary>
        [NonSerialized] public bool Frozen;

        Camera cam;
        float quitAfter, autoConnectAt = -1, maxFrameMs;
        float viewT = 1, viewSize = Views[0].size, fromSize;
        Vector3 fromTarget;
        Quaternion fromRot;
        string autoConnect;
        PlcState lastState = PlcState.Disconnected;

        void Awake()
        {
            cam = Camera.main;
            var (cfg, err) = TwinConfig.Load(Path.Combine(Application.streamingAssetsPath, "plc_twin_config.json"));
            Config = cfg;
            ConfigError = err;
            if (cfg != null)
            {
                PanelVisible = cfg.panelVisible;
                SelectTransport(cfg.transport);
            }
            else Debug.LogError("CONFIG ERROR: " + err);

            TwinStyle.Apply(Arg("-style") ?? "a",this, GetComponent<MpsView>(), cam);
            int v = Array.FindIndex(Views, x => string.Equals(x.name, Arg("-view"), StringComparison.OrdinalIgnoreCase));
            if (v >= 0)
            {
                View = v;
                cam.transform.SetPositionAndRotation(Views[v].pos, Quaternion.Euler(Views[v].euler));
                viewSize = Views[v].size;
            }
            // Preview-only: -viewCam px,py,pz,ex,ey,ez,size (invariant culture) overrides the start camera.
            string vc = Arg("-viewCam");
            var vcs = vc?.Split(',');
            var f = new float[7];
            if (vcs != null && vcs.Length == 7 && Array.TrueForAll(vcs, s => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
            {
                for (int i = 0; i < 7; i++) f[i] = float.Parse(vcs[i], System.Globalization.CultureInfo.InvariantCulture);
                cam.transform.SetPositionAndRotation(new Vector3(f[0], f[1], f[2]), Quaternion.Euler(f[3], f[4], f[5]));
                viewSize = f[6];
            }
            string shot = Arg("-mpsShot");
            if (shot != null) gameObject.AddComponent<ShotScenario>().Dir = shot;
            autoConnect = Arg("-mpsAutoConnect");
            if (autoConnect != null) autoConnectAt = 1f;
            if (float.TryParse(Arg("-mpsQuitAfter"), out float q)) quitAfter = q;
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        /// <summary>Fills IP and port from the config section of the transport (session-only).</summary>
        public void SelectTransport(string t)
        {
            Transport = t;
            if (Config == null) return;
            Ip = t == "modbus" ? Config.modbus.ip : Config.mc3e.ip;
            PortText = (t == "modbus" ? Config.modbus.port : Config.mc3e.port).ToString();
        }

        public void Connect()
        {
            if (Poller != null || Config == null) return;
            if (!int.TryParse(PortText, out int port) || port < 1 || port > 65535) { UiError = "port '" + PortText + "' must be 1..65535"; return; }
            UiError = null;
            var (pc, connect) = Config.ToPollerConfig(Transport, Ip.Trim(), port);
            var p = new PlcPoller(pc, connect);
            TwinTick.Tick(Model, p, 0); // sets both write blocks in one batch before Start
            p.Start();
            Poller = p;
        }

        /// <summary>Starts a 0.4 s move to view preset i (look point, rotation and ortho size eased; unscaled time).</summary>
        public void SetView(int i)
        {
            fromTarget = cam.transform.position + cam.transform.forward;
            fromRot = cam.transform.rotation;
            fromSize = viewSize;
            View = i;
            viewT = 0;
        }

        /// <summary>Back to the power-on state: every chip gone (MpsView drops the pooled ones), cylinders and servo at rest.</summary>
        public void ResetLine() => Model = new MpsModel();

        public void Disconnect()
        {
            Poller?.Stop();
            Poller = null;
        }

        void Update()
        {
            if (!Frozen) TwinTick.Tick(Model, Poller, Math.Min(Time.deltaTime, 0.05));
            if (viewT < 1)
            {
                // Ease the look point (1 m in front of the camera) rather than the position: the camera stays 1 m back from it.
                viewT = Mathf.Min(1, viewT + Time.unscaledDeltaTime / 0.4f);
                float k = Mathf.SmoothStep(0, 1, viewT);
                var to = Views[View];
                var rot = Quaternion.Slerp(fromRot, Quaternion.Euler(to.euler), k);
                var target = Vector3.Lerp(fromTarget, to.pos + Quaternion.Euler(to.euler) * Vector3.forward, k);
                cam.transform.SetPositionAndRotation(target - rot * Vector3.forward, rot);
                viewSize = Mathf.Lerp(fromSize, to.size, k);
            }
            // Keep the RefW x RefH area in view at any window size (view size = half height of the reference area).
            cam.orthographicSize = viewSize * Mathf.Max(1f, RefW / RefH / cam.aspect);
            Smoke();
        }

        // AC13 smoke support: log state changes, the max frame time after the auto-connect, and quit after N seconds.
        void Smoke()
        {
            var s = Poller?.State ?? PlcState.Disconnected;
            if (s != lastState) Debug.Log("smoke: t=" + Time.time.ToString("0.00") + " state=" + s + (s == PlcState.Error ? " Error: " + Poller.LastError : ""));
            lastState = s;
            if (autoConnectAt >= 0 && Time.time >= autoConnectAt)
            {
                autoConnectAt = -1;
                var parts = autoConnect.Split(':');
                SelectTransport(parts[0]);
                Ip = parts[1];
                PortText = parts[2];
                Connect();
                Debug.Log("smoke: auto-connect " + autoConnect);
            }
            else if (autoConnect != null && autoConnectAt < 0 && Poller != null)
                maxFrameMs = Mathf.Max(maxFrameMs, Time.unscaledDeltaTime * 1000f);
            if (quitAfter > 0 && Time.time >= quitAfter)
            {
                quitAfter = 0;
                Debug.Log("smoke: done t=" + Time.time.ToString("0.00") + " state=" + s
                          + " maxFrameMsAfterConnect=" + maxFrameMs.ToString("0.0") + " panelVisible=" + PanelVisible
                          + " panelFrames=" + GetComponent<StatusPanel>().Repaints + " screen=" + Screen.width + "x" + Screen.height
                          + " lastError=" + Poller?.LastError?.Trim('\0', ' ', '\r', '\n'));
                Application.Quit();
            }
        }

        void OnApplicationQuit() => Disconnect();
        void OnDestroy() => Disconnect();
    }
}
