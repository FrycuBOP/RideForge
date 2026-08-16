namespace RideForgeApi.Routing;

/// <summary>Input validation for the stitch endpoint. Extracted so it is unit-testable.</summary>
public static class RouteValidation
{
    /// <summary>
    /// Validates an incoming stitch request. Returns a human-readable error message when the
    /// request is invalid (→ endpoint responds 400), or <c>null</c> when it is well-formed.
    /// </summary>
    public static string? Validate(StitchRequestDto? dto)
    {
        if (dto?.Waypoints is null || dto.Waypoints.Count < 2)
        {
            return "At least two waypoints are required.";
        }

        for (var i = 0; i < dto.Waypoints.Count; i++)
        {
            var c = dto.Waypoints[i];
            if (double.IsNaN(c.Lat) || double.IsNaN(c.Lng)
                || c.Lat < -90 || c.Lat > 90 || c.Lng < -180 || c.Lng > 180)
            {
                return $"Waypoint {i} is out of range (lat -90..90, lng -180..180).";
            }
        }

        return null;
    }
}
