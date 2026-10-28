using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PlcTwin.Mc
{
    /// <summary>
    /// Sync 3E binary client, one request in flight. Any timeout or I/O error closes the socket for good:
    /// the caller must discard this instance and connect a new one (a late reply would desync framing).
    /// </summary>
    public sealed class McClient : IDisposable
    {
        readonly Socket sock;
        readonly object io = new object();

        McClient(Socket s) { sock = s; }

        /// <summary>
        /// Connects within connectTimeoutMs, or returns early when stop is set. Throws TimeoutException ("connect timeout"),
        /// OperationCanceledException (stop), SocketException (e.g. refused) or FormatException (bad ip).
        /// </summary>
        public static McClient Connect(string ip, int port, int connectTimeoutMs, int timeoutMs, WaitHandle stop) =>
            new McClient(ConnectSocket(ip, port, connectTimeoutMs, timeoutMs, stop));

        /// <summary>Shared by McClient and ModbusTransport: BeginConnect + WaitAny(stop), send/receive timeouts = timeoutMs.</summary>
        internal static Socket ConnectSocket(string ip, int port, int connectTimeoutMs, int timeoutMs, WaitHandle stop)
        {
            var addr = IPAddress.Parse(ip); // numeric only: no DNS inside the timed window
            var s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true, SendTimeout = timeoutMs, ReceiveTimeout = timeoutMs
            };
            try
            {
                var ar = s.BeginConnect(addr, port, null, null);
                int i = WaitHandle.WaitAny(new[] { ar.AsyncWaitHandle, stop }, connectTimeoutMs);
                if (i != 0)
                {
                    s.Close();
                    // Observe the abandoned connect off-thread so it never blocks us; its ObjectDisposedException is expected.
                    ThreadPool.QueueUserWorkItem(_ => { try { s.EndConnect(ar); } catch (Exception) { } });
                    if (i == 1) throw new OperationCanceledException("stopped");
                    throw new TimeoutException("connect timeout");
                }
                s.EndConnect(ar); // SocketException here (refused, unreachable) means not connected
                return s;
            }
            catch
            {
                s.Close();
                throw;
            }
        }

        /// <summary>Sends one frame and returns the full response (9-byte header + len bytes). Throws TimeoutException("timeout") on receive timeout.</summary>
        public byte[] Request(byte[] frame)
        {
            lock (io)
            {
                try
                {
                    sock.Send(frame);
                    var head = new byte[McFrame.HeaderSize];
                    ReadExact(sock, head, 0, head.Length);
                    var resp = new byte[McFrame.ResponseTotalSize(head)];
                    Buffer.BlockCopy(head, 0, resp, 0, head.Length);
                    ReadExact(sock, resp, head.Length, resp.Length - head.Length);
                    return resp;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    Close();
                    throw new TimeoutException("timeout");
                }
                catch
                {
                    Close();
                    throw;
                }
            }
        }

        /// <summary>Receives exactly count bytes (TCP may split a reply). Throws IOException when the peer closes.</summary>
        internal static void ReadExact(Socket sock, byte[] buf, int off, int count)
        {
            while (count > 0)
            {
                int n = sock.Receive(buf, off, count, SocketFlags.None);
                if (n <= 0) throw new IOException("connection closed by peer");
                off += n;
                count -= n;
            }
        }

        /// <summary>Safe from any thread; unblocks a pending receive.</summary>
        public void Close()
        {
            try { sock.Close(); } catch (Exception) { }
        }

        public void Dispose() => Close();
    }
}
