using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

using Npgsql;

using RideForgeApi.Auth;
using RideForgeApi.Persistence;
using RideForgeApi.RateLimiting;
using RideForgeApi.Routing;
using RideForgeApi.SavedRoutes;

var builder = WebApplication.CreateBuilder(args);

// The Expo web build is a browser app served from a different origin than this API, so
// cross-origin responses need CORS headers or the browser blocks the JS from reading them.
// MVP: allow any origin. Auth has since landed (FR-008) and this policy deliberately stayed as it
// is: RideForge authenticates with bearer tokens, which are not "credentials" in the CORS sense, so
// the conflict that forces a narrow origin list never arises. A hostile origin cannot read a
// signed-in rider's data, because nothing is sent automatically — no cookie, no Authorization
// header — without the rider's own token, which that origin does not have.
// Revisit only if cookie-based auth is ever introduced.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Route-stitching adapter (F-02). The provider is swappable via config; the default is the
// in-process fake so local/CI runs need no external API or key. Set RouteStitching__Provider=
// openrouteservice (+ RouteStitching__ApiKey) to use the real directions provider.
var stitchingOptions = builder.Configuration
    .GetSection(RouteStitchingOptions.SectionName)
    .Get<RouteStitchingOptions>() ?? new RouteStitchingOptions();

builder.Services.Configure<RouteStitchingOptions>(
    builder.Configuration.GetSection(RouteStitchingOptions.SectionName));

// Typed HttpClient for the ORS provider, with a per-call ceiling kept under the 30s NFR-01
// budget. Registered unconditionally (harmless when the fake provider is active).
builder.Services.AddHttpClient<OpenRouteServiceStitcher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(stitchingOptions.TimeoutSeconds);
});

// Both misconfiguration paths fail fast at startup rather than degrading silently at request time.
// An unrecognized provider name used to fall through to the fake, which answers 200 with a
// straight-line polygon at 1/DetourFactor of the requested distance and nothing in the body to say
// so — a wrong-output failure that is far harder to spot than a boot failure.
var resolvedProvider = stitchingOptions.Provider.ToLowerInvariant();
switch (resolvedProvider)
{
    case "openrouteservice":
        // Fail fast on a misconfigured deploy rather than 403->502-ing every request.
        if (string.IsNullOrWhiteSpace(stitchingOptions.ApiKey))
        {
            throw new InvalidOperationException(
                "RouteStitching:Provider is 'openrouteservice' but RouteStitching:ApiKey is not set " +
                "(supply it via the RouteStitching__ApiKey environment variable).");
        }
        builder.Services.AddTransient<IRouteStitcher>(
            sp => sp.GetRequiredService<OpenRouteServiceStitcher>());
        break;
    case "fake":
        builder.Services.AddSingleton<IRouteStitcher, FakeRouteStitcher>();
        break;
    default:
        throw new InvalidOperationException(
            $"RouteStitching:Provider is '{stitchingOptions.Provider}', which is not a known provider. " +
            "Use 'openrouteservice' or 'fake'.");
}

// Supabase auth (S-05). The API trusts nothing the client says about who it is: it verifies the
// rider's access token against the project's published public keys on every request.
var supabaseOptions = builder.Configuration
    .GetSection(SupabaseAuthOptions.SectionName)
    .Get<SupabaseAuthOptions>() ?? new SupabaseAuthOptions();

builder.Services.Configure<SupabaseAuthOptions>(
    builder.Configuration.GetSection(SupabaseAuthOptions.SectionName));

// Same fail-fast reasoning as the stitching provider above. A blank project URL would boot fine
// and then 401 every authenticated request with nothing in the logs pointing at config.
if (string.IsNullOrWhiteSpace(supabaseOptions.ProjectUrl))
{
    throw new InvalidOperationException(
        "Supabase:ProjectUrl is not set (supply it via the Supabase__ProjectUrl environment " +
        "variable, e.g. https://<project-ref>.supabase.co).");
}

// Key resolution goes straight at the JWKS document rather than through OIDC discovery: the JWKS
// URL is what Supabase documents, so this does not depend on the project also serving a
// .well-known/openid-configuration. ConfigurationManager caches the key set and refreshes it on
// its own schedule, so this costs one outbound call on cold start, not one per request.
//
// Note this endpoint serves an EMPTY key array while a project is still on legacy HS256 signing.
// If every token suddenly fails to validate, check that asymmetric signing keys are enabled in the
// Supabase dashboard before looking anywhere else. The fix is never to fall back to a shared
// secret — that is the failure mode this design exists to remove.
var jwksManager = new ConfigurationManager<JsonWebKeySet>(
    supabaseOptions.JwksUri,
    new JwksRetriever(),
    new HttpDocumentRetriever());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = supabaseOptions.Authority,
            ValidateAudience = true,
            ValidAudience = supabaseOptions.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            // Stated rather than inferred from whatever the JWKS happens to serve. Algorithm
            // confusion is not reachable today — IdentityModel refuses an HMAC algorithm against an
            // RSA/EC key — so this is hardening against a future key-set surprise, not a hole being
            // closed. Both algorithms Supabase issues asymmetric keys for are listed.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
            // IssuerSigningKeyResolver has no async overload, so the cached fetch is awaited
            // inline. After the first call this is a dictionary read, not I/O.
            IssuerSigningKeyResolver = (_, _, _, _) =>
                jwksManager.GetConfigurationAsync(CancellationToken.None)
                    .GetAwaiter().GetResult().GetSigningKeys(),
        };
    });

builder.Services.AddAuthorization();

// Persistence (S-06): saved routes in the Supabase project's Postgres, reached through the session
// pooler — Railway has no IPv6 route to the direct connection. Same fail-fast reasoning as above: a
// blank connection string would boot fine and 500 every save. Registering the context opens no
// connection; nothing touches the database until a request needs it, so generation never depends on
// it being reachable.
const string ConnectionStringName = "RideForge";

var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"ConnectionStrings:{ConnectionStringName} is not set (supply it via the " +
        $"ConnectionStrings__{ConnectionStringName} environment variable, in Npgsql key-value form: " +
        "Host=…;Port=5432;Database=postgres;Username=…;Password=…;SSL Mode=Require).");
}

// EF's own failure events are demoted below the default log level, because they log the raw
// exception: a connection failure reads "Failed to connect to <host>:<port>" and ConnectionError
// names the server outright. No connection detail may reach a log line, so the save endpoint logs
// its own sanitized warning instead (see LogDatabaseFailure). CommandError also fires on every
// repeat save — a double tap is a normal outcome there, not an error worth a stack trace.
// Program.cs only: `dotnet ef` builds its context through the design-time factory and keeps
// EF's full error output, which is exactly where a human debugging a migration wants it.
builder.Services.AddDbContext<RideForgeDbContext>(options => options
    .UseRideForgeDatabase(connectionString)
    .ConfigureWarnings(w => w.Log(
        (RelationalEventId.ConnectionError, LogLevel.Debug),
        (RelationalEventId.CommandError, LogLevel.Debug),
        (CoreEventId.SaveChangesFailed, LogLevel.Debug),
        (CoreEventId.QueryIterationFailed, LogLevel.Debug))));

// Migrations are applied by the Railway pre-deploy step (api/migrate.sh), never from here — a failed
// migration must stop the deploy while the previous version keeps serving, not crash-loop a running
// API. But that pre-deploy command lives in a Railway dashboard field this repo cannot see: clear it,
// or create a new service without it, and the app boots against an older schema where every save
// fails with an undifferentiated 503. This reports that case at startup. It never applies anything.
builder.Services.AddHostedService<PendingMigrationsCheck>();

// Anonymous generation quota (S-05). Each generation is a billed provider call, so the ceiling is
// enforced here rather than in the client, where it would be one devtools away from gone.

// Partition key for callers the quota does not apply to. A single shared key is correct here —
// GetNoLimiter counts nothing, so every signed-in rider sharing it costs one dictionary entry
// rather than one per rider.
const string AuthenticatedPartitionKey = "authenticated";

// Name of the rate-limiting policy applied to POST /route/generate.
const string GenerationQuotaPolicy = "generation-quota";

// Client-supplied install identifier; written by src/api/client.ts.
const string InstallHeaderName = "X-RideForge-Install";

var quotaOptions = builder.Configuration
    .GetSection(GenerationQuotaOptions.SectionName)
    .Get<GenerationQuotaOptions>() ?? new GenerationQuotaOptions();

builder.Services.Configure<GenerationQuotaOptions>(
    builder.Configuration.GetSection(GenerationQuotaOptions.SectionName));

// Railway terminates TLS at its edge and forwards, so Connection.RemoteIpAddress is the proxy's
// address on every request. Without this the IP fallback below partitions the entire internet onto
// one counter and the quota becomes global — two generations per hour for all riders combined.
//
// KnownIPNetworks/KnownProxies must be cleared because Railway's proxy is not on loopback and its
// address is not fixed; the default allow-list would drop the header unread. That does mean a
// caller can spoof X-Forwarded-For, which is consistent with what this quota is: a speed bump on
// honest riders, not a security control. The header is only consulted for callers that did not
// send a usable install id anyway.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.AddPolicy(GenerationQuotaPolicy, httpContext =>
    {
        // The whole point of the quota: signing in removes it. This is why the limiter has to run
        // AFTER UseAuthentication — before it, HttpContext.User is unpopulated, every caller looks
        // anonymous, and signed-in riders get limited too. That bug passes every test that does not
        // present a real token.
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return RateLimitPartition.GetNoLimiter(AuthenticatedPartitionKey);
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            AnonymousPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = quotaOptions.PermitLimit,
                Window = TimeSpan.FromMinutes(quotaOptions.WindowMinutes),
                // Queueing would hold a rider's request open until the window rolls — up to an hour.
                // Rejecting immediately lets the app show the sign-in prompt instead.
                QueueLimit = 0,
            });
    });
});

/// <summary>Body ceiling for a save — see the middleware that applies it.</summary>
const long SaveRequestSizeLimitBytes = 2_000_000;

// What a database failure is called, in the log line and in the body the rider sees. Split by
// direction: a read that fails has not failed to save anything, and a log line claiming otherwise
// sends whoever is diagnosing it to the wrong endpoint.
const string SaveOperation = "Saving a route";
const string ReadOperation = "Reading saved routes";
const string SaveFailedDetail = "The route could not be saved right now. Try again shortly.";
const string ReadFailedDetail = "Your saved routes could not be loaded right now. Try again shortly.";

var app = builder.Build();

// Railway injects PORT; ASP.NET Core does not pick it up automatically. When PORT is set
// (container/Railway) bind all interfaces so the platform's proxy can reach us; locally bind
// loopback only, which avoids the Windows firewall prompt that binding all interfaces triggers.
var portEnv = Environment.GetEnvironmentVariable("PORT");
var host = portEnv is null ? "localhost" : "0.0.0.0";
var port = portEnv ?? "8080";
app.Urls.Add($"http://{host}:{port}");

// Say which stitcher is actually serving traffic. A deploy that simply never sets
// RouteStitching__Provider falls back to the committed "fake" default and is otherwise
// indistinguishable from a working one until you measure the route length.
app.Logger.LogInformation("Route stitching provider resolved to '{Provider}'.", resolvedProvider);

// First in the pipeline, before anything reads the client IP. See the ForwardedHeadersOptions
// comment above for why the quota depends on this.
app.UseForwardedHeaders();

app.UseCors();

// A save carries the only large body this API accepts, and SavedRouteValidation's 20,000-point
// ceiling is reachable only after System.Text.Json has materialised the whole array. Left at
// Kestrel's 30 MB default, a signed-in caller could force hundreds of thousands of allocations per
// request, repeatedly and unthrottled, and take generation down with the container. A full-size
// geometry serializes to roughly 600 KB, so this refuses the abusive case at the transport layer
// while staying generous for anything real. Set here, before the body is read; the feature is
// absent on some hosts (and read-only once reading has begun), hence the guard.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.StartsWithSegments("/saved-routes"))
    {
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = SaveRequestSizeLimitBytes;
        }
    }

    await next(context);
});

// Bearer tokens, not cookies — so the AllowAnyOrigin policy above stays valid. An Authorization
// header is not a "credential" in the CORS sense, which is what AllowAnyOrigin conflicts with.
app.UseAuthentication();
app.UseAuthorization();

// After authentication, deliberately: the partition function reads HttpContext.User to decide
// whether the caller is exempt.
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "rideforge-api" }));

// The rider's identity as *this API* sees it, which is the point: it proves the token validated
// server-side rather than only being well-formed on the device. No token, or a token this service
// cannot verify, is a 401 from the middleware — the handler only ever runs for a valid one.
app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    id = SubjectOf(user),
    email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
})).RequireAuthorization();

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
    if (!Guid.TryParse(SubjectOf(user), out var ownerId))
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
            return DatabaseUnavailable(SaveFailedDetail);
        }
    }
    catch (Exception ex) when (IsDatabaseFailure(ex))
    {
        LogDatabaseFailure(logger, ex, SaveOperation);
        return DatabaseUnavailable(SaveFailedDetail);
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
    if (!Guid.TryParse(SubjectOf(user), out var ownerId))
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
    catch (Exception ex) when (IsDatabaseFailure(ex))
    {
        LogDatabaseFailure(logger, ex, ReadOperation);
        return DatabaseUnavailable(ReadFailedDetail);
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
    if (!Guid.TryParse(SubjectOf(user), out var ownerId))
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
    catch (Exception ex) when (IsDatabaseFailure(ex))
    {
        LogDatabaseFailure(logger, ex, ReadOperation);
        return DatabaseUnavailable(ReadFailedDetail);
    }
}).RequireAuthorization();

// Stitch an ordered waypoint list into a road-following route. Failure classes map to
// distinct HTTP statuses so the mobile client (which reads only the status code, not the
// body) can tell them apart: 400 bad input, 422 no route, 502 provider error, 504 timeout.
//
// Rate-limited under the same policy as /route/generate, and deliberately sharing one allowance
// rather than getting its own: this endpoint makes the identical billed provider call, and the
// cost argument that justifies the generation quota applies to it verbatim. No shipped client
// calls it, so a shared budget costs real riders nothing and leaves no unmetered path open.
app.MapPost("/route/stitch", async (StitchRequestDto dto, IRouteStitcher stitcher, CancellationToken ct) =>
{
    var error = RouteValidation.Validate(dto);
    if (error is not null)
    {
        return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var route = await stitcher.StitchAsync(new RouteRequest(dto.Waypoints!), ct);
        return Results.Ok(new StitchResponseDto(route.Geometry, route.DistanceMeters, route.DurationSeconds));
    }
    catch (RouteStitchException ex)
    {
        var status = ex.Kind switch
        {
            StitchFailure.NoRoute => StatusCodes.Status422UnprocessableEntity,
            StitchFailure.Timeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway,
        };
        return Results.Problem(detail: ex.Message, statusCode: status);
    }
}).RequireRateLimiting(GenerationQuotaPolicy);

// Generate a loop route from a start point + requested distance, then stitch it into a
// road-following route. RideForge's own (curviness-agnostic for S-01) waypoint generator feeds
// the same stitcher, so the success body matches /route/stitch and the failure→status mapping
// is identical: 400 bad input, 422 no route, 502 provider error, 504 timeout.
app.MapPost("/route/generate", async (GenerateRequestDto dto, IRouteStitcher stitcher, CancellationToken ct) =>
{
    var error = RouteValidation.Validate(dto);
    if (error is not null)
    {
        return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
    }

    var waypoints = RouteGenerator.GenerateLoop(dto.Start!, dto.DistanceKm!.Value);

    // Re-validate the generated geometry, not just the request: the generator projects on a plane, so
    // a start the input check accepts could still place a waypoint outside the legal domain. Catching
    // it here yields a 400 instead of an opaque provider 502.
    var geometryError = RouteValidation.Validate(new StitchRequestDto(waypoints));
    if (geometryError is not null)
    {
        return Results.Problem(
            detail: $"Generated route is not routable: {geometryError}",
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var route = await stitcher.StitchAsync(new RouteRequest(waypoints), ct);
        return Results.Ok(new StitchResponseDto(route.Geometry, route.DistanceMeters, route.DurationSeconds));
    }
    catch (RouteStitchException ex)
    {
        var status = ex.Kind switch
        {
            StitchFailure.NoRoute => StatusCodes.Status422UnprocessableEntity,
            StitchFailure.Timeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway,
        };
        return Results.Problem(detail: ex.Message, statusCode: status);
    }
}).RequireRateLimiting(GenerationQuotaPolicy);

app.Run();

/// <summary>
/// Which counter an anonymous caller is charged against: their install id when they sent a usable
/// one, their IP otherwise.
/// <para>
/// The GUID parse buys less than it looks like. It stops a <em>missing or malformed</em> header
/// from being a free pass — those callers fall through to the shared IP partition. It does not
/// stop a caller who sends a fresh, well-formed GUID on every request: each one lands on a brand
/// new partition with a full allowance, and never reaches the IP fallback at all. That is a
/// deliberate, accepted limit (the identifier is a speed bump, not a security control — see the
/// plan's Open Risks), not something this parse defends against.
/// </para>
/// <para>
/// The consequence worth remembering is memory, not just billing: every distinct key caches its
/// own limiter, and a fixed-window limiter that has spent a permit is not evicted until its window
/// replenishes. With a 60-minute window, live entries scale with request rate rather than with
/// anything an operator controls. Bounding this needs a partition the client cannot choose — an
/// IP-keyed limiter chained alongside — which is out of scope here and recorded in Open Risks.
/// </para>
/// <para>
/// The prefixes keep the two namespaces from colliding — an IP is not a GUID today, but the keys
/// share one dictionary and the cost of saying so is three characters.
/// </para>
/// </summary>
static string AnonymousPartitionKey(HttpContext context)
{
    if (context.Request.Headers.TryGetValue(InstallHeaderName, out var header)
        && Guid.TryParse(header.ToString(), out var installId))
    {
        return $"install:{installId}";
    }

    // A null RemoteIpAddress is possible (in-memory test transports, some socket configurations).
    // Everyone in that bucket shares one allowance, which is the conservative direction to err.
    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

/// <summary>
/// The rider's id as the verified token states it. Read under both names because inbound claim
/// mapping may or may not have renamed <c>sub</c> to <see cref="ClaimTypes.NameIdentifier"/>.
/// </summary>
static string? SubjectOf(ClaimsPrincipal user) =>
    user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

/// <summary>
/// A repeat save: the insert collided with the per-owner unique index. Matched by constraint name,
/// so any other unique violation is not mistaken for one and handed someone's existing row.
/// </summary>
static bool IsRepeatSave(DbUpdateException ex) =>
    ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: RideForgeDbContext.OwnerClientRouteIndex,
    };

/// <summary>
/// Anything the database layer threw. Npgsql's non-retrying execution strategy wraps transient
/// failures (a refused or dropped connection) in an <see cref="InvalidOperationException"/>, and
/// EF wraps command failures in a <see cref="DbUpdateException"/>, so the whole chain is searched.
/// </summary>
static bool IsDatabaseFailure(Exception ex) =>
    ExceptionChain(ex).Any(e => e is NpgsqlException or DbUpdateException);

static IEnumerable<Exception> ExceptionChain(Exception ex)
{
    for (Exception? e = ex; e is not null; e = e.InnerException)
    {
        yield return e;
    }
}

/// <summary>
/// Logs a database failure without the exception object. Npgsql's messages carry the host and port
/// ("Failed to connect to …"), and no connection detail may reach a log line. What is kept is enough
/// to diagnose: the exception types, the socket error, and — for a server-side refusal — the
/// SQLSTATE and the server's own message (a missing grant or RLS policy reads 42501 here).
/// </summary>
static void LogDatabaseFailure(ILogger logger, Exception ex, string operation)
{
    var chain = string.Join(" > ", ExceptionChain(ex).Select(e =>
        e is SocketException socket ? $"{nameof(SocketException)}({socket.SocketErrorCode})" : e.GetType().Name));
    var server = ExceptionChain(ex).OfType<PostgresException>().FirstOrDefault();
    var serverMessage = server is null ? "none"
        : MayLogServerMessage(server.SqlState) ? server.MessageText
        : "withheld";

    logger.LogWarning(
        "{Operation} failed in the database: {ExceptionChain}; SQLSTATE {SqlState}: {ServerMessage}",
        operation, chain, server?.SqlState ?? "none", serverMessage);
}

/// <summary>
/// Whether a server-side failure's own message may be logged. The allowed codes describe the
/// <em>statement</em> — a violated constraint, a missing grant or policy, a table an unapplied
/// migration has not created yet — and naming them is the whole point of logging at all. Every other
/// code echoes the connection identity back instead: 28P01 reads
/// <c>password authentication failed for user "rideforge_api.&lt;project-ref&gt;"</c> and 3D000 names
/// the database, so a password rotated in Supabase but not on Railway would write the pooler
/// username and the project ref into the deploy log on every save. For those, only the SQLSTATE is
/// kept — enough to look the cause up, with nothing of the connection in it.
/// </summary>
static bool MayLogServerMessage(string sqlState) => sqlState is
    PostgresErrorCodes.UniqueViolation or
    PostgresErrorCodes.CheckViolation or
    PostgresErrorCodes.NotNullViolation or
    PostgresErrorCodes.StringDataRightTruncation or
    PostgresErrorCodes.InsufficientPrivilege or
    PostgresErrorCodes.UndefinedTable or
    PostgresErrorCodes.UndefinedColumn;

/// <summary>
/// A generic 503: the body says nothing about where or what the database is. The <em>detail</em> is
/// the caller's, because "could not be saved" is nonsense on a read — the status mapping is what all
/// three endpoints share, not the copy.
/// </summary>
static IResult DatabaseUnavailable(string detail) =>
    Results.Problem(detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable);

/// <summary>
/// Reports once, at startup, whether the database is missing migrations this build expects — the
/// symptom of a pre-deploy step that did not run. Runs in the background so a slow or unreachable
/// database never delays the API coming up, and applies nothing: the deploy pipeline owns migrating.
/// </summary>
internal sealed class PendingMigrationsCheck(
    IServiceScopeFactory scopes, ILogger<PendingMigrationsCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RideForgeDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(stoppingToken)).ToArray();

            if (pending.Length > 0)
            {
                logger.LogError(
                    "The database is missing {Count} migration(s) this build expects: {Migrations}. " +
                    "The pre-deploy migration step did not run; saves will fail until it does.",
                    pending.Length, string.Join(", ", pending));
            }
        }
        catch (Exception ex)
        {
            // Never the exception itself: Npgsql's message names the host, and no connection detail
            // may reach a log line (see LogDatabaseFailure). An unreachable database is not this
            // check's problem to report — a save that needs it will say so itself.
            logger.LogWarning(
                "Could not check for pending migrations at startup ({Error}).", ex.GetType().Name);
        }
    }
}

/// <summary>
/// Exposed so the test project can boot the real pipeline with
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Top-level statements generate an internal
/// <c>Program</c> class; this makes it public without changing any behaviour.
/// </summary>
public partial class Program;
