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
        bool wspr = station.Mode.Equals("WSPR2", StringComparison.OrdinalIgnoreCase) ||
                    station.Mode.Equals("WSPR", StringComparison.OrdinalIgnoreCase);
        return wspr ? GenerateWsprSlot(utc, station) : GenerateFt8Slot(utc, station);
    }

    IReadOnlyList<DecodeHit> GenerateWsprSlot(DateTime utc, StationSettings station)
    {
        int count = _rng.Next(2, 7);
        int[] powers = [10, 13, 17, 20, 23, 27, 30, 33, 37];
        var hits = new List<DecodeHit>(count);

        for (int i = 0; i < count; i++)
        {
            string call = Calls[_rng.Next(Calls.Length)];
            string grid = Grids[_rng.Next(Grids.Length)][..4];
            int power = powers[_rng.Next(powers.Length)];
            // WSPR passband is narrow (~200 Hz around 1500).
            int hz = 1400 + _rng.Next(0, 200);
            double snr = Math.Round(_rng.NextDouble() * 20 - 28, 1);
            double dt = Math.Round(_rng.NextDouble() * 0.8 - 0.2, 2);
            string message = $"{call} {grid} {power}";
            bool isNew = _heard.Add(call);
            string? country = CallsignCountry.FromCall(call);
            string? state = UsState.Resolve(country, grid, call);
            double? miles = Maidenhead.DistanceMiles(station.MyGrid, grid);

            hits.Add(new DecodeHit
            {
                Utc = utc,
                Mode = "WSPR2",
                Hz = hz,
                SnrDb = snr,
                Dt = dt,
                Message = message,
                DxCall = call,
                DxGrid = grid,
                Country = country,
                State = state,
                IsNew = isNew,
                IsCq = true, // beacon-style
                IsToMe = false,
                DistanceMiles = miles is double m ? Math.Round(m) : null
            });
        }

        return hits.OrderBy(h => h.Hz).ToList();
    }

    IReadOnlyList<DecodeHit> GenerateFt8Slot(DateTime utc, StationSettings station)
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
