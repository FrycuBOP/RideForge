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

switch (stitchingOptions.Provider.ToLowerInvariant())
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
    default:
        builder.Services.AddSingleton<IRouteStitcher, FakeRouteStitcher>();
        break;
}

var app = builder.Build();

// Railway injects PORT; ASP.NET Core does not pick it up automatically. When PORT is set
// (container/Railway) bind all interfaces so the platform's proxy can reach us; locally bind
// loopback only, which avoids the Windows firewall prompt that binding all interfaces triggers.
var portEnv = Environment.GetEnvironmentVariable("PORT");
var host = portEnv is null ? "localhost" : "0.0.0.0";
var port = portEnv ?? "8080";
app.Urls.Add($"http://{host}:{port}");

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "rideforge-api" }));

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

app.Run();
