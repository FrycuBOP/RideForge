using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RideForgeApi.Persistence;
using RideForgeApi.SavedRoutes;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the two claims <see cref="SavedRouteQueriesTests"/> cannot make: that the owner filter and
/// the cap reach Postgres, and that the list never asks for the <c>geometry</c> column.
/// <para>
/// LINQ-to-objects proves the expressions are <em>correct</em>; it says nothing about where they run.
/// A <c>ToListAsync()</c> followed by a C# <c>Where</c>/<c>Take</c>/<c>Select</c> would satisfy every
/// test in that file while reading every rider's rows — ~600 KB of jsonb each — into the API's
/// memory. <c>ToQueryString()</c> builds the SQL without opening a connection, so these run under
/// <see cref="RideForgeApiFactory"/>'s deliberately unroutable connection string.
/// </para>
/// <para>
/// Asserted on column names and keywords rather than the whole statement: a legitimate query rewrite
/// by a future EF or Npgsql should not fail this for the wrong reason.
/// </para>
/// </summary>
public class SavedRouteQuerySqlTests : IClassFixture<RideForgeApiFactory>
{
    private readonly RideForgeApiFactory _factory;

    public SavedRouteQuerySqlTests(RideForgeApiFactory factory) => _factory = factory;

    /// <summary>The SQL the list endpoint's query compiles to. Opens no connection.</summary>
    private string ListSql()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RideForgeDbContext>();

        return db.SavedRoutes.AsNoTracking()
            .SummariesOwnedBy(Guid.NewGuid(), SavedRouteListLimits.MaxItems)
            .ToQueryString();
    }

    [Fact]
    public void ListSql_FiltersOnOwnerId_ThroughAParameter()
    {
        var sql = ListSql();

        Assert.Contains("owner_id", sql);
        Assert.Contains("WHERE", sql);

        // Parameterised, not inlined. An inlined literal would mean the id went through string
        // building on its way into the statement, which is the shape SQL injection lives in — and
        // it defeats plan caching for a query that runs on every list.
        Assert.Contains("@", sql);
    }

    [Fact]
    public void ListSql_AppliesTheCapAsALimit()
    {
        // Postgres discards the surplus rows; the API never receives them. A Take applied after
        // materialising would trim the same list and read every row to do it.
        var sql = ListSql();

        Assert.Contains("LIMIT", sql);
    }

    [Fact]
    public void ListSql_NeverSelectsGeometry()
    {
        // The whole reason the summary DTO exists. A full-size ride is ~600 KB of jsonb; a list that
        // selected it would grow without bound in a rider's own hands, and nothing else in the stack
        // would notice until a response timed out.
        var sql = ListSql();

        Assert.DoesNotContain("geometry", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListSql_OrdersByCreatedAtThenId_Descending()
    {
        // The ordering is what makes the cap meaningful: without it, LIMIT 50 keeps an arbitrary 50.
        var sql = ListSql();

        var createdAt = sql.LastIndexOf("created_at", StringComparison.Ordinal);
        var orderBy = sql.LastIndexOf("ORDER BY", StringComparison.Ordinal);

        Assert.True(orderBy >= 0, $"No ORDER BY in the generated SQL:\n{sql}");
        Assert.True(createdAt > orderBy, $"created_at is not ordered on:\n{sql}");
        Assert.Contains("DESC", sql);
    }

    [Fact]
    public void DetailSql_FiltersOnOwnerIdAsWellAsId()
    {
        // The IDOR guard, as Postgres sees it. A statement naming only id would return another
        // rider's row and the endpoint would happily serialize it.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RideForgeDbContext>();

        var sql = db.SavedRoutes.AsNoTracking()
            .OwnedRoute(Guid.NewGuid(), Guid.NewGuid())
            .ToQueryString();

        Assert.Contains("owner_id", sql);
        Assert.Contains("WHERE", sql);

        // And the other half of the list's geometry claim: the column name *does* appear in SQL that
        // selects it, so ListSql_NeverSelectsGeometry is asserting an absence that could be present.
        Assert.Contains("geometry", sql, StringComparison.OrdinalIgnoreCase);
    }
}
