using System.Diagnostics.CodeAnalysis;

namespace RideForgeApi.Routing;

/// <summary>Input validation for the stitch endpoint. Extracted so it is unit-testable.</summary>
public static class RouteValidation
{
    /// <summary>Upper bound on a requested ride distance; guards against absurd loops / runaway generation.</summary>
    public const double MaxDistanceKm = 500.0;

    /// <summary>
    /// Latitude ceiling for a generation start. The generator's equirectangular offset divides by
    /// <c>cos(latitude)</c>, which blows up as the loop's centre approaches a pole — at the 500 km
    /// ceiling a start at 89.541 puts the centre exactly on the pole and produces longitudes around
    /// 7.5e15. 85 leaves ample margin (the widest loop reaches ~0.92 degrees north of its start) and
    /// excludes nothing rideable.
    /// </summary>
    public const double MaxGenerationLatitude = 85.0;

    /// <summary>
    /// The coordinate-range rule every endpoint shares: lat -90..90, lng -180..180, no NaN. A null
    /// point (a JSON <c>null</c> inside an array) is out of range rather than a crash.
    /// </summary>
    public static bool IsInRange([NotNullWhen(true)] Coord? c) =>
        c is not null
        && !double.IsNaN(c.Lat) && !double.IsNaN(c.Lng)
        && c.Lat >= -90 && c.Lat <= 90 && c.Lng >= -180 && c.Lng <= 180;

    /// <summary>
    /// Validates an incoming generate request. Returns a human-readable error message when the
    /// request is invalid (→ endpoint responds 400), or <c>null</c> when it is well-formed.
    /// </summary>
    public static string? Validate(GenerateRequestDto? dto)
    {
        if (dto?.Start is null)
        {
            return "A start location is required.";
        }

        if (!IsInRange(dto.Start))
        {
            return "Start is out of range (lat -90..90, lng -180..180).";
        }

        if (Math.Abs(dto.Start.Lat) > MaxGenerationLatitude)
        {
            return $"Start is too close to a pole for loop generation (max latitude {MaxGenerationLatitude}).";
        }

        if (dto.DistanceKm is null || double.IsNaN(dto.DistanceKm.Value)
            || dto.DistanceKm <= 0 || dto.DistanceKm > MaxDistanceKm)
        {
            return $"Distance must be greater than 0 and at most {MaxDistanceKm} km.";
        }

        return null;
    }

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
            if (!IsInRange(dto.Waypoints[i]))
            {
                return $"Waypoint {i} is out of range (lat -90..90, lng -180..180).";
            }
        }

        return null;
    }
}
