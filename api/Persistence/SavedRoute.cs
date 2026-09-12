using RideForgeApi.Routing;

namespace RideForgeApi.Persistence;

/// <summary>
/// A ride a signed-in rider chose to keep (FR-009): the generated output, the inputs that produced
/// it, and a server-derived name. This is the storage shape; the wire shape lives in its own DTOs so
/// the two can evolve independently.
/// </summary>
public sealed class SavedRoute
{
    /// <summary>Row identity. Assigned by the API on insert, never taken from the client.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The rider who saved it — the verified token's <c>sub</c> claim. Never read from a request
    /// body; that is the whole ownership rule.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Id the app mints once per generated route. Unique per owner, which is what makes a double tap
    /// or a retry after a lost response return the existing row instead of a duplicate.
    /// </summary>
    public Guid ClientRouteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double StartLat { get; set; }

    public double StartLng { get; set; }

    /// <summary>What the rider typed as the start, when they typed anything.</summary>
    public string? StartLabel { get; set; }

    /// <summary>The distance the rider asked for, as opposed to the stitched <see cref="DistanceMeters"/>.</summary>
    public double RequestedDistanceKm { get; set; }

    public double DistanceMeters { get; set; }

    public double DurationSeconds { get; set; }

    /// <summary>The road-following polyline, in order. Stored as one <c>jsonb</c> column.</summary>
    public List<Coord> Geometry { get; set; } = [];

    /// <summary>Stamped by the database (<c>now()</c>), always UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
