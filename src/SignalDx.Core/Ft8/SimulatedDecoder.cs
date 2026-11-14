using SignalDx.Models;

namespace SignalDx.Ft8;

public sealed class SimulatedDecoder
{
    static readonly string[] Calls =
    [
        "K1ABC", "W2XYZ", "N3QRP", "VE3DX", "G0ABC", "DL1XYZ", "JA1YOTA",
        "ZL2ABC", "PY2DX", "EA8BFK", "F4ABC", "IK2XYZ", "SM0DX", "OH2BH"
    ];

    static readonly string[] Grids =
    [
        "FN42", "EM12", "DM04", "CN87", "JO22", "JN18", "PM95", "RE78", "GG66", "IL18"
    ];

    readonly Random _rng = new();
    readonly HashSet<string> _heard = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DecodeHit> GenerateSlot(DateTime utc, StationSettings station)
    {
        int count = _rng.Next(4, 12);
        var hits = new List<DecodeHit>(count);

        for (int i = 0; i < count; i++)
        {
            string call = Calls[_rng.Next(Calls.Length)];
            string grid = Grids[_rng.Next(Grids.Length)];
            int hz = 300 + _rng.Next(0, 240) * (int)Ft8Timing.ToneSpacingHz;
            double snr = Math.Round(_rng.NextDouble() * 28 - 20, 1);
            double dt = Math.Round(_rng.NextDouble() * 1.2 - 0.4, 2);
            bool cq = _rng.NextDouble() < 0.45;

            string message = cq
                ? $"CQ {call} {grid}"
                : $"{station.MyCall} {call} {(snr >= 0 ? "+" : "")}{(int)snr}";

            bool isNew = _heard.Add(call);
            var parsed = Ft8MessageBuilder.Parse(message);

            hits.Add(new DecodeHit
            {
                Utc = utc,
                Mode = "FT8",
                Hz = hz,
                SnrDb = snr,
                Dt = dt,
                Message = message,
                DxCall = parsed.DxCall,
                DxGrid = parsed.DxGrid,
                IsNew = isNew,
                IsCq = cq
            });
        }

        return hits.OrderBy(h => h.Hz).ToList();
    }
}
