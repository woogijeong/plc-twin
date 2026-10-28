using System;

namespace PlcTwin.Mc
{
    /// <summary>
    /// Word-only link to a PLC, addressed by D number. One request in flight. Any exception means the transport is
    /// dead: the caller disposes it and connects a new one. Timeouts throw TimeoutException("timeout").
    /// </summary>
    public interface IPlcTransport : IDisposable
    {
        short[] ReadWords(int d, int count);
        void WriteWords(int d, short[] values); // whole words only
    }
}
