using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RideForgeApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSavedRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rideforge");

            migrationBuilder.CreateTable(
                name: "saved_routes",
                schema: "rideforge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    start_lat = table.Column<double>(type: "double precision", nullable: false),
                    start_lng = table.Column<double>(type: "double precision", nullable: false),
                    start_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requested_distance_km = table.Column<double>(type: "double precision", nullable: false),
                    distance_meters = table.Column<double>(type: "double precision", nullable: false),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    geometry = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_routes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_saved_routes_owner_client_route",
                schema: "rideforge",
                table: "saved_routes",
                columns: new[] { "owner_id", "client_route_id" },
                unique: true);

            // Hand-written from here on. SQL literals rather than C# constants on purpose: a migration
            // records the schema as it was, and must not change meaning if a constant is renamed later.

            // The runtime role the API connects as. In Supabase it already exists — bootstrap-supabase.sql
            // creates it with a login, and rideforge_migrator could not create roles anyway — so this
            // is skipped there. It exists for a fresh database (a local Postgres for the opt-in tests),
            // where the grants below need the role to exist. Created NOLOGIN and without a password, so
            // no credential is ever in the repo. Roles are cluster-wide, hence the existence check: a
            // re-run, or a second database on the same cluster, must not fail here.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rideforge_api') THEN
                        CREATE ROLE rideforge_api NOLOGIN;
                    END IF;
                END
                $$;
                """);

            // Least privilege: read and insert saved routes, nothing else. No UPDATE or DELETE until a
            // slice needs them, and no DDL ever — migrations run as rideforge_migrator, which owns the
            // schema. Every future migration that adds a table must grant it here the same way.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA rideforge TO rideforge_api;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON rideforge.saved_routes TO rideforge_api;");

            // Defense in depth behind the schema choice. RLS binds every role except the table owner
            // (rideforge_migrator in Supabase) and BYPASSRLS roles, so it shuts out anon/authenticated
            // even if the schema is ever exposed through the Data API by mistake.
            migrationBuilder.Sql("ALTER TABLE rideforge.saved_routes ENABLE ROW LEVEL SECURITY;");

            // Not optional. rideforge_api is not the owner either, so with RLS on and no policy naming
            // it, every insert fails and every select returns zero rows. Ownership is enforced by the
            // API from the verified token, not by this policy — it only lets the API role through.
            // The Postgres test suite connects as the owner and cannot catch this policy going missing.
            migrationBuilder.Sql("""
                CREATE POLICY saved_routes_rideforge_api ON rideforge.saved_routes
                    FOR ALL TO rideforge_api
                    USING (true)
                    WITH CHECK (true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverses the hand-written steps, newest first. The role itself stays: it may hold grants
            // or a login set outside this migration, and dropping it is not a migration's job.
            migrationBuilder.Sql("DROP POLICY IF EXISTS saved_routes_rideforge_api ON rideforge.saved_routes;");
            migrationBuilder.Sql("ALTER TABLE rideforge.saved_routes DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("REVOKE SELECT, INSERT ON rideforge.saved_routes FROM rideforge_api;");
            migrationBuilder.Sql("REVOKE USAGE ON SCHEMA rideforge FROM rideforge_api;");

            migrationBuilder.DropTable(
                name: "saved_routes",
                schema: "rideforge");
        }
    }
}
