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

app.Run();
