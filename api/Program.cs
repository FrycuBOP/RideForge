using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

using RideForgeApi.Auth;
using RideForgeApi.Persistence;
using RideForgeApi.RateLimiting;
using RideForgeApi.Routing;

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

builder.Services.AddDbContext<RideForgeDbContext>(options => options.UseRideForgeDatabase(connectionString));

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
    id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
    email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
})).RequireAuthorization();

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
/// Exposed so the test project can boot the real pipeline with
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Top-level statements generate an internal
/// <c>Program</c> class; this makes it public without changing any behaviour.
/// </summary>
public partial class Program;
