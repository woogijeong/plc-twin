using System;

namespace PlcTwin.Mc
{
    /// <summary>A parsed device such as "Y2F". X/Y numbers are HEX, M/D numbers are DECIMAL.</summary>
    public readonly struct DeviceAddress
    {
        public char Type { get; }
        public byte Code { get; }
        public int Number { get; }

        DeviceAddress(char type, byte code, int number) { Type = type; Code = code; Number = number; }

        public bool IsBit => Type != 'D';

        public static DeviceAddress Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new FormatException("Invalid device address: '" + s + "'");
            char t = char.ToUpperInvariant(s[0]);
            byte code;
            bool hex;
            switch (t)
            {
                case 'X': code = 0x9C; hex = true; break;
                case 'Y': code = 0x9D; hex = true; break;
                case 'M': code = 0x90; hex = false; break;
                case 'D': code = 0xA8; hex = false; break;
                default: throw new FormatException("Invalid device address (type): '" + s + "'");
            }
            if (s.Length < 2 || s.Length > 9) throw new FormatException("Invalid device address (number): '" + s + "'");
            int n = 0;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                int d = c >= '0' && c <= '9' ? c - '0'
                      : hex && c >= 'A' && c <= 'F' ? c - 'A' + 10
                      : hex && c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
                if (d < 0) throw new FormatException("Invalid device address (number): '" + s + "'");
                n = n * (hex ? 16 : 10) + d;
            }
            if (n > 0xFFFFFF) throw new FormatException("Device number out of range: '" + s + "'");
            return new DeviceAddress(t, code, n);
        }

        public override string ToString() => Type + (Type == 'X' || Type == 'Y' ? Number.ToString("X") : Number.ToString());
    }
}
