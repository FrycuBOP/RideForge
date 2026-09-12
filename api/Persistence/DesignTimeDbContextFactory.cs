using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RideForgeApi.Persistence;

/// <summary>
/// How <c>dotnet ef</c> and the migration bundle build the context — without booting the app host.
/// <para>
/// Left to itself the tooling runs <c>Program.cs</c> up to <c>Build()</c> to find the context, and
/// that trips every fail-fast check there: a blank <c>Supabase:ProjectUrl</c>, a blank connection
/// string. Adding a migration should need neither.
/// </para>
/// <para>
/// The tools act as the migration identity, never the runtime one: the connection string comes from
/// <c>ConnectionStrings__RideForgeMigrations</c> (the <c>rideforge_migrator</c> role in Supabase), not
/// the API's <c>ConnectionStrings__RideForge</c>, whose role has no DDL rights. Unset, it falls back
/// to a local placeholder so <c>migrations add</c> works with nothing configured — that command never
/// connects. <c>database update</c> does connect, so always run it with the variable set explicitly
/// to the database you mean to change. On Railway, <c>migrate.sh</c> passes the same variable to the
/// bundle as <c>--connection</c>.
/// </para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RideForgeDbContext>
{
    private const string LocalPlaceholder =
        "Host=localhost;Port=5432;Database=rideforge;Username=postgres;Password=postgres";

    public RideForgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__RideForgeMigrations");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = LocalPlaceholder;
        }

        var options = new DbContextOptionsBuilder<RideForgeDbContext>();
        options.UseRideForgeDatabase(connectionString);

        return new RideForgeDbContext(options.Options);
    }
}
