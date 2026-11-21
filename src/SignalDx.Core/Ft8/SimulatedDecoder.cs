using SignalDx.Geo;
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
            // Prefer explicit grid from CQ/message; fall back to the simulated station grid.
            // DxCall is the calling/transmitting station (not the called party).
            string? dxCall = parsed.DxCall ?? call;
            string? dxGrid = parsed.DxGrid ?? (cq ? grid : null);
            double? miles = Maidenhead.DistanceMiles(station.MyGrid, dxGrid);
            string? country = CallsignCountry.FromCall(dxCall);
            string? state = UsState.Resolve(country, dxGrid, dxCall);

            hits.Add(new DecodeHit
            {
                Utc = utc,
                Mode = string.IsNullOrWhiteSpace(station.Mode) ? "FT8" : station.Mode,
                Hz = hz,
                SnrDb = snr,
                Dt = dt,
                Message = message,
                DxCall = dxCall,
                DxGrid = dxGrid,
                Country = country,
                State = state,
                IsNew = isNew,
                IsCq = cq,
                IsToMe = !string.IsNullOrWhiteSpace(station.MyCall) &&
                         message.StartsWith(station.MyCall.Trim() + " ", StringComparison.OrdinalIgnoreCase),
                DistanceMiles = miles is double m ? Math.Round(m) : null
            });
        }

        return hits.OrderBy(h => h.Hz).ToList();
    }
}
