namespace RideForgeApi.Routing;

/// <summary>
/// RideForge's own (first-cut) route generator: turns a start point + requested distance into an
/// ordered, closed loop of sparse waypoints for the stitcher to route along real roads. This is
/// curviness-agnostic for S-01 — it only sizes and shapes a loop; the curviness algorithm (S-02)
/// comes later and will replace/extend the shaping while keeping this waypoint output contract.
/// </summary>
public static class RouteGenerator
{
    private const double EarthRadiusMeters = 6_371_000.0;

    /// <summary>Waypoints placed around the loop — a regular polygon whose first vertex is the start.</summary>
    private const int WaypointCount = 8;

    /// <summary>
    /// Compensation for real roads being longer than the straight-line loop. The generator sizes the
    /// straight-line perimeter to <c>distanceKm / DetourFactor</c>, so once the stitcher follows real
    /// roads the on-road length lands near the requested distance. Tuned to <c>1.6</c> from
    /// OpenRouteService samples around Kraków — the straight→road inflation measured ~1.45–1.71 across
    /// 20–60 km (smaller loops detour more), so a single constant can't be exact: treat US-01 ±20% as
    /// the goal, not a guarantee. A size-dependent factor is a future refinement; re-tune per region
    /// if real-road length drifts outside ±20%.
    /// </summary>
    public const double DetourFactor = 1.6;

    /// <summary>
    /// Generate a closed loop of waypoints that starts and ends exactly at <paramref name="start"/>,
    /// sized so its straight-line perimeter is <c>distanceKm / DetourFactor</c>. Ordered; the first and
    /// last elements are the start (departs from and returns to the start, per US-01).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="distanceKm"/> is not positive.</exception>
    public static IReadOnlyList<Coord> GenerateLoop(Coord start, double distanceKm)
    {
        if (double.IsNaN(distanceKm) || distanceKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceKm), "Distance must be a positive number of kilometres.");
        }

        var targetPerimeterMeters = distanceKm * 1000.0 / DetourFactor;
        // Regular N-gon perimeter = N · 2r · sin(pi/N)  ⇒  r = perimeter / (2N · sin(pi/N)).
        var radiusMeters = targetPerimeterMeters / (2.0 * WaypointCount * Math.Sin(Math.PI / WaypointCount));

        // Circle centre sits one radius due north, so the start is the polygon's southern vertex.
        var centre = Offset(start, eastMeters: 0, northMeters: radiusMeters);

        var loop = new List<Coord>(WaypointCount + 1) { start };
        // Vertex 0 (south of the circle) is the start itself; place the remaining vertices around it.
        for (var i = 1; i < WaypointCount; i++)
        {
            var angle = -Math.PI / 2 + i * (2.0 * Math.PI / WaypointCount);
            loop.Add(Offset(centre, radiusMeters * Math.Cos(angle), radiusMeters * Math.Sin(angle)));
        }
        loop.Add(start); // close the loop exactly at the start
        return loop;
    }

    /// <summary>Offset a point by east/north metres (equirectangular approximation), returning lat/lng.</summary>
    private static Coord Offset(Coord origin, double eastMeters, double northMeters)
    {
        var dLat = northMeters / EarthRadiusMeters * (180.0 / Math.PI);
        var dLng = eastMeters / (EarthRadiusMeters * Math.Cos(DegToRad(origin.Lat))) * (180.0 / Math.PI);
        return new Coord(origin.Lat + dLat, NormalizeLongitude(origin.Lng + dLng));
    }

    /// <summary>
    /// Wrap a longitude back into [-180, 180]. A loop centred near the antimeridian otherwise emits
    /// values like -183.4, which are outside the legal domain and are rejected by the provider.
    /// (Latitude needs no equivalent: <see cref="RouteValidation.MaxGenerationLatitude"/> keeps the
    /// loop clear of the poles, where wrapping would also have to mirror the longitude.)
    /// </summary>
    private static double NormalizeLongitude(double lng)
    {
        var wrapped = (lng + 180.0) % 360.0;
        if (wrapped < 0) wrapped += 360.0;
        return wrapped - 180.0;
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
}
