using System.Text.Json.Nodes;

namespace RideForgeApi.Tests;

/// <summary>
/// Request bodies for <c>POST /saved-routes</c>, built as JSON rather than through the DTO so a test
/// can send exactly what a client would — including fields the DTO does not have, and nulls where it
/// expects values.
/// </summary>
public static class SavedRoutePayload
{
    /// <summary>The stitched distance of <see cref="Valid"/>; the naming rule turns it into "42 km".</summary>
    public const double DistanceMeters = 41_600;

    /// <summary>
    /// A loop around Kraków. Every latitude is near 50 and every longitude near 19–20, so a lat/lng
    /// swap anywhere between request and storage is visible in the stored values, and every point is
    /// distinct so a reordering is too.
    /// </summary>
    public static readonly (double Lat, double Lng)[] Geometry =
    [
        (50.0647, 19.9450),
        (50.0712, 19.9611),
        (50.0839, 19.9528),
        (50.0801, 19.9302),
        (50.0655, 19.9447),
    ];

    /// <summary>A save the endpoint accepts. Each call is a fresh object the caller may mutate.</summary>
    public static JsonObject Valid(Guid? clientRouteId = null) => new()
    {
        ["clientRouteId"] = (clientRouteId ?? Guid.NewGuid()).ToString(),
        ["start"] = Point(50.0647, 19.9450),
        ["startLabel"] = "Kraków",
        ["requestedDistanceKm"] = 40.0,
        ["geometry"] = Points(Geometry),
        ["distanceMeters"] = DistanceMeters,
        ["durationSeconds"] = 3_120.0,
    };

    public static JsonObject Point(double lat, double lng) => new() { ["lat"] = lat, ["lng"] = lng };

    public static JsonArray Points(IEnumerable<(double Lat, double Lng)> points) =>
        [.. points.Select(p => (JsonNode)Point(p.Lat, p.Lng))];

    /// <summary><paramref name="count"/> in-range points, alternating so none repeats its neighbour.</summary>
    public static JsonArray Points(int count, double lat = 50.0, double lng = 19.0) =>
        Points(Enumerable.Range(0, count).Select(i => i % 2 == 0 ? (lat, lng) : (-lat, -lng)));
}
