using System.Threading;

namespace PlcTwin.Mc
{
    /// <summary>MC 3E binary D-word transport: 0401/0000 batch read, 1401/0000 batch write. Closes itself on any error.</summary>
    public sealed class McTransport : IPlcTransport
    {
        readonly McClient client;
        readonly int timer;

        McTransport(McClient c, int monitorTimer250ms) { client = c; timer = monitorTimer250ms; }

        public static McTransport Connect(string ip, int port, int connectTimeoutMs, int timeoutMs, int monitorTimer250ms, WaitHandle stop) =>
            new McTransport(McClient.Connect(ip, port, connectTimeoutMs, timeoutMs, stop), monitorTimer250ms);

        public short[] ReadWords(int d, int count)
        {
            try { return McFrame.ParseReadWords(client.Request(McFrame.BuildReadWords(D(d), count, timer)), count); }
            catch { client.Close(); throw; }
        }

        public void WriteWords(int d, short[] values)
        {
            try { McFrame.ParseWriteResponse(client.Request(McFrame.BuildWriteWords(D(d), values, timer))); }
            catch { client.Close(); throw; }
        }

        static DeviceAddress D(int d) => DeviceAddress.Parse("D" + d);

        public void Dispose() => client.Close();
    }
}
