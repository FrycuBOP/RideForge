namespace RideForgeApi.Routing;

/// <summary>Small geodesic helpers shared by stitcher implementations.</summary>
public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000.0;

    /// <summary>Great-circle distance between two points, in metres (haversine).</summary>
    public static double HaversineMeters(Coord a, Coord b)
    {
        var lat1 = DegToRad(a.Lat);
        var lat2 = DegToRad(b.Lat);
        var dLat = DegToRad(b.Lat - a.Lat);
        var dLng = DegToRad(b.Lng - a.Lng);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    /// <summary>Summed great-circle length over a polyline of waypoints, in metres.</summary>
    public static double PathLengthMeters(IReadOnlyList<Coord> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += HaversineMeters(points[i - 1], points[i]);
        }
        return total;
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
}
