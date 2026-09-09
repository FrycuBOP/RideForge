using System.Security.Claims;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

using RideForgeApi.Auth;
using RideForgeApi.Routing;

var builder = WebApplication.CreateBuilder(args);

// The Expo web build is a browser app served from a different origin than this API, so
// cross-origin responses need CORS headers or the browser blocks the JS from reading them.
// MVP: allow any origin (public, unauthenticated read API). Scope this to the real web
// origin(s) once auth/cookies land (FR-008) — AllowAnyOrigin cannot be combined with credentials.
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
            // IssuerSigningKeyResolver has no async overload, so the cached fetch is awaited
            // inline. After the first call this is a dictionary read, not I/O.
            IssuerSigningKeyResolver = (_, _, _, _) =>
                jwksManager.GetConfigurationAsync(CancellationToken.None)
                    .GetAwaiter().GetResult().GetSigningKeys(),
        };
    });

builder.Services.AddAuthorization();

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

app.UseCors();

// Bearer tokens, not cookies — so the AllowAnyOrigin policy above stays valid. An Authorization
// header is not a "credential" in the CORS sense, which is what AllowAnyOrigin conflicts with.
app.UseAuthentication();
app.UseAuthorization();

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
});

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
});

app.Run();

/// <summary>
/// Exposed so the test project can boot the real pipeline with
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Top-level statements generate an internal
/// <c>Program</c> class; this makes it public without changing any behaviour.
/// </summary>
public partial class Program;
