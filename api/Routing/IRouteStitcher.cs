namespace RideForgeApi.Routing;

/// <summary>
/// The single swappable seam between RideForge and the external directions/map-matching
/// provider. The endpoint (now) and the curviness algorithm (later) depend on this
/// interface, never a concrete provider. All provider vocabulary stays inside implementations.
/// </summary>
public interface IRouteStitcher
{
    /// <summary>
    /// Stitch an ordered waypoint list into a road-following route.
    /// </summary>
    /// <exception cref="RouteStitchException">
    /// Thrown for provider-side failures (no route, provider error, timeout) so the
    /// endpoint can map them to distinct HTTP status codes without knowing provider internals.
    /// </exception>
    Task<StitchedRoute> StitchAsync(RouteRequest request, CancellationToken ct);
}

/// <summary>Classifies a stitching failure so the endpoint can pick the right HTTP status.</summary>
public enum StitchFailure
{
    /// <summary>The waypoints could not be connected into a route (→ 422).</summary>
    NoRoute,

    /// <summary>The provider errored or returned an unexpected response (→ 502).</summary>
    ProviderError,

    /// <summary>The provider call timed out or was cancelled (→ 504).</summary>
    Timeout,
}

/// <summary>A provider-side failure, tagged with a <see cref="StitchFailure"/> kind.</summary>
public sealed class RouteStitchException : Exception
{
    public StitchFailure Kind { get; }

    public RouteStitchException(StitchFailure kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}
