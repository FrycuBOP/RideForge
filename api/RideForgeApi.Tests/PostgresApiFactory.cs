using System.Collections.Concurrent;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RideForgeApi.Persistence;

namespace RideForgeApi.Tests;

/// <summary>
/// A signed-in host backed by the real Postgres named in <c>RIDEFORGE_TEST_DB</c>, for the rules only
/// a real database enforces.
/// <para>
/// Once per fixture it applies the migrations — which doubles as the proof that the migration applies
/// to a real, empty Postgres — and when the fixture ends it deletes every row owned by a rider it
/// handed out through <see cref="NewRider"/>. Each test mints its own riders and client route ids, so
/// no test depends on another having run first, or on the rows another one left.
/// </para>
/// <para>
/// Connect as the database owner (a local <c>postgres</c> is fine): the migration creates the
/// <c>rideforge_api</c> role. Owners bypass row-level security, so this suite cannot catch a
/// missing RLS policy for the runtime role — only a deployed save can.
/// </para>
/// <para>
/// With the variable unset every <see cref="PostgresFactAttribute"/> test is skipped, but xUnit still
/// constructs this fixture; it then does nothing and never builds a host.
/// </para>
/// </summary>
public class PostgresApiFactory : AuthenticatedApiFactory, IAsyncLifetime
{
    private readonly ConcurrentBag<Guid> _riders = [];

    /// <summary>A rider id no other test uses, remembered so its rows are cleaned up.</summary>
    public Guid NewRider()
    {
        var rider = Guid.NewGuid();
        _riders.Add(rider);
        return rider;
    }

    /// <summary>Runs <paramref name="query"/> against the test database through a fresh context.</summary>
    public async Task<T> QueryAsync<T>(Func<RideForgeDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<RideForgeDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        if (PostgresFactAttribute.ConnectionString is { } connectionString)
        {
            builder.UseSetting("ConnectionStrings:RideForge", connectionString);
        }
    }

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.ConnectionString is null)
        {
            return;
        }

        await QueryAsync(async db =>
        {
            await db.Database.MigrateAsync();
            return 0;
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (PostgresFactAttribute.ConnectionString is null || _riders.IsEmpty)
        {
            return;
        }

        var riders = _riders.ToArray();
        await QueryAsync(db => db.SavedRoutes.Where(r => riders.Contains(r.OwnerId)).ExecuteDeleteAsync());
    }
}
