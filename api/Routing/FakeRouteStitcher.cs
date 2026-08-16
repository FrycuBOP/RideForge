namespace RideForgeApi.Routing;

/// <summary>
/// In-process stitcher that returns the input waypoints unchanged as the route geometry
/// (straight-line passthrough). Lets the whole vertical run with no network and no API key —
/// the default provider for local/CI. Distance is the summed great-circle length; duration a
/// naive constant-speed estimate.
/// </summary>
public sealed class FakeRouteStitcher : IRouteStitcher
{
    // ~50 km/h in m/s — a plausible average for a recreational ride, used only so the fake
    // returns a non-zero, distance-proportional duration. Not a real routing estimate.
    private const double AssumedSpeedMetersPerSecond = 50_000.0 / 3600.0;

    public Task<StitchedRoute> StitchAsync(RouteRequest request, CancellationToken ct)
    {
        if (request.Waypoints.Count < 2)
        {
            // Defensive: the endpoint validates first, but never emit a degenerate route.
            throw new RouteStitchException(
                StitchFailure.NoRoute, "At least two waypoints are required to stitch a route.");
        }

        var distanceMeters = GeoMath.PathLengthMeters(request.Waypoints);
        var durationSeconds = distanceMeters / AssumedSpeedMetersPerSecond;

        var route = new StitchedRoute(request.Waypoints, distanceMeters, durationSeconds);
        return Task.FromResult(route);
    }
}
