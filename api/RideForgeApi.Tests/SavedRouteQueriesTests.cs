using RideForgeApi.Persistence;
using RideForgeApi.SavedRoutes;

namespace RideForgeApi.Tests;

/// <summary>
/// Executes the real ownership, ordering and cap expressions from <see cref="SavedRouteQueries"/>
/// against an in-memory list.
/// <para>
/// These are the tests that actually run. The RLS policy on <c>rideforge.saved_routes</c> is
/// <c>USING (true)</c>, so the <c>OwnerId</c> predicate is the only thing between two riders — and
/// the real-Postgres suite that would otherwise prove it is skipped, because there is no local
/// Postgres. A guard nothing executes is a comment. <c>List&lt;SavedRoute&gt;.AsQueryable()</c> runs
/// the same expression trees as LINQ-to-objects, so what is asserted here is the shipped filter, not
/// a stub's imitation of it.
/// </para>
/// <para>
/// The oracle is the plan's rule, not the query's output: a rider sees their own routes and no
/// others, newest first with a deterministic tiebreak, at most
/// <see cref="SavedRouteListLimits.MaxItems"/> of them.
/// </para>
/// </summary>
public class SavedRouteQueriesTests
{
    private static readonly Guid RiderA = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid RiderB = Guid.Parse("b0000000-0000-4000-8000-000000000002");

    private static readonly DateTimeOffset Epoch = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SavedRoute Route(Guid owner, int minutesOld, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OwnerId = owner,
        ClientRouteId = Guid.NewGuid(),
        Name = $"Loop · {minutesOld}",
        StartLat = 50.0647,
        StartLng = 19.9450,
        RequestedDistanceKm = 40,
        DistanceMeters = 41_600,
        DurationSeconds = 3_120,
        Geometry = [new(50.0647, 19.9450), new(50.0712, 19.9611)],
        CreatedAt = Epoch.AddMinutes(-minutesOld),
    };

    /// <summary>Rider A's and rider B's routes interleaved in time, so an owner filter is load-bearing.</summary>
    private static IQueryable<SavedRoute> TwoRiders(out SavedRoute[] aNewestFirst, out SavedRoute bRoute)
    {
        var aOld = Route(RiderA, minutesOld: 30);
        bRoute = Route(RiderB, minutesOld: 20);
        var aNew = Route(RiderA, minutesOld: 10);

        aNewestFirst = [aNew, aOld];
        return new List<SavedRoute> { aOld, bRoute, aNew }.AsQueryable();
    }

    [Fact]
    public void SummariesOwnedBy_ReturnsOnlyTheCallersRoutes()
    {
        // The list IDOR regression. Drop the OwnerId predicate and this is the only test that fails.
        var routes = TwoRiders(out var aNewestFirst, out var bRoute);

        var summaries = routes.SummariesOwnedBy(RiderA, SavedRouteListLimits.MaxItems).ToList();

        Assert.Equal(aNewestFirst.Select(r => r.Id), summaries.Select(s => s.Id));
        Assert.DoesNotContain(summaries, s => s.Id == bRoute.Id);
    }

    [Fact]
    public void SummariesOwnedBy_OrdersNewestFirst()
    {
        var routes = TwoRiders(out var aNewestFirst, out _);

        var summaries = routes.SummariesOwnedBy(RiderA, SavedRouteListLimits.MaxItems).ToList();

        Assert.Equal(aNewestFirst.Select(r => r.CreatedAt), summaries.Select(s => s.CreatedAt));
    }

    [Fact]
    public void SummariesOwnedBy_BreaksTiedTimestampsByIdDescending()
    {
        // created_at defaults to now(), which is transaction-scoped: two routes saved in one
        // transaction share a timestamp. Without the Id tiebreak their order is whatever Postgres
        // feels like, and the cap could keep a different pair on each call.
        var lower = Route(RiderA, minutesOld: 5, id: Guid.Parse("00000000-0000-4000-8000-000000000001"));
        var higher = Route(RiderA, minutesOld: 5, id: Guid.Parse("00000000-0000-4000-8000-000000000002"));
        var routes = new List<SavedRoute> { lower, higher }.AsQueryable();

        var summaries = routes.SummariesOwnedBy(RiderA, SavedRouteListLimits.MaxItems).ToList();

        Assert.Equal([higher.Id, lower.Id], summaries.Select(s => s.Id));
    }

    [Fact]
    public void SummariesOwnedBy_KeepsAtMostTheLimit_AndKeepsTheNewest()
    {
        // Oldest first in the source, so a cap applied before the ordering would keep the wrong rows.
        var routes = Enumerable.Range(1, SavedRouteListLimits.MaxItems + 10)
            .Select(i => Route(RiderA, minutesOld: SavedRouteListLimits.MaxItems + 10 - i))
            .ToList()
            .AsQueryable();

        var summaries = routes.SummariesOwnedBy(RiderA, SavedRouteListLimits.MaxItems).ToList();

        Assert.Equal(SavedRouteListLimits.MaxItems, summaries.Count);
        Assert.Equal(Epoch, summaries[0].CreatedAt);
        Assert.All(summaries, s => Assert.True(
            s.CreatedAt >= Epoch.AddMinutes(-(SavedRouteListLimits.MaxItems - 1)),
            $"Kept a route from {s.CreatedAt}, which is older than the newest {SavedRouteListLimits.MaxItems}."));
    }

    [Fact]
    public void SummariesOwnedBy_IsEmptyForARiderWithNoRoutes()
    {
        // Having saved nothing is a legitimate state, not an error or a missing resource — the
        // endpoint answers 200 with an empty items array on the strength of this.
        var routes = TwoRiders(out _, out _);

        var summaries = routes.SummariesOwnedBy(Guid.NewGuid(), SavedRouteListLimits.MaxItems).ToList();

        Assert.Empty(summaries);
    }

    [Fact]
    public void OwnedRoute_YieldsNothingForAnotherRidersRoute()
    {
        // The detail IDOR regression. The id exists; the caller is not its owner. Filtering on the id
        // alone — the obvious way to write this endpoint — hands rider B's ride to rider A.
        var routes = TwoRiders(out _, out var bRoute);

        Assert.Empty(routes.OwnedRoute(RiderA, bRoute.Id));
    }

    [Fact]
    public void OwnedRoute_YieldsTheRouteForItsOwner()
    {
        // The other direction: without this, an OwnedRoute that returned nothing at all would pass
        // the test above and 404 every rider on their own rides.
        var routes = TwoRiders(out var aNewestFirst, out _);
        var mine = aNewestFirst[0];

        Assert.Equal(mine.Id, Assert.Single(routes.OwnedRoute(RiderA, mine.Id)).Id);
    }
}
