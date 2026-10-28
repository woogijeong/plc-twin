using PlcTwin.Sim;
using UnityEditor;
using UnityEngine;

namespace PlcTwin.EditorTools
{
    /// <summary>
    /// Manual test window (plan Step 5): runs a fake MC or fake Modbus server on 127.0.0.1, edits D100-D103 (64 bits) and
    /// D140-D146, shows D0-D3/D40-D44 as written by the app. Point the app at 127.0.0.1 and the port shown here.
    /// </summary>
    public sealed class FakePlcWindow : EditorWindow
    {
        FakePlcServer fake;
        bool modbus;
        int port = 5010;
        Vector2 scroll;

        [MenuItem("PlcTwin/Fake PLC")]
        static void Open() => GetWindow<FakePlcWindow>("Fake PLC");

        void OnDisable() { fake?.Stop(); fake = null; }

        void OnGUI()
        {
            if (fake == null)
            {
                modbus = EditorGUILayout.Toggle("Modbus (else MC 3E)", modbus);
                port = EditorGUILayout.IntField("Port", port);
                if (GUILayout.Button("Start")) fake = modbus ? (FakePlcServer)new FakeModbusServer(port) : new FakeMcServer(port);
                return;
            }
            EditorGUILayout.LabelField((modbus ? "Modbus" : "MC 3E") + " fake on 127.0.0.1:" + fake.Port + ", connections " + fake.Connections);
            if (GUILayout.Button("Stop")) { fake.Stop(); fake = null; return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var cells = PanelLayout.Build();
            foreach (var c in cells)
            {
                if (c.Kind == PanelKind.Bit)
                {
                    short w = fake.GetWord(c.D);
                    bool on = (w >> c.Bit & 1) != 0;
                    string text = c.Lines[0] + " " + c.Lines[1] + " " + c.Lines[2];
                    if (c.Input) { EditorGUILayout.LabelField(text, on ? "ON" : "off"); continue; }
                    bool v = EditorGUILayout.ToggleLeft(text, on);
                    if (v != on) fake.SetWord(c.D, (short)(v ? w | 1 << c.Bit : w & ~(1 << c.Bit)));
                }
                else if (c.Kind == PanelKind.WordTitle)
                {
                    string text = c.Lines[0] + " " + c.Lines[1];
                    if (c.Input) EditorGUILayout.LabelField(text, fake.GetWord(c.D).ToString());
                    else fake.SetWord(c.D, (short)EditorGUILayout.IntField(text, fake.GetWord(c.D)));
                }
            }
            EditorGUILayout.EndScrollView();
            Repaint();
        }
    }
}
