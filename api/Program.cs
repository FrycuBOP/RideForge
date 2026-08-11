var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Railway injects PORT; ASP.NET Core does not pick it up automatically
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://+:{port}");

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "rideforge-api" }));

app.Run();
