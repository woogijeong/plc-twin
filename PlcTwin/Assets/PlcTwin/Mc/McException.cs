using System;

namespace PlcTwin.Mc
{
    /// <summary>MC protocol failure. EndCode is the PLC end code, or -1 when the reply was malformed.</summary>
    public class McException : Exception
    {
        public int EndCode { get; }

        public McException(int endCode) : base("end code " + endCode.ToString("X4")) { EndCode = endCode; }

        public McException(string message) : base(message) { EndCode = -1; }
    }
}
