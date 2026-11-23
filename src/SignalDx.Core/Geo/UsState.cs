using System.Text.Json;

namespace SignalDx.Geo;

/// <summary>
/// US state from a Maidenhead grid center, using real state boundary polygons
/// (embedded us-states.geojson). Grids that fall on water use the nearest state
/// within a small tolerance so Great Lakes / coastal squares still resolve.
/// </summary>
public static class UsState
{
    static readonly Dictionary<string, string> Abbr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "AL", ["Alaska"] = "AK", ["Arizona"] = "AZ", ["Arkansas"] = "AR",
        ["California"] = "CA", ["Colorado"] = "CO", ["Connecticut"] = "CT", ["Delaware"] = "DE",
        ["District of Columbia"] = "DC", ["Florida"] = "FL", ["Georgia"] = "GA", ["Hawaii"] = "HI",
        ["Idaho"] = "ID", ["Illinois"] = "IL", ["Indiana"] = "IN", ["Iowa"] = "IA",
        ["Kansas"] = "KS", ["Kentucky"] = "KY", ["Louisiana"] = "LA", ["Maine"] = "ME",
        ["Maryland"] = "MD", ["Massachusetts"] = "MA", ["Michigan"] = "MI", ["Minnesota"] = "MN",
        ["Mississippi"] = "MS", ["Missouri"] = "MO", ["Montana"] = "MT", ["Nebraska"] = "NE",
        ["Nevada"] = "NV", ["New Hampshire"] = "NH", ["New Jersey"] = "NJ", ["New Mexico"] = "NM",
        ["New York"] = "NY", ["North Carolina"] = "NC", ["North Dakota"] = "ND", ["Ohio"] = "OH",
        ["Oklahoma"] = "OK", ["Oregon"] = "OR", ["Pennsylvania"] = "PA", ["Rhode Island"] = "RI",
        ["South Carolina"] = "SC", ["South Dakota"] = "SD", ["Tennessee"] = "TN", ["Texas"] = "TX",
        ["Utah"] = "UT", ["Vermont"] = "VT", ["Virginia"] = "VA", ["Washington"] = "WA",
        ["West Virginia"] = "WV", ["Wisconsin"] = "WI", ["Wyoming"] = "WY", ["Puerto Rico"] = "PR",
    };

    const double NearestToleranceDeg = 0.3;

    readonly record struct Pt(double Lon, double Lat);

    sealed record Part(double MinLat, double MaxLat, double MinLon, double MaxLon, Pt[][] Rings);

    sealed record Shape(string Abbr, Part[] Parts);

    static readonly Lazy<Shape[]> Shapes = new(LoadShapes);

    static Shape[] LoadShapes()
    {
        using var stream = typeof(UsState).Assembly.GetManifestResourceStream("SignalDx.Data.us-states.geojson");
        if (stream is null) return [];

        using var doc = JsonDocument.Parse(stream);
        var shapes = new List<Shape>();
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            string name = feature.GetProperty("properties").GetProperty("name").GetString() ?? "";
            if (!Abbr.TryGetValue(name, out var abbr)) continue;

            var geometry = feature.GetProperty("geometry");
            string type = geometry.GetProperty("type").GetString() ?? "";
            var coords = geometry.GetProperty("coordinates");
            var parts = new List<Part>();
            if (type == "Polygon")
                parts.Add(ReadPolygon(coords));
            else if (type == "MultiPolygon")
                foreach (var polygon in coords.EnumerateArray())
                    parts.Add(ReadPolygon(polygon));

            shapes.Add(new Shape(abbr, parts.ToArray()));
        }
        return shapes.ToArray();
    }

    static Part ReadPolygon(JsonElement polygon)
    {
        var rings = polygon.EnumerateArray()
            .Select(ring => ring.EnumerateArray()
                .Select(p => new Pt(p[0].GetDouble(), p[1].GetDouble()))
                .ToArray())
            .ToArray();
        var outer = rings[0];
        return new Part(
            outer.Min(p => p.Lat), outer.Max(p => p.Lat),
            outer.Min(p => p.Lon), outer.Max(p => p.Lon),
            rings);
    }

    public static string? FromGrid(string? grid)
    {
        if (!Maidenhead.TryGetLatLon(grid, out double lat, out double lon))
            return null;
        return FromLatLon(lat, lon);
    }

    public static string? FromLatLon(double lat, double lon)
    {
        var shapes = Shapes.Value;

        string? inside = null;
        double insideArea = double.MaxValue;
        foreach (var shape in shapes)
        foreach (var part in shape.Parts)
        {
            if (!Contains(part, lat, lon)) continue;
            double area = (part.MaxLat - part.MinLat) * (part.MaxLon - part.MinLon);
            if (area < insideArea)
            {
                insideArea = area;
                inside = shape.Abbr;
            }
        }
        if (inside is not null) return inside;

        string? nearest = null;
        double best = NearestToleranceDeg;
        foreach (var shape in shapes)
        foreach (var part in shape.Parts)
        {
            double d = DistanceDeg(part, lat, lon);
            if (d <= best)
            {
                best = d;
                nearest = shape.Abbr;
            }
        }
        return nearest;
    }

    public static string? Resolve(string? country, string? grid, string? callsign = null)
    {
        // Territories / states already labeled as Country from prefix.
        if (country is "Alaska") return "AK";
        if (country is "Hawaii") return "HI";
        if (country is "Puerto Rico") return "PR";
        if (country is "USVI") return "VI";
        if (country is "Guam" or "Mariana") return "GU";

        if (country is not ("USA" or "United States"))
        {
            // Still try grid for US mainland if prefix missed (rare).
            if (country is not null) return null;
        }

        // Only real 2-letter states from grid / territory — never call-district
        // nicknames like "SCEN" / "NE" (those are not states).
        return FromGrid(CallGridDatabase.PreferredGrid(callsign, grid));
    }

    static bool Contains(Part part, double lat, double lon)
    {
        if (lat < part.MinLat || lat > part.MaxLat || lon < part.MinLon || lon > part.MaxLon)
            return false;

        bool inside = false;
        foreach (var ring in part.Rings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Lat > lat) != (b.Lat > lat) &&
                    lon < (b.Lon - a.Lon) * (lat - a.Lat) / (b.Lat - a.Lat) + a.Lon)
                    inside = !inside;
            }
        }
        return inside;
    }

    static double DistanceDeg(Part part, double lat, double lon)
    {
        double cosLat = Math.Cos(lat * Math.PI / 180.0);
        double best = double.MaxValue;
        foreach (var ring in part.Rings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                double d = SegmentDistance(lon, lat, ring[j], ring[i], cosLat);
                if (d < best) best = d;
            }
        }
        return best;
    }

    static double SegmentDistance(double px, double py, Pt a, Pt b, double cosLat)
    {
        double x = px * cosLat, y = py;
        double ax = a.Lon * cosLat, ay = a.Lat;
        double dx = b.Lon * cosLat - ax, dy = b.Lat - ay;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
        double ex = ax + t * dx - x, ey = ay + t * dy - y;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
