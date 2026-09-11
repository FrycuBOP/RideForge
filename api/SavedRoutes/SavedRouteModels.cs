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

/// <summary>
/// One row of <c>GET /saved-routes</c>. Serializes to camelCase: <c>id</c>, <c>name</c>,
/// <c>distanceMeters</c>, <c>durationSeconds</c>, <c>createdAt</c>.
/// <para>
/// Deliberately geometry-free. A full-size ride is roughly 600 KB of <c>jsonb</c>, so a list that
/// carried geometry would grow without bound in a rider's own hands. The projection that builds this
/// stays inside the <c>IQueryable</c> so the column is never read at all — see
/// <see cref="SavedRouteQueries.SummariesOwnedBy"/>.
/// </para>
/// </summary>
public record SavedRouteSummaryDto(
    Guid Id,
    string Name,
    double DistanceMeters,
    double DurationSeconds,
    DateTimeOffset CreatedAt);

/// <summary>
/// Response body for <c>GET /saved-routes</c>. Serializes to camelCase: <c>items</c>.
/// <para>
/// An envelope rather than a bare array so a <c>cursor</c> can be added when paging arrives, without
/// breaking a client whose response guard checks the shape it was given.
/// </para>
/// </summary>
public record SavedRouteListResponseDto(IReadOnlyList<SavedRouteSummaryDto> Items);

/// <summary>
/// Response body for <c>GET /saved-routes/{id}</c>: one ride, geometry included, which is what makes
/// it drawable again. Serializes to camelCase: <c>id</c>, <c>name</c>, <c>start</c>,
/// <c>startLabel</c>, <c>requestedDistanceKm</c>, <c>distanceMeters</c>, <c>durationSeconds</c>,
/// <c>geometry</c>, <c>createdAt</c>.
/// </summary>
public record SavedRouteDetailDto(
    Guid Id,
    string Name,
    Coord Start,
    string? StartLabel,
    double RequestedDistanceKm,
    double DistanceMeters,
    double DurationSeconds,
    IReadOnlyList<Coord> Geometry,
    DateTimeOffset CreatedAt);

/// <summary>Ceilings the read endpoints apply, named so a test and the endpoint cannot disagree.</summary>
public static class SavedRouteListLimits
{
    /// <summary>
    /// Most rows <c>GET /saved-routes</c> will return. Applied as a SQL <c>LIMIT</c>, not a trim
    /// after the rows arrive: without it a rider's list is an unbounded read that grows with every
    /// save. There is no paging yet, so a rider with more than this many saved routes sees only the
    /// newest — accepted at MVP volumes, and the envelope leaves room for a cursor later.
    /// </summary>
    public const int MaxItems = 50;
}
