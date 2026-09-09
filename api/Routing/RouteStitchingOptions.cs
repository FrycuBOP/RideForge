namespace RideForgeApi.Routing;

/// <summary>
/// Config for the route-stitching adapter. Bound from the <c>RouteStitching</c> section.
/// On Railway the secret arrives as env var <c>RouteStitching__ApiKey</c> (ASP.NET Core's
/// <c>__</c> → section convention), so no key is ever committed.
/// </summary>
public sealed class RouteStitchingOptions
{
    public const string SectionName = "RouteStitching";

    /// <summary>Which provider to resolve: <c>fake</c> (default, no network) or <c>openrouteservice</c>.</summary>
    public string Provider { get; set; } = "fake";

    /// <summary>External provider API key. Null/empty for the fake provider.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Provider base URL (no trailing path).</summary>
    public string BaseUrl { get; set; } = "https://api.openrouteservice.org";

    /// <summary>Routing profile, e.g. <c>driving-car</c>.</summary>
    public string Profile { get; set; } = "driving-car";

    /// <summary>Per-call ceiling for the outbound provider request; kept well under the 30s NFR-01 budget.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How far (metres) the provider may search from each waypoint for a routable road, sent as
    /// the per-waypoint <c>radiuses</c> array.
    /// <para>
    /// ORS defaults to 350 m, which rejects any start inside a large pedestrian zone: Kraków's Old
    /// Town is ~400–500 m of car-free streets, so a rider standing on Rynek Główny got "no route"
    /// for a perfectly valid address. Measured on the deployed API — of the nine waypoints a 40 km
    /// loop from that start produces, only waypoint 0 (the rider's own start) failed to snap; all
    /// eight generated vertices routed fine. The start point is the whole problem, so the radius
    /// only has to cover the walk from an address to the nearest drivable road.
    /// </para>
    /// <para>
    /// Set to <c>0</c> to omit <c>radiuses</c> and fall back to the provider's own default. That is
    /// the escape hatch if a provider instance caps its search radius below this value — it answers
    /// "parameter exceeds the maximum configured limit", which would otherwise fail every request.
    /// Tunable at runtime via <c>RouteStitching__SnapRadiusMeters</c>, no redeploy needed.
    /// </para>
    /// </summary>
    public int SnapRadiusMeters { get; set; } = 1000;
}
