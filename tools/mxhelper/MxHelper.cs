// 32-bit bridge between PlcTwin (64-bit Unity) and MX Component (32-bit COM ActUtlType).
// Line protocol on stdin/stdout, one reply per request line:
//   PING                  -> OK            (creates the COM object, does NOT connect)
//   OPEN <station>        -> OK | ERR <hex>   (connects to logical station <station>)
//   R <d> <n>             -> OK v1 v2 ...  (D<d>..D<d+n-1> as signed 16-bit)
//   W <d> v1 v2 ...       -> OK
//   Q                     -> exits (also on stdin EOF: the parent died)
using System;
using System.Globalization;
using System.Text;

static class MxHelper
{
    const int MaxWords = 480;

    static int Main()
    {
        ActUtl.ActUtlType plc = null;
        bool open = false;
        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            var p = line.Trim().Split(' ');
            if (p[0] == "Q") break;
            try { Console.Out.WriteLine(Handle(p, ref plc, ref open)); }
            catch (Exception e) { Console.Out.WriteLine("ERR " + e.GetType().Name + ": " + e.Message.Replace('\n', ' ').Replace('\r', ' ')); }
        }
        try { if (open) plc.Close(); } catch (Exception) { }
        return 0;
    }

    static string Handle(string[] p, ref ActUtl.ActUtlType plc, ref bool open)
    {
        switch (p[0])
        {
            case "PING":
                if (plc == null) plc = new ActUtl.ActUtlType();
                return "OK";
            case "OPEN":
            {
                if (open) return "ERR already open";
                if (plc == null) plc = new ActUtl.ActUtlType();
                plc.ActLogicalStationNumber = int.Parse(p[1], CultureInfo.InvariantCulture);
                int rc = plc.Open();
                open = rc == 0;
                return rc == 0 ? "OK" : "ERR " + rc.ToString("X8");
            }
            case "R":
            {
                if (!open) return "ERR not open";
                int d = int.Parse(p[1], CultureInfo.InvariantCulture), n = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (d < 0 || n < 1 || n > MaxWords) return "ERR bad range";
                var buf = new short[n];
                int rc = plc.ReadDeviceBlock2("D" + d, n, out buf[0]);
                if (rc != 0) return "ERR " + rc.ToString("X8");
                var sb = new StringBuilder("OK");
                foreach (var v in buf) sb.Append(' ').Append(v);
                return sb.ToString();
            }
            case "W":
            {
                if (!open) return "ERR not open";
                int d = int.Parse(p[1], CultureInfo.InvariantCulture), n = p.Length - 2;
                if (d < 0 || n < 1 || n > MaxWords) return "ERR bad range";
                var buf = new short[n];
                for (int i = 0; i < n; i++) buf[i] = short.Parse(p[2 + i], CultureInfo.InvariantCulture);
                int rc = plc.WriteDeviceBlock2("D" + d, n, ref buf[0]);
                return rc == 0 ? "OK" : "ERR " + rc.ToString("X8");
            }
            default: return "ERR unknown command";
        }
    }
}
