namespace SignalDx.Wspr;

/// <summary>
/// WSPR Type-1 channel-symbol encoder (callsign + 4-char grid + power dBm → 162 symbols).
/// Algorithm adapted from JTEncode / WsprSharp (Scott Harden AJ4VD, MIT), originally Joe Taylor K1JT.
/// </summary>
public static class WsprEncoder
{
    public const int SymbolCount = 162;
    public const double SymbolDurationSec = 8192.0 / 12000.0; // ≈ 0.6826667
    public const double ToneSpacingHz = 12000.0 / 8192.0;     // ≈ 1.46484375
    public const double SignalDurationSec = SymbolCount * SymbolDurationSec; // 110.592
    public const double SlotSeconds = 120;
    public const double LeadInSec = 1.0;

    static readonly byte[] Sync =
    [
        1,1,0,0,0,0,0,0,1,0,0,0,1,1,1,0,0,0,1,0,0,
        1,0,1,1,1,1,0,0,0,0,0,0,0,1,0,0,1,0,1,0,0,
        0,0,0,0,1,0,1,1,0,0,1,1,0,1,0,0,0,1,1,0,1,
        0,0,0,0,1,1,0,1,0,1,0,1,0,1,0,0,1,0,0,1,0,
        1,1,0,0,0,1,1,0,1,0,1,0,0,0,1,0,0,0,0,0,1,
        0,0,1,0,0,1,1,1,0,1,1,0,0,1,1,0,1,0,0,0,1,
        1,1,0,0,0,0,0,1,0,1,0,0,1,1,0,0,0,0,0,0,0,
        1,1,0,1,0,1,1,0,0,0,1,1,0,0,0
    ];

    public static readonly int[] ValidPowerDbm =
        [0, 3, 7, 10, 13, 17, 20, 23, 27, 30, 33, 37, 40, 43, 47, 50, 53, 57, 60];

    /// <summary>Encode standard Type-1 WSPR message into 162 tone indices (0–3).</summary>
    public static int[] EncodeSymbols(string callsign, string grid4, int powerDbm)
    {
        byte[] packed = PackMessage(callsign, grid4, powerDbm);
        byte[] convolved = Convolve(packed);
        byte[] interleaved = Interleave(convolved);
        var symbols = new int[SymbolCount];
        for (int i = 0; i < SymbolCount; i++)
            symbols[i] = interleaved[i] * 2 + Sync[i];
        return symbols;
    }

    /// <summary>Format "CALL GRID POWER" used in Band Activity / FreeText.</summary>
    public static string FormatMessage(string callsign, string grid4, int powerDbm)
    {
        string call = SanitizeCallsign(callsign).Trim();
        string grid = SanitizeGrid(grid4);
        int p = SnapPower(powerDbm);
        return $"{call} {grid} {p}";
    }

    public static int SnapPower(int powerDbm)
    {
        int best = ValidPowerDbm[0];
        foreach (int p in ValidPowerDbm)
        {
            if (powerDbm >= p) best = p;
        }
        return best;
    }

    /// <summary>Convert RF watts to nearest valid WSPR dBm.</summary>
    public static int WattsToDbm(double watts)
    {
        if (watts <= 0) return 0;
        double dbm = 10.0 * Math.Log10(watts * 1000.0);
        return SnapPower((int)Math.Round(dbm));
    }

    /// <summary>Approximate RF watts from WSPR dBm (for CAT RFPOWER sync).</summary>
    public static double DbmToWatts(int powerDbm)
    {
        int p = SnapPower(powerDbm);
        return Math.Pow(10.0, (p - 30) / 10.0);
    }

    /// <summary>UI label e.g. "37 dBm (5 W)".</summary>
    public static string FormatPowerLabel(int powerDbm)
    {
        int p = SnapPower(powerDbm);
        double w = DbmToWatts(p);
        string watts = w >= 1
            ? $"{w:0.##} W"
            : w >= 0.001
                ? $"{w * 1000:0.##} mW"
                : $"{w * 1_000_000:0.##} µW";
        return $"{p} dBm ({watts})";
    }

    public static byte[] PackMessage(string callsign, string grid4, int powerDbm)
    {
        string call = SanitizeCallsign(callsign);
        string loc = SanitizeGrid(grid4);
        byte power = (byte)SnapPower(powerDbm);

        char[] c = call.ToCharArray();
        uint intA = WsprCode(c[0]);
        intA = intA * 36 + WsprCode(c[1]);
        intA = intA * 10 + WsprCode(c[2]);
        intA = (uint)(intA * 27 + (WsprCode(c[3]) - 10));
        intA = (uint)(intA * 27 + (WsprCode(c[4]) - 10));
        intA = (uint)(intA * 27 + (WsprCode(c[5]) - 10));

        char[] g = loc.ToCharArray();
        uint intB = (uint)(
            180 * (179 - 10 * (g[0] - 'A') - (g[2] - '0')) +
            10 * (g[1] - 'A') +
            (g[3] - '0'));
        intB = (intB * 128) + power + 64;

        var bytes = new byte[7];
        bytes[3] = (byte)((intA & 0xF) << 4);
        intA >>= 4;
        bytes[2] = (byte)(intA & 0xFF);
        intA >>= 8;
        bytes[1] = (byte)(intA & 0xFF);
        intA >>= 8;
        bytes[0] = (byte)(intA & 0xFF);
        bytes[6] = (byte)((intB & 0x3) << 6);
        intB >>= 2;
        bytes[5] = (byte)(intB & 0xFF);
        intB >>= 8;
        bytes[4] = (byte)(intB & 0xFF);
        intB >>= 8;
        bytes[3] = (byte)(bytes[3] | (intB & 0xF));
        return bytes;
    }

    public static string SanitizeCallsign(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            throw new ArgumentException("Callsign required for WSPR.");

        string call = callsign.Trim().ToUpperInvariant();
        int slash = call.IndexOf('/');
        if (slash > 0)
            call = call[..slash]; // Type-1 does not support compound calls

        if (call.Length > 6)
            call = call[..6];

        int digits = call.Count(char.IsDigit);
        if (digits < 1)
            throw new ArgumentException("WSPR callsign must contain a digit.");
        if (digits > 1)
            throw new ArgumentException("WSPR Type-1 callsign may contain only one digit.");
        if (!char.IsLetter(call[0]))
            throw new ArgumentException("WSPR callsign must start with a letter.");
        if (!char.IsLetter(call[^1]))
            throw new ArgumentException("WSPR callsign must end with a letter.");

        // Pad so the digit is in position 3 (0-based index 2).
        if (char.IsDigit(call[1]))
            call = " " + call;

        if (call.Length < 3 || !char.IsDigit(call[2]))
            throw new ArgumentException("WSPR callsign digit must be in the 2nd or 3rd character.");

        while (call.Length < 6)
            call += " ";

        return call;
    }

    public static string SanitizeGrid(string grid)
    {
        if (string.IsNullOrWhiteSpace(grid) || grid.Trim().Length < 4)
            throw new ArgumentException("WSPR requires a 4-character Maidenhead grid.");
        string g = grid.Trim().ToUpperInvariant()[..4];
        if (g[0] is < 'A' or > 'R' || g[1] is < 'A' or > 'R')
            throw new ArgumentException("Grid must start with two letters A–R.");
        if (g[2] is < '0' or > '9' || g[3] is < '0' or > '9')
            throw new ArgumentException("Grid must end with two digits.");
        return g;
    }

    static byte[] Convolve(byte[] data)
    {
        var padded = new byte[11];
        Array.Copy(data, 0, padded, 0, Math.Min(data.Length, padded.Length));
        var output = new byte[SymbolCount];
        uint reg0 = 0, reg1 = 0;
        int bitIndex = 0;

        for (int i = 0; i < 11 && bitIndex < SymbolCount; i++)
        {
            for (int j = 0; j < 8 && bitIndex < SymbolCount; j++)
            {
                byte inputBit = (byte)((((padded[i] << j) & 0x80) == 0x80) ? 1 : 0);
                reg0 = (reg0 << 1) | inputBit;
                reg1 = (reg1 << 1) | inputBit;

                uint regTemp = reg0 & 0xf2d05351;
                byte parity = 0;
                for (int k = 0; k < 32; k++)
                {
                    parity ^= (byte)(regTemp & 1);
                    regTemp >>= 1;
                }
                output[bitIndex++] = parity;

                regTemp = reg1 & 0xe4613c47;
                parity = 0;
                for (int k = 0; k < 32; k++)
                {
                    parity ^= (byte)(regTemp & 1);
                    regTemp >>= 1;
                }
                if (bitIndex < SymbolCount)
                    output[bitIndex++] = parity;
            }
        }

        return output;
    }

    static byte[] Interleave(byte[] data)
    {
        var d = new byte[SymbolCount];
        int i = 0;
        for (int j = 0; j < 255 && i < SymbolCount; j++)
        {
            int j2 = j;
            int rev = 0;
            for (int k = 0; k < 8; k++)
            {
                if ((j2 & 1) > 0)
                    rev |= 1 << (7 - k);
                j2 >>= 1;
            }

            if (rev < SymbolCount)
            {
                d[rev] = data[i];
                i++;
            }
        }
        return d;
    }

    static byte WsprCode(char c)
    {
        if (char.IsDigit(c)) return (byte)(c - '0');
        if (c == ' ') return 36;
        if (c is >= 'A' and <= 'Z') return (byte)(c - 'A' + 10);
        throw new InvalidOperationException($"Invalid WSPR character '{c}'.");
    }
}
