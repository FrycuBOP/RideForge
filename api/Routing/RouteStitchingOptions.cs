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
}
