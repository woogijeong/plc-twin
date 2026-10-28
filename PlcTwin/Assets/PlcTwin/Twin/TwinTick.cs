using System;
using System.Linq;
using PlcTwin.Mc;
using PlcTwin.Sim;

namespace PlcTwin.Twin
{
    /// <summary>
    /// Per-frame glue between MpsModel and PlcPoller (plain C#, no UnityEngine). PlcTwinApp.Update and LineIntegrationTests
    /// both call it, so the tested glue is the shipped glue. Block roles follow TwinConfig.ToPollerConfig:
    /// ReadBlocks = (outBits, outWords), WriteBlocks = (inWords, inBits).
    /// </summary>
    public static class TwinTick
    {
        /// <summary>p may be null (Disconnected, no poller): the model only steps, offline.</summary>
        public static void Tick(MpsModel m, PlcPoller p, double dt)
        {
            bool online = p != null && p.State == PlcState.Connected;
            m.SetOnline(online); // OQ-2: any state but Connected is offline; a link drop is felt before the next step
            var s = online ? p.Snapshot() : null; // one snapshot: all commands come from the same read
            if (s != null)
            {
                var rb = p.Config.ReadBlocks;
                int first = rb.Min(b => b.d);
                m.SetCommands(Slice(s, rb[0].d - first, 4), Slice(s, rb[1].d - first, 7));
            }
            m.Step(dt);
            if (p == null) return;
            short[] bits = new short[4], words = new short[5];
            m.ComposeInputs(bits, words);
            p.SetWords((p.Config.WriteBlocks[0].d, words), (p.Config.WriteBlocks[1].d, bits)); // one batch: D40 and D0 consistent
        }

        static short[] Slice(short[] s, int at, int n)
        {
            var w = new short[n];
            Array.Copy(s, at, w, 0, n);
            return w;
        }
    }
}
