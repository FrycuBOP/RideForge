namespace RideForgeApi.Routing;

/// <summary>
/// A single geographic point. Provider-agnostic: no encoded polylines, no GeoJSON,
/// no provider vocabulary crosses this boundary.
/// </summary>
public record Coord(double Lat, double Lng);

/// <summary>
/// The stable input the stitching adapter accepts: an ordered list of waypoints the
/// route must follow. The (future) curviness algorithm produces this; the endpoint
/// hands it in directly for now.
/// </summary>
public record RouteRequest(IReadOnlyList<Coord> Waypoints);

/// <summary>
/// The stable output the adapter returns: a road-following geometry plus the summary
/// metrics US-01 checks against (distance / duration).
/// </summary>
public record StitchedRoute(
    IReadOnlyList<Coord> Geometry,
    double DistanceMeters,
    double DurationSeconds);

/// <summary>
/// Endpoint request body. Serializes to camelCase: <c>waypoints</c>. Kept distinct from
/// <see cref="RouteRequest"/> so the wire contract and the internal contract can evolve
/// independently.
/// </summary>
public record StitchRequestDto(IReadOnlyList<Coord>? Waypoints);

/// <summary>
/// Endpoint request body for route generation. Serializes to camelCase: <c>start</c>,
/// <c>distanceKm</c>. The generator turns this into a waypoint loop the stitcher then routes;
/// the success body is the same <see cref="StitchResponseDto"/> the stitch endpoint returns.
/// </summary>
public record GenerateRequestDto(Coord? Start, double? DistanceKm);

/// <summary>
/// Endpoint success body. Serializes to camelCase: <c>geometry</c>, <c>distanceMeters</c>,
/// <c>durationSeconds</c> — the shape the mobile client (react-native-maps Polyline) consumes.
/// </summary>
public record StitchResponseDto(
    IReadOnlyList<Coord> Geometry,
    double DistanceMeters,
    double DurationSeconds);
