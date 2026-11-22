namespace SignalDx.Geo;

/// <summary>Approximate US state from Maidenhead grid (bounding boxes).</summary>
public static class UsState
{
    // minLat, maxLat, minLon, maxLon, abbreviation — rough boxes, smallest area wins on overlap.
    static readonly (double MinLat, double MaxLat, double MinLon, double MaxLon, string Abbr, string Name)[] Boxes =
    [
        (24.5, 31.1, -87.7, -80.0, "FL", "Florida"),
        (30.2, 35.0, -88.5, -84.8, "AL", "Alabama"),
        (30.1, 35.0, -91.7, -88.0, "MS", "Mississippi"),
        (28.9, 33.1, -94.1, -89.6, "LA", "Louisiana"),
        (33.0, 36.7, -94.7, -89.6, "AR", "Arkansas"),
        (33.7, 36.7, -90.4, -81.6, "TN", "Tennessee"),
        (30.3, 35.1, -85.7, -80.7, "GA", "Georgia"),
        (32.0, 35.3, -83.4, -78.4, "SC", "South Carolina"),
        (33.7, 36.7, -84.4, -75.4, "NC", "North Carolina"),
        (36.5, 39.5, -83.7, -75.1, "VA", "Virginia"),
        (37.8, 40.7, -82.7, -74.9, "MD", "Maryland"),
        (38.7, 39.8, -77.2, -76.8, "DC", "District of Columbia"),
        (37.8, 40.7, -82.7, -77.6, "WV", "West Virginia"),
        (36.4, 39.2, -89.6, -81.9, "KY", "Kentucky"),
        (38.3, 42.0, -84.9, -80.4, "OH", "Ohio"),
        (39.6, 42.3, -80.6, -74.6, "PA", "Pennsylvania"),
        (38.8, 41.4, -75.6, -73.8, "NJ", "New Jersey"),
        (40.4, 45.1, -79.9, -71.7, "NY", "New York"),
        (40.9, 42.1, -73.8, -71.7, "CT", "Connecticut"),
        (41.1, 42.1, -71.9, -71.1, "RI", "Rhode Island"),
        (41.2, 42.9, -73.6, -69.8, "MA", "Massachusetts"),
        (42.6, 45.4, -73.5, -70.9, "VT", "Vermont"),
        (42.6, 45.4, -72.6, -70.5, "NH", "New Hampshire"),
        (43.0, 47.5, -71.2, -66.8, "ME", "Maine"),
        (41.6, 48.3, -90.5, -82.3, "MI", "Michigan"),
        (37.7, 41.8, -88.2, -84.7, "IN", "Indiana"),
        (36.9, 42.6, -91.6, -87.0, "IL", "Illinois"),
        (42.4, 47.2, -92.9, -86.7, "WI", "Wisconsin"),
        (43.4, 49.4, -97.3, -89.4, "MN", "Minnesota"),
        (40.3, 43.6, -96.7, -90.1, "IA", "Iowa"),
        (35.9, 40.7, -95.9, -89.0, "MO", "Missouri"),
        (33.5, 37.1, -103.1, -94.3, "OK", "Oklahoma"),
        (25.8, 36.6, -106.7, -93.4, "TX", "Texas"),
        (36.9, 41.1, -102.1, -95.2, "KS", "Kansas"),
        (40.0, 43.1, -104.1, -95.2, "NE", "Nebraska"),
        (42.4, 46.0, -104.2, -96.4, "SD", "South Dakota"),
        (45.8, 49.1, -104.2, -96.5, "ND", "North Dakota"),
        (44.9, 49.1, -116.1, -104.0, "MT", "Montana"),
        (40.9, 45.1, -111.1, -104.0, "WY", "Wyoming"),
        (36.9, 41.1, -109.2, -102.0, "CO", "Colorado"),
        (31.3, 37.1, -109.2, -103.0, "NM", "New Mexico"),
        (31.3, 37.1, -115.0, -109.0, "AZ", "Arizona"),
        (36.9, 42.1, -114.1, -109.0, "UT", "Utah"),
        (41.9, 49.1, -117.3, -111.0, "ID", "Idaho"),
        (35.0, 42.1, -120.1, -114.0, "NV", "Nevada"),
        (41.9, 46.3, -124.7, -116.4, "OR", "Oregon"),
        (45.5, 49.1, -124.9, -116.9, "WA", "Washington"),
        (32.5, 42.1, -124.5, -114.1, "CA", "California"),
        (18.8, 22.4, -160.4, -154.7, "HI", "Hawaii"),
        (51.0, 71.5, -179.2, -129.9, "AK", "Alaska"),
        (17.8, 18.6, -67.4, -65.2, "PR", "Puerto Rico"),
        (17.6, 18.5, -65.2, -64.5, "VI", "US Virgin Islands"),
        (13.2, 13.8, -145.0, -144.5, "GU", "Guam"),
    ];

    public static string? FromGrid(string? grid)
    {
        if (!Maidenhead.TryGetLatLon(grid, out double lat, out double lon))
            return null;

        string? best = null;
        double bestArea = double.MaxValue;
        foreach (var b in Boxes)
        {
            if (lat < b.MinLat || lat > b.MaxLat || lon < b.MinLon || lon > b.MaxLon)
                continue;
            double area = (b.MaxLat - b.MinLat) * (b.MaxLon - b.MinLon);
            if (area < bestArea)
            {
                bestArea = area;
                best = b.Abbr;
            }
        }
        return best;
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
        return FromGrid(grid);
    }
}
