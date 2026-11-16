namespace SignalDx.Geo;

public static class Maidenhead
{
    const double EarthRadiusMiles = 3958.7613;

    public static bool TryGetLatLon(string? grid, out double latDeg, out double lonDeg)
    {
        latDeg = 0;
        lonDeg = 0;
        if (string.IsNullOrWhiteSpace(grid)) return false;

        string g = grid.Trim().ToUpperInvariant();
        if (g.Length is not (4 or 6 or 8)) return false;
        if (!char.IsLetter(g[0]) || !char.IsLetter(g[1]) || !char.IsDigit(g[2]) || !char.IsDigit(g[3]))
            return false;

        // Field
        lonDeg = (g[0] - 'A') * 20.0 - 180.0;
        latDeg = (g[1] - 'A') * 10.0 - 90.0;

        // Square
        lonDeg += (g[2] - '0') * 2.0;
        latDeg += (g[3] - '0') * 1.0;

        if (g.Length >= 6)
        {
            if (!char.IsLetter(g[4]) || !char.IsLetter(g[5])) return false;
            lonDeg += (g[4] - 'A') * (2.0 / 24.0);
            latDeg += (g[5] - 'A') * (1.0 / 24.0);

            // Center of subsquare
            lonDeg += 2.0 / 24.0 / 2.0;
            latDeg += 1.0 / 24.0 / 2.0;
        }
        else
        {
            // Center of square
            lonDeg += 1.0;
            latDeg += 0.5;
        }

        return latDeg is >= -90 and <= 90 && lonDeg is >= -180 and <= 180;
    }

    public static double? DistanceMiles(string? fromGrid, string? toGrid)
    {
        if (!TryGetLatLon(fromGrid, out double lat1, out double lon1)) return null;
        if (!TryGetLatLon(toGrid, out double lat2, out double lon2)) return null;
        return GreatCircleMiles(lat1, lon1, lat2, lon2);
    }

    public static double GreatCircleMiles(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        double lat1 = DegToRad(lat1Deg);
        double lat2 = DegToRad(lat2Deg);
        double dLat = DegToRad(lat2Deg - lat1Deg);
        double dLon = DegToRad(lon2Deg - lon1Deg);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1) * Math.Cos(lat2) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMiles * c;
    }

    static double DegToRad(double deg) => deg * Math.PI / 180.0;
}
