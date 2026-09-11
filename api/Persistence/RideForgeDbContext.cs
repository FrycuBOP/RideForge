using Microsoft.EntityFrameworkCore;

namespace RideForgeApi.Persistence;

/// <summary>
/// The API's only database context (S-06).
/// <para>
/// Everything lives in the <c>rideforge</c> schema, never <c>public</c>. Supabase's Data API serves
/// <c>public</c> to the anon key, and that key ships inside the app — a table there, which gets no
/// row-level security by default, would be readable and writable by anyone holding the app. The
/// migrations history table is moved too, so not even EF's bookkeeping lands in <c>public</c>.
/// </para>
/// </summary>
public sealed class RideForgeDbContext(DbContextOptions<RideForgeDbContext> options) : DbContext(options)
{
    public const string Schema = "rideforge";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public DbSet<SavedRoute> SavedRoutes => Set<SavedRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SavedRoute>(route =>
        {
            route.ToTable("saved_routes");

            route.HasKey(r => r.Id);
            route.Property(r => r.Id).HasColumnName("id");
            route.Property(r => r.OwnerId).HasColumnName("owner_id");
            route.Property(r => r.ClientRouteId).HasColumnName("client_route_id");
            route.Property(r => r.Name).HasColumnName("name").HasMaxLength(120);
            route.Property(r => r.StartLat).HasColumnName("start_lat");
            route.Property(r => r.StartLng).HasColumnName("start_lng");
            route.Property(r => r.StartLabel).HasColumnName("start_label").HasMaxLength(200);
            route.Property(r => r.RequestedDistanceKm).HasColumnName("requested_distance_km");
            route.Property(r => r.DistanceMeters).HasColumnName("distance_meters");
            route.Property(r => r.DurationSeconds).HasColumnName("duration_seconds");
            route.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            route.ComplexCollection(r => r.Geometry, geometry =>
            {
                geometry.ToJson("geometry");
                geometry.Property(c => c.Lat).HasJsonPropertyName("lat");
                geometry.Property(c => c.Lng).HasJsonPropertyName("lng");
            });

            // Per owner, never client_route_id alone. A global unique would send rider B's save of
            // an id rider A already used down the conflict branch — and hand B rider A's row.
            route.HasIndex(r => new { r.OwnerId, r.ClientRouteId })
                .IsUnique()
                .HasDatabaseName("ux_saved_routes_owner_client_route");
        });
    }
}

public static class RideForgeDbContextOptions
{
    /// <summary>
    /// The one place the provider is configured, shared by the app host and the design-time factory
    /// so the two can never disagree about where the migrations history table lives.
    /// </summary>
    public static DbContextOptionsBuilder UseRideForgeDatabase(
        this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
            RideForgeDbContext.MigrationsHistoryTable, RideForgeDbContext.Schema));
}
