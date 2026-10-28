using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using PlcTwin.Mc;
using UnityEngine;

namespace PlcTwin.Twin
{
    [Serializable] public class Mc3eCfg { public string ip; public int port, monitorTimer250ms, pollMs; }
    [Serializable] public class ModbusCfg { public string ip; public int port, unitId, pollMs, writeTableBase, readTableBase; }
    [Serializable] public class MapCfg { public string inBits, inWords, outBits, outWords; }

    [Serializable]
    public class TwinConfig
    {
        // Block lengths are fixed by the reference map; only the base addresses are configurable.
        public const int InBitsLen = 4, InWordsLen = 5, OutBitsLen = 4, OutWordsLen = 7;

        public string _help; // documentation only, ignored
        public string transport;
        public Mc3eCfg mc3e;
        public ModbusCfg modbus;
        public int connectTimeoutMs, timeoutMs;
        public MapCfg map;
        public bool panelVisible;

        /// <summary>Never throws. Exactly one of config/error is non-null.</summary>
        public static (TwinConfig config, string error) Load(string path)
        {
            try { return Parse(File.ReadAllText(path)); }
            catch (Exception e) { return (null, "config file '" + path + "': " + e.Message); }
        }

        public static (TwinConfig config, string error) Parse(string json)
        {
            try
            {
                var c = JsonUtility.FromJson<TwinConfig>(json);
                if (c == null) return (null, "config is empty");
                // JsonUtility ignores unknown fields, so the old format is recognized from the text.
                if (string.IsNullOrEmpty(c.transport) || Regex.IsMatch(json, "\"(conveyor|tower|sensor)\"\\s*:"))
                    return (null, "old config format (conveyor/tower/sensor) is no longer supported; see README.txt");
                var err = c.Validate();
                return err == null ? (c, null) : (null, err);
            }
            catch (Exception e) { return (null, "config parse: " + e.Message); }
        }

        /// <summary>
        /// Poller settings plus the transport factory for the chosen transport ("mc3e" or "modbus"). transport, ip and port
        /// are the UI overrides (nothing is written back to the file); everything else comes from this config.
        /// Reads: outBits, outWords. Writes: inWords (D40) first, then inBits (D0).
        /// </summary>
        public (PlcPollerConfig poller, Func<WaitHandle, IPlcTransport> connect) ToPollerConfig(string transport, string ip, int port)
        {
            int inBits = Dev(map.inBits), inWords = Dev(map.inWords), outBits = Dev(map.outBits), outWords = Dev(map.outWords);
            var cfg = new PlcPollerConfig
            {
                PollMs = transport == "modbus" ? modbus.pollMs : mc3e.pollMs,
                RewriteMs = transport == "modbus" ? 1000 : 0, // ponytail: bridge costs ~310 ms/request; resend unchanged blocks about once a second
                ReadBlocks = new System.Collections.Generic.List<(int d, int count)> { (outBits, OutBitsLen), (outWords, OutWordsLen) },
                WriteBlocks = new System.Collections.Generic.List<(int d, int count)> { (inWords, InWordsLen), (inBits, InBitsLen) },
            };
            if (transport != "mc3e" && transport != "modbus") throw new ArgumentException("transport '" + transport + "' must be mc3e or modbus", nameof(transport));
            Func<WaitHandle, IPlcTransport> connect = transport == "modbus"
                ? stop => ModbusTransport.Connect(ip, port, connectTimeoutMs, timeoutMs, (byte)modbus.unitId, modbus.writeTableBase, modbus.readTableBase, stop)
                : (Func<WaitHandle, IPlcTransport>)(stop => McTransport.Connect(ip, port, connectTimeoutMs, timeoutMs, mc3e.monitorTimer250ms, stop));
            return (cfg, connect);
        }

        static int Dev(string d) => DeviceAddress.Parse(d).Number;

        string Validate()
        {
            if (transport != "mc3e" && transport != "modbus") return "transport '" + transport + "' must be mc3e or modbus";
            if (mc3e == null) return "mc3e section is required";
            if (modbus == null) return "modbus section is required";
            if (map == null) return "map section is required";
            var err = CheckIp("mc3e.ip", mc3e.ip) ?? CheckPort("mc3e.port", mc3e.port) ?? CheckIp("modbus.ip", modbus.ip) ?? CheckPort("modbus.port", modbus.port);
            if (err != null) return err;
            if (modbus.unitId < 0 || modbus.unitId > 255) return "modbus.unitId " + modbus.unitId + " must be 0..255";
            if (mc3e.pollMs < 10) return "mc3e.pollMs " + mc3e.pollMs + " must be >= 10";
            if (modbus.pollMs < 10) return "modbus.pollMs " + modbus.pollMs + " must be >= 10";
            if (connectTimeoutMs <= 0) return "connectTimeoutMs " + connectTimeoutMs + " must be > 0";
            if (mc3e.monitorTimer250ms < 1) return "mc3e.monitorTimer250ms " + mc3e.monitorTimer250ms + " must be >= 1";
            if (timeoutMs < mc3e.monitorTimer250ms * 250 + 500) return "timeoutMs must be >= mc3e.monitorTimer250ms*250+500 (" + (mc3e.monitorTimer250ms * 250 + 500) + ")";

            // Modbus table ranges are validated even when transport is mc3e, because the UI can switch transport at runtime.
            if (modbus.writeTableBase < 0) return "modbus.writeTableBase " + modbus.writeTableBase + " must be >= 0";
            if (modbus.readTableBase < 0) return "modbus.readTableBase " + modbus.readTableBase + " must be >= 0";

            var names = new[] { "map.inBits", "map.inWords", "map.outBits", "map.outWords" };
            var lens = new[] { InBitsLen, InWordsLen, OutBitsLen, OutWordsLen };
            var start = new int[4];
            var devs = new[] { map.inBits, map.inWords, map.outBits, map.outWords };
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var a = DeviceAddress.Parse(devs[i]);
                    if (a.Type != 'D') return names[i] + " '" + devs[i] + "' must be a D device";
                    start[i] = a.Number;
                }
                catch (FormatException e) { return names[i] + ": " + e.Message; }
            }
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (start[i] < start[j] + lens[j] && start[j] < start[i] + lens[i])
                        return names[i] + " and " + names[j] + " overlap (D" + start[i] + "+" + lens[i] + ", D" + start[j] + "+" + lens[j] + ")";

            int readSpan = Math.Max(start[2] + OutBitsLen, start[3] + OutWordsLen) - Math.Min(start[2], start[3]);
            if (readSpan > McFrame.MaxWords) return "map.outBits/map.outWords read span " + readSpan + " words (max " + McFrame.MaxWords + ")";
            for (int i = 0; i < 4; i++)
            {
                int tb = i < 2 ? modbus.writeTableBase : modbus.readTableBase;
                if (start[i] < tb || start[i] + lens[i] > tb + ModbusTransport.TableWords)
                    return names[i] + " D" + start[i] + "+" + lens[i] + " is outside the modbus " + (i < 2 ? "writeTableBase" : "readTableBase")
                         + " table [D" + tb + ", D" + (tb + ModbusTransport.TableWords) + ")";
            }
            return null;
        }

        static string CheckIp(string name, string ip) =>
            string.IsNullOrEmpty(ip) || ip.Split('.').Length != 4 || !IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork
                ? name + " '" + ip + "' must be a numeric IPv4 address (a.b.c.d)" : null;

        static string CheckPort(string name, int port) => port < 1 || port > 65535 ? name + " " + port + " must be 1..65535" : null;
    }
}
