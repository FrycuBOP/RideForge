using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using RideForgeApi.Auth;
using RideForgeApi.Persistence;
using RideForgeApi.Routing;

namespace RideForgeApi.SavedRoutes;

/// <summary>
/// The saved-routes surface: one write and two owner-scoped reads, declared together because they
/// share an owner rule, a database-failure contract, and a table whose RLS policy guards none of it.
/// </summary>
public static class SavedRoutesEndpoints
{
    // What a database failure is called, in the log line and in the body the rider sees. Split by
    // direction: a read that fails has not failed to save anything, and a log line claiming otherwise
    // sends whoever is diagnosing it to the wrong endpoint.
    private const string SaveOperation = "Saving a route";
    private const string ReadOperation = "Reading saved routes";
    private const string SaveFailedDetail = "The route could not be saved right now. Try again shortly.";
    private const string ReadFailedDetail = "Your saved routes could not be loaded right now. Try again shortly.";

    /// <summary>
    /// Maps the three saved-route endpoints.
    /// <para>
    /// <c>RequireAuthorization</c> is applied per endpoint rather than to a route group on purpose. A
    /// group-level default is one edit away from silently covering a future endpoint that should not
    /// have it, and the per-endpoint call is what <c>SavedRoutesReadEndpointTests</c> is really
    /// asserting.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapSavedRoutes(this IEndpointRouteBuilder app)
    {
        // Save a generated ride to the signed-in rider's account (FR-009): 401 no/invalid token, 400 bad
        // payload, 201 saved, 200 already saved, 503 database unavailable.
        //
        // The owner is the verified token's sub and nothing else — the request DTO has no owner field to
        // bind. A repeat save of the same clientRouteId (a double tap, a retry after a lost response) hands
        // back the rider's existing row. That is decided by the per-owner unique index, not a pre-check:
        // select-then-insert would leave a race between two concurrent taps. First write wins; a repeat
        // carrying a different payload changes nothing. No rate limit: a save is one bounded insert, not a
        // billed provider call.
        app.MapPost("/saved-routes", async (
            SaveRouteRequestDto dto,
            ClaimsPrincipal user,
            RideForgeDbContext db,
            // ILogger<Program> rather than one typed to this class: moving these declarations out of
            // Program.cs must not change a log line, and the category is part of one.
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var error = SavedRouteValidation.Validate(dto);
            if (error is not null)
            {
                return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
            }

            // Supabase subjects are always UUIDs. A validly signed token whose sub is not one identifies
            // nobody this API can store a route for.
            if (!RiderIdentity.TryGetRiderId(user, out var ownerId))
            {
                return Results.Unauthorized();
            }

            var startLabel = string.IsNullOrWhiteSpace(dto.StartLabel) ? null : dto.StartLabel.Trim();
            var route = new SavedRoute
            {
                OwnerId = ownerId,
                ClientRouteId = dto.ClientRouteId!.Value,
                Name = SavedRouteNaming.Derive(startLabel, dto.DistanceMeters!.Value),
                StartLat = dto.Start!.Lat,
                StartLng = dto.Start.Lng,
                StartLabel = startLabel,
                RequestedDistanceKm = dto.RequestedDistanceKm!.Value,
                DistanceMeters = dto.DistanceMeters.Value,
                DurationSeconds = dto.DurationSeconds!.Value,
                Geometry = [.. dto.Geometry!],
            };

            try
            {
                try
                {
                    db.SavedRoutes.Add(route);
                    await db.SaveChangesAsync(ct);
                    return Results.Created(
                        (string?)null, new SavedRouteResponseDto(route.Id, route.Name, route.CreatedAt));
                }
                catch (DbUpdateException ex) when (IsRepeatSave(ex))
                {
                    // The failed entity is still tracked as Added, so this context must not SaveChanges
                    // again. A no-tracking read is all the repeat path needs.
                    var existing = await db.SavedRoutes.AsNoTracking()
                        .Where(r => r.OwnerId == ownerId && r.ClientRouteId == route.ClientRouteId)
                        .Select(r => new SavedRouteResponseDto(r.Id, r.Name, r.CreatedAt))
                        .SingleOrDefaultAsync(ct);

                    if (existing is not null)
                    {
                        return Results.Ok(existing);
                    }

                    // The index said the row exists and the read found none. Nothing deletes saved routes
                    // yet, so this is not expected; answer as unavailable rather than invent a result.
                    logger.LogWarning("Repeat save hit the per-owner unique index but no existing row was found.");
                    return DatabaseFailures.DatabaseUnavailable(SaveFailedDetail);
                }
            }
            catch (Exception ex) when (DatabaseFailures.IsDatabaseFailure(ex))
            {
                DatabaseFailures.LogDatabaseFailure(logger, ex, SaveOperation);
                return DatabaseFailures.DatabaseUnavailable(SaveFailedDetail);
            }
        }).RequireAuthorization();

        // The signed-in rider's saved rides, newest first (FR-010): 401 no/invalid token, 200 with the
        // envelope (an empty items array for a rider with none — having saved nothing is not a missing
        // resource), 503 database unavailable.
        //
        // Whose routes these are is decided by the token's sub and nothing else. That is not belt-and-braces
        // here: the RLS policy on the table is USING (true), so SavedRouteQueries' owner predicate is the
        // only thing between two riders. Capped and geometry-free — see SavedRouteListLimits.MaxItems and
        // SavedRouteSummaryDto. No rate limit, matching the save: this is one bounded indexed read.
        app.MapGet("/saved-routes", async (
            ClaimsPrincipal user,
            RideForgeDbContext db,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (!RiderIdentity.TryGetRiderId(user, out var ownerId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var items = await db.SavedRoutes.AsNoTracking()
                    .SummariesOwnedBy(ownerId, SavedRouteListLimits.MaxItems)
                    .ToListAsync(ct);

                return Results.Ok(new SavedRouteListResponseDto(items));
            }
            catch (Exception ex) when (DatabaseFailures.IsDatabaseFailure(ex))
            {
                DatabaseFailures.LogDatabaseFailure(logger, ex, ReadOperation);
                return DatabaseFailures.DatabaseUnavailable(ReadFailedDetail);
            }
        }).RequireAuthorization();

        // One saved ride, geometry included, so the app can draw it again (FR-010): 401 no/invalid token,
        // 404 unknown, 200 with the ride, 503 database unavailable.
        //
        // A route owned by another rider answers 404 — the same answer as one that does not exist, and
        // deliberately so. A 403 would confirm the id names a real route, turning this endpoint into a probe
        // for other riders' ids. The :guid constraint disposes of a malformed id as a 404 before the handler
        // runs.
        app.MapGet("/saved-routes/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            RideForgeDbContext db,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (!RiderIdentity.TryGetRiderId(user, out var ownerId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var route = await db.SavedRoutes.AsNoTracking()
                    .OwnedRoute(ownerId, id)
                    .SingleOrDefaultAsync(ct);

                return route is null
                    ? Results.NotFound()
                    : Results.Ok(new SavedRouteDetailDto(
                        route.Id,
                        route.Name,
                        new Coord(route.StartLat, route.StartLng),
                        route.StartLabel,
                        route.RequestedDistanceKm,
                        route.DistanceMeters,
                        route.DurationSeconds,
                        route.Geometry,
                        route.CreatedAt));
            }
            catch (Exception ex) when (DatabaseFailures.IsDatabaseFailure(ex))
            {
                DatabaseFailures.LogDatabaseFailure(logger, ex, ReadOperation);
                return DatabaseFailures.DatabaseUnavailable(ReadFailedDetail);
            }
        }).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// A repeat save: the insert collided with the per-owner unique index. Matched by constraint name,
    /// so any other unique violation is not mistaken for one and handed someone's existing row.
    /// </summary>
    private static bool IsRepeatSave(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RideForgeDbContext.OwnerClientRouteIndex,
        };
}
