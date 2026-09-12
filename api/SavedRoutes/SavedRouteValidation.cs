using RideForgeApi.Routing;

namespace RideForgeApi.SavedRoutes;

/// <summary>
/// Input validation for <c>POST /saved-routes</c>. Everything malformed or oversized is refused
/// here with a 400, before the database is touched.
/// </summary>
public static class SavedRouteValidation
{
    /// <summary>
    /// Ceiling on stored geometry. Bounds the <c>jsonb</c> column to a few hundred KB per row; a real
    /// stitched loop at the 500 km distance ceiling stays well under it.
    /// </summary>
    public const int MaxGeometryPoints = 20_000;

    /// <summary>Matches the <c>start_label</c> column (<c>varchar(200)</c>).</summary>
    public const int MaxStartLabelLength = 200;

    /// <summary>
    /// Sanity ceiling on the stitched distance: twenty times the longest ride anyone can request.
    /// Nothing rideable comes near it; it exists so an absurd value (say 1e300) is refused as
    /// malformed rather than rendered into a 300-digit route name that overflows its column.
    /// </summary>
    public const double MaxDistanceMeters = RouteValidation.MaxDistanceKm * 1000 * 20;

    /// <summary>
    /// Validates an incoming save. Returns a human-readable error message when the request is invalid
    /// (→ endpoint responds 400), or <c>null</c> when it is well-formed.
    /// </summary>
    public static string? Validate(SaveRouteRequestDto? dto)
    {
        if (dto is null)
        {
            return "A request body is required.";
        }

        if (dto.ClientRouteId is null || dto.ClientRouteId == Guid.Empty)
        {
            return "A non-empty clientRouteId is required.";
        }

        if (!RouteValidation.IsInRange(dto.Start))
        {
            return "Start is required and must be in range (lat -90..90, lng -180..180).";
        }

        if (dto.RequestedDistanceKm is not { } requested || double.IsNaN(requested)
            || requested <= 0 || requested > RouteValidation.MaxDistanceKm)
        {
            return $"Requested distance must be greater than 0 and at most {RouteValidation.MaxDistanceKm} km.";
        }

        if (dto.Geometry is null || dto.Geometry.Count < 2 || dto.Geometry.Count > MaxGeometryPoints)
        {
            return $"Geometry must have between 2 and {MaxGeometryPoints} points.";
        }

        for (var i = 0; i < dto.Geometry.Count; i++)
        {
            if (!RouteValidation.IsInRange(dto.Geometry[i]))
            {
                return $"Geometry point {i} is out of range (lat -90..90, lng -180..180).";
            }
        }

        if (dto.DistanceMeters is not { } distance || !double.IsFinite(distance)
            || distance <= 0 || distance > MaxDistanceMeters)
        {
            return $"Distance must be a finite number greater than 0 and at most {MaxDistanceMeters} m.";
        }

        if (dto.DurationSeconds is not { } duration || !double.IsFinite(duration) || duration <= 0)
        {
            return "Duration must be a finite number greater than 0.";
        }

        if (dto.StartLabel is { Length: > MaxStartLabelLength })
        {
            return $"Start label must be at most {MaxStartLabelLength} characters.";
        }

        return null;
    }
}
