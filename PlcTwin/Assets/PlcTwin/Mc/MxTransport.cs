using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PlcTwin.Mc
{
    /// <summary>MxHelper reported an error (an MX Component return code in hex, or a protocol error).</summary>
    public sealed class MxException : Exception
    {
        public MxException(string message) : base(message) { }
    }

    /// <summary>
    /// D-word transport through MX Component (e.g. the GX Simulator2 logical station). MX Component is a 32-bit COM
    /// server and the player is 64-bit, so a 32-bit child process (tools/mxhelper/MxHelper.cs, StreamingAssets/MxHelper.exe)
    /// does the COM calls. Line protocol: "OPEN n", "R d n", "W d v..."; each gets exactly one reply line "OK ..." or "ERR ...".
    /// One request in flight. Any failure kills the helper for good (same contract as the other transports). The helper
    /// exits by itself when our stdin closes, so a crashed player cannot leave a connection open.
    /// </summary>
    public sealed class MxTransport : IPlcTransport
    {
        readonly Process proc;
        readonly int timeoutMs;
        readonly object io = new object();

        MxTransport(Process p, int timeoutMs) { proc = p; this.timeoutMs = timeoutMs; }

        /// <summary>Starts the helper and opens the logical station. fileName/args are injectable for tests.</summary>
        public static MxTransport Connect(string fileName, string args, int station, int connectTimeoutMs, int timeoutMs, WaitHandle stop)
        {
            if (!File.Exists(fileName)) throw new FileNotFoundException("MX helper not found: " + fileName);
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.ASCII,
            };
            var t = new MxTransport(Process.Start(psi), timeoutMs);
            try
            {
                t.Exchange(BuildOpen(station), connectTimeoutMs, stop);
                return t;
            }
            catch { t.Close(); throw; }
        }

        public static string BuildOpen(int station) => "OPEN " + station.ToString(CultureInfo.InvariantCulture);
        public static string BuildRead(int d, int count) => "R " + d.ToString(CultureInfo.InvariantCulture) + " " + count.ToString(CultureInfo.InvariantCulture);

        public static string BuildWrite(int d, short[] values)
        {
            var sb = new StringBuilder("W ").Append(d.ToString(CultureInfo.InvariantCulture));
            foreach (var v in values) sb.Append(' ').Append(v.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Words of an "OK v1 v2 ..." reply; throws MxException for "ERR ..." or anything else.</summary>
        public static short[] ParseReply(string reply)
        {
            if (reply == null) throw new MxException("helper closed");
            var p = reply.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0 || p[0] != "OK") throw new MxException(reply.Trim());
            var w = new short[p.Length - 1];
            for (int i = 0; i < w.Length; i++)
                if (!short.TryParse(p[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out w[i])) throw new MxException("bad word '" + p[i + 1] + "'");
            return w;
        }

        public short[] ReadWords(int d, int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var w = Exchange(BuildRead(d, count), timeoutMs, null);
            if (w.Length != count) { Close(); throw new MxException("short reply: " + w.Length + " of " + count + " words"); }
            return w;
        }

        public void WriteWords(int d, short[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length < 1) throw new ArgumentOutOfRangeException(nameof(values));
            Exchange(BuildWrite(d, values), timeoutMs, null);
        }

        short[] Exchange(string request, int waitMs, WaitHandle stop)
        {
            lock (io)
            {
                try
                {
                    proc.StandardInput.WriteLine(request);
                    proc.StandardInput.Flush();
                    var line = proc.StandardOutput.ReadLineAsync();
                    // Wait in slices so a stop request (Disconnect) ends a slow OPEN promptly.
                    var deadline = Environment.TickCount + waitMs;
                    while (!line.Wait(50))
                    {
                        if (stop != null && stop.WaitOne(0)) throw new OperationCanceledException("stopped");
                        if (Environment.TickCount - deadline >= 0) throw new TimeoutException("timeout");
                    }
                    return ParseReply(line.Result);
                }
                catch { Close(); throw; }
            }
        }

        /// <summary>Safe from any thread; unblocks a pending read.</summary>
        public void Close()
        {
            try { proc.StandardInput.WriteLine("Q"); proc.StandardInput.Flush(); } catch (Exception) { }
            try { if (!proc.WaitForExit(500)) proc.Kill(); } catch (Exception) { }
        }

        public void Dispose() { Close(); proc.Dispose(); }
    }
}
