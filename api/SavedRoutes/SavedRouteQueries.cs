using RideForgeApi.Persistence;

namespace RideForgeApi.SavedRoutes;

/// <summary>
/// The ownership rule for reading saved routes, plus the ordering, cap and summary projection that
/// go with it.
/// <para>
/// These live here rather than inline in the endpoints for one reason: the RLS policy on
/// <c>rideforge.saved_routes</c> is <c>USING (true)</c> — an exposure guard that keeps the table away
/// from the anon key and scopes nothing per rider. The <c>OwnerId</c> predicate below is therefore
/// the <em>whole</em> guard between two riders, and a guard that important has to be executable by a
/// test. As pure functions over <see cref="IQueryable{T}"/> they compose into SQL against EF and run
/// as LINQ-to-objects against a <c>List&lt;SavedRoute&gt;.AsQueryable()</c>, so
/// <c>SavedRouteQueriesTests</c> executes the real filter expression without a database — which
/// matters here, because the real-Postgres suite is skipped in practice.
/// </para>
/// </summary>
public static class SavedRouteQueries
{
    /// <summary>
    /// A rider's saved routes as geometry-free summaries: newest first, capped at
    /// <paramref name="limit"/>.
    /// <para>
    /// The <c>Select</c> stays inside the <see cref="IQueryable{T}"/> on purpose. Translated, it
    /// becomes a SELECT naming only these five columns and the ~600 KB <c>geometry</c> blob never
    /// leaves Postgres; materialising first and mapping in C# would read every blob into the API's
    /// memory and produce exactly the unbounded read this shape exists to avoid.
    /// <c>SavedRouteQuerySqlTests</c> is what keeps a future refactor from making that mistake
    /// silently.
    /// </para>
    /// <para>
    /// The <c>Id</c> tiebreak is not decoration. <c>created_at</c> defaults to <c>now()</c>, which is
    /// transaction-scoped, so two routes saved in one transaction share a timestamp — without it the
    /// order between them is whatever Postgres feels like, and the cap could keep a different pair on
    /// each call.
    /// </para>
    /// </summary>
    public static IQueryable<SavedRouteSummaryDto> SummariesOwnedBy(
        this IQueryable<SavedRoute> routes, Guid ownerId, int limit) =>
        routes
            .Where(r => r.OwnerId == ownerId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .Select(r => new SavedRouteSummaryDto(
                r.Id, r.Name, r.DistanceMeters, r.DurationSeconds, r.CreatedAt));

    /// <summary>
    /// One route, but only if <paramref name="ownerId"/> owns it. Filtering on
    /// <paramref name="routeId"/> alone is the IDOR bug this whole file exists to prevent: a route id
    /// is a guessable-enough handle, and nothing else in the stack would stop rider A reading rider
    /// B's ride.
    /// <para>
    /// A filter and nothing more. The caller loads the entity and maps it in memory because
    /// <c>Geometry</c> is an EF complex collection mapped with <c>ToJson</c>, and composing that
    /// inside a projection risks a translation failure that only a real database would surface —
    /// which is to say, never, given that suite is skipped. One row bounded by the save-side
    /// 20,000-point ceiling is a read this API can afford to do the safe way.
    /// </para>
    /// </summary>
    public static IQueryable<SavedRoute> OwnedRoute(
        this IQueryable<SavedRoute> routes, Guid ownerId, Guid routeId) =>
        routes.Where(r => r.OwnerId == ownerId && r.Id == routeId);
}
