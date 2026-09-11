using RideForgeApi.Routing;

namespace RideForgeApi.SavedRoutes;

/// <summary>
/// Request body for <c>POST /saved-routes</c>. Serializes to camelCase: <c>clientRouteId</c>,
/// <c>start</c>, <c>startLabel</c>, <c>requestedDistanceKm</c>, <c>geometry</c>,
/// <c>distanceMeters</c>, <c>durationSeconds</c>.
/// <para>
/// There is deliberately no owner field. Whoever the body claims to be, binding drops it — the owner
/// is the verified token's <c>sub</c>, and nothing else.
/// </para>
/// <para>
/// Kept apart from the <c>SavedRoute</c> entity so the wire and storage shapes can evolve
/// independently, as <see cref="StitchRequestDto"/> is kept apart from <see cref="RouteRequest"/>.
/// </para>
/// </summary>
public record SaveRouteRequestDto(
    Guid? ClientRouteId,
    Coord? Start,
    string? StartLabel,
    double? RequestedDistanceKm,
    IReadOnlyList<Coord>? Geometry,
    double? DistanceMeters,
    double? DurationSeconds);

/// <summary>
/// Response body for <c>POST /saved-routes</c>, both for a fresh save (201) and for a repeat that
/// found the rider's existing row (200). Serializes to camelCase: <c>id</c>, <c>name</c>,
/// <c>createdAt</c>.
/// </summary>
public record SavedRouteResponseDto(Guid Id, string Name, DateTimeOffset CreatedAt);
