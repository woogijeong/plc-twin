using System.Collections.Generic;
using PlcTwin.Mc;
using PlcTwin.Sim;
using UnityEngine;

namespace PlcTwin.Twin
{
    /// <summary>
    /// IMGUI status panel (plan Step 6) drawn from PanelLayout: 45 bit cells, 9 word cells (title + value), Metal/Nonmetal,
    /// Connect/Disconnect, the Panel toggle (OQ-5, display only), view preset buttons, transport selector, IP/port fields and a state line.
    /// Center-anchored like the reference; the whole layout scales uniformly with the window (OQ-8).
    /// </summary>
    public sealed class StatusPanel : MonoBehaviour
    {
        public PlcTwinApp App;
        public int Repaints { get; private set; }

        List<PanelCell> cells;
        int inBitsD, inWordsD, outBitsD, outWordsD;
        readonly Dictionary<int, Texture2D> tex = new Dictionary<int, Texture2D>();
        readonly short[] inBits = new short[4], inWords = new short[5], outBits = new short[4], outWords = new short[7];
        GUIStyle label, value, button, small, field, state;

        static float Scale => Mathf.Min(Screen.width / PlcTwinApp.RefW, Screen.height / PlcTwinApp.RefH);

        void Start()
        {
            var c = App.Config;
            inBitsD = c != null ? DeviceAddress.Parse(c.map.inBits).Number : 0;
            inWordsD = c != null ? DeviceAddress.Parse(c.map.inWords).Number : 40;
            outBitsD = c != null ? DeviceAddress.Parse(c.map.outBits).Number : 100;
            outWordsD = c != null ? DeviceAddress.Parse(c.map.outWords).Number : 140;
            cells = PanelLayout.Build(inBitsD, inWordsD, outBitsD, outWordsD);
        }

        /// <summary>Screen rect (pixels, top-left origin) of a layout cell at the current window size.</summary>
        public static Rect ScreenRect(PanelCell c)
        {
            float s = Scale;
            return new Rect(Screen.width / 2f + (c.Cx - c.W / 2f) * s, Screen.height / 2f - (c.Cy + c.H / 2f) * s, c.W * s, c.H * s);
        }

        /// <summary>True when the mouse (Input.mousePosition, bottom-left origin) is over a drawn widget.</summary>
        public bool IsOverUi(Vector2 mouse)
        {
            var p = new Vector2(mouse.x, Screen.height - mouse.y);
            foreach (var c in cells)
                if ((App.PanelVisible || c.Kind == PanelKind.Button || c.Kind == PanelKind.Extra) && ScreenRect(c).Contains(p)) return true;
            return false;
        }

        Texture2D Tex(int rgb)
        {
            if (tex.TryGetValue(rgb, out var t)) return t;
            t = new Texture2D(1, 1);
            t.SetPixel(0, 0, new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255));
            t.Apply();
            return tex[rgb] = t;
        }

        void Styles()
        {
            if (label != null) return;
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Gulim", "Arial" }, 14);
            var ink = new Color32(50, 50, 50, 255);
            label = new GUIStyle { font = font, fontSize = 14, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            label.normal.textColor = ink;
            value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 4, 0, 0) };
            button = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            button.hover.textColor = button.active.textColor = ink;
            small = new GUIStyle(button) { fontSize = 12 };
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 14, alignment = TextAnchor.MiddleLeft };
            state = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft };
        }

        // Virtual (unscaled) rect: GUI.matrix scales it to the screen.
        static Rect V(PanelCell c, float vw, float vh) => new Rect(vw / 2f + c.Cx - c.W / 2f, vh / 2f - c.Cy - c.H / 2f, c.W, c.H);

        void Fill(Rect r, int rgb)
        {
            GUI.DrawTexture(r, Tex(Darker(rgb)));
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), Tex(rgb));
        }

        static int Darker(int rgb) => (int)((rgb >> 16 & 255) * 0.8f) << 16 | (int)((rgb >> 8 & 255) * 0.8f) << 8 | (int)((rgb & 255) * 0.8f);

        void OnGUI()
        {
            if (cells == null) return;
            Styles();
            if (Event.current.type == EventType.Repaint) Repaints++;
            float s = Scale, vw = Screen.width / s, vh = Screen.height / s;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            var m = App.Model;
            m.ComposeInputs(inBits, inWords);
            m.CopyLastCommands(outBits, outWords);
            bool disconnected = App.Poller == null;

            foreach (var c in cells)
            {
                var r = V(c, vw, vh);
                switch (c.Kind)
                {
                    case PanelKind.Bit:
                        if (!App.PanelVisible) break;
                        var words = c.Input ? inBits : outBits;
                        Fill(r, PlcMap.Get(words, (c.D - (c.Input ? inBitsD : outBitsD)) * 16 + c.Bit) ? PanelLayout.OnColor : PanelLayout.OffColor);
                        for (int i = 0; i < 3; i++) GUI.Label(new Rect(r.x, r.y + 5 + 16.5f * i, r.width, 16), c.Lines[i], label);
                        break;
                    case PanelKind.WordTitle:
                        if (!App.PanelVisible) break;
                        Fill(r, PanelLayout.TitleColor);
                        for (int i = 0; i < 2; i++) GUI.Label(new Rect(r.x, r.y + 3 + 16.5f * i, r.width, 16), c.Lines[i], label);
                        break;
                    case PanelKind.WordValue:
                        if (!App.PanelVisible) break;
                        Fill(r, PanelLayout.ValueColor);
                        short v = c.Input ? inWords[c.D - inWordsD] : outWords[c.D - outWordsD];
                        GUI.Label(r, v.ToString(), value);
                        break;
                    default:
                        Widget(c, r, disconnected);
                        break;
                }
            }
        }

        void Widget(PanelCell c, Rect r, bool disconnected)
        {
            var app = App;
            switch (c.Lines[0])
            {
                case PanelLayout.Metal:
                    Fill(r, PanelLayout.MetalColor);
                    if (GUI.Button(r, "Metal", button)) app.Model.AddChip(true);
                    break;
                case PanelLayout.Nonmetal:
                    Fill(r, PanelLayout.NonmetalColor);
                    if (GUI.Button(r, "Nonmetal", button)) app.Model.AddChip(false);
                    break;
                case PanelLayout.Reset: // offline only: a connected PLC owns the line
                    Fill(r, PanelLayout.IdleButtonColor);
                    GUI.enabled = disconnected;
                    if (GUI.Button(r, "Reset", button)) app.ResetLine();
                    GUI.enabled = true;
                    break;
                case PanelLayout.Connect:
                    Fill(r, disconnected && app.Config != null ? PanelLayout.ConnectColor : PanelLayout.IdleButtonColor);
                    if (GUI.Button(r, "Connect", button)) app.Connect();
                    break;
                case PanelLayout.Disconnect:
                    Fill(r, disconnected ? PanelLayout.IdleButtonColor : PanelLayout.DisconnectColor);
                    if (GUI.Button(r, "Disconnect", button)) app.Disconnect();
                    break;
                case PanelLayout.PanelToggle:
                    Fill(r, app.PanelVisible ? PanelLayout.PressedColor : PanelLayout.IdleButtonColor);
                    if (GUI.Button(r, "Panel", button)) app.PanelVisible = !app.PanelVisible;
                    break;
                case PanelLayout.Transport:
                    Fill(r, PanelLayout.IdleButtonColor);
                    GUI.enabled = disconnected;
                    if (GUI.Button(r, app.Transport == "modbus" ? "Modbus" : "MC 3E", button))
                        app.SelectTransport(app.Transport == "modbus" ? "mc3e" : "modbus");
                    GUI.enabled = true;
                    break;
                case PanelLayout.Ip:
                    GUI.enabled = disconnected;
                    app.Ip = GUI.TextField(r, app.Ip, 15, field);
                    GUI.enabled = true;
                    break;
                case PanelLayout.Port:
                    GUI.enabled = disconnected;
                    app.PortText = GUI.TextField(r, app.PortText, 5, field);
                    GUI.enabled = true;
                    break;
                case PanelLayout.StateLine:
                    GUI.Label(r, StateText(app), state);
                    break;
                default: // view presets
                    int v = System.Array.IndexOf(PanelLayout.Views, c.Lines[0]);
                    if (v < 0) break;
                    Fill(r, app.View == v ? PanelLayout.PressedColor : PanelLayout.IdleButtonColor);
                    if (GUI.Button(r, c.Lines[0], small)) app.SetView(v);
                    break;
            }
        }

        static string StateText(PlcTwinApp app)
        {
            if (app.ConfigError != null) return "CONFIG ERROR: " + app.ConfigError;
            if (app.UiError != null) return "Error: " + app.UiError;
            var p = app.Poller;
            if (p == null) return "State: Disconnected (offline, 3D switches active)";
            string where = (app.Transport == "modbus" ? "Modbus " : "MC 3E ") + app.Ip + ":" + app.PortText;
            switch (p.State)
            {
                case PlcState.Connected: return "State: Connected (" + where + ") rtt " + p.RttMs + " ms";
                case PlcState.Error: return "Error: " + p.LastError + " (" + where + ", retrying)";
                default: return "State: " + p.State + " (" + where + ")";
            }
        }
    }
}
