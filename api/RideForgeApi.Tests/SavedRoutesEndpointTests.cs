using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RideForgeApi.SavedRoutes;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the boundary of <c>POST /saved-routes</c> that needs no database: who may save (FR-009:
/// signed-in riders only), what is refused as malformed, and what an unreachable database looks like
/// from outside.
/// <para>
/// The host points at a port nothing listens on. That makes every case here meaningful in both
/// directions: a request validation should refuse comes back 400, and one that slips past validation
/// reaches the database and comes back 503 — it cannot pass by accident. The rules a real database
/// enforces (ownership, per-owner uniqueness, round-trips) live in
/// <see cref="SavedRoutesPersistenceTests"/>, because a stub would lie about every one of them.
/// </para>
/// </summary>
public class SavedRoutesEndpointTests : IClassFixture<SavedRoutesEndpointTests.UnreachableDatabaseFactory>
{
    private readonly UnreachableDatabaseFactory _factory;

    public SavedRoutesEndpointTests(UnreachableDatabaseFactory factory) => _factory = factory;

    /// <summary>
    /// A signed-in host whose database refuses connections on the spot (port 1 on loopback), with a
    /// log sink so a test can see what the API wrote about the failure.
    /// </summary>
    public class UnreachableDatabaseFactory : AuthenticatedApiFactory
    {
        public const string DatabaseHost = "127.0.0.1";

        /// <summary>Not a credential for anything; distinctive so a leak is unmistakable.</summary>
        public const string DatabasePassword = "leak-canary-5b1f";

        public ConcurrentQueue<string> Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseSetting(
                "ConnectionStrings:RideForge",
                $"Host={DatabaseHost};Port=1;Database=rideforge;Username=rideforge_api;Password={DatabasePassword}");

            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(Logs)));
        }
    }

    private HttpClient SignedInClient(string? subject = null)
    {
        var client = _factory.CreateClient();
        var token = subject is null ? _factory.CreateToken() : _factory.CreateToken(subject: subject);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> Save(HttpClient client, JsonObject payload) =>
        client.PostAsJsonAsync("/saved-routes", payload);

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await Save(_factory.CreateClient(), SavedRoutePayload.Valid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidlySignedTokenWhoseSubjectIsNotARiderId_Returns401()
    {
        // The owner column is a UUID and Supabase subjects always are. A token that names someone
        // else in some other shape identifies no rider this API can store a route for.
        var response = await Save(SignedInClient(subject: "not-a-uuid"), SavedRoutePayload.Valid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>One way to break a valid payload per validation rule; the key is the test case name.</summary>
    private static readonly Dictionary<string, Action<JsonObject>> Breakers = new()
    {
        ["clientRouteId missing"] = p => p.Remove("clientRouteId"),
        ["clientRouteId empty"] = p => p["clientRouteId"] = Guid.Empty.ToString(),
        ["start missing"] = p => p.Remove("start"),
        ["start latitude out of range"] = p => p["start"] = SavedRoutePayload.Point(90.5, 19.945),
        ["start longitude out of range"] = p => p["start"] = SavedRoutePayload.Point(50.0647, -180.5),
        ["requested distance zero"] = p => p["requestedDistanceKm"] = 0.0,
        ["requested distance over 500 km"] = p => p["requestedDistanceKm"] = 500.1,
        ["geometry missing"] = p => p.Remove("geometry"),
        ["geometry of one point"] = p => p["geometry"] = SavedRoutePayload.Points(1),
        ["geometry over 20,000 points"] = p =>
            p["geometry"] = SavedRoutePayload.Points(SavedRouteValidation.MaxGeometryPoints + 1),
        ["geometry point out of range"] = p =>
            p["geometry"]!.AsArray()[2] = SavedRoutePayload.Point(50.08, 181.0),
        ["geometry point null"] = p => p["geometry"]!.AsArray().Insert(1, null),
        ["distanceMeters zero"] = p => p["distanceMeters"] = 0.0,
        ["distanceMeters absurd"] = p => p["distanceMeters"] = 1e300,
        ["durationSeconds negative"] = p => p["durationSeconds"] = -1.0,
        ["durationSeconds missing"] = p => p.Remove("durationSeconds"),
        ["startLabel over 200 characters"] = p =>
            p["startLabel"] = new string('a', SavedRouteValidation.MaxStartLabelLength + 1),
    };

    public static TheoryData<string> InvalidPayloads => [.. Breakers.Keys];

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task InvalidPayload_Returns400(string rule)
    {
        var payload = SavedRoutePayload.Valid();
        Breakers[rule](payload);

        var response = await Save(SignedInClient(), payload);

        // 503 here would mean the payload got past validation and on to the database.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PayloadAtEveryInclusiveBound_PassesValidation()
    {
        // Each limit is inclusive: exactly 20,000 points, a 200-character label, a 500 km request,
        // points on the poles and the antimeridian. Refusing any of them is an off-by-one that the
        // over-the-limit rows above cannot see. Passing validation means reaching the (unreachable)
        // database, hence 503 rather than 201.
        var payload = SavedRoutePayload.Valid();
        payload["geometry"] = SavedRoutePayload.Points(SavedRouteValidation.MaxGeometryPoints, lat: 90, lng: 180);
        payload["start"] = SavedRoutePayload.Point(-90, -180);
        payload["startLabel"] = new string('a', SavedRouteValidation.MaxStartLabelLength);
        payload["requestedDistanceKm"] = 500.0;
        payload["distanceMeters"] = SavedRouteValidation.MaxDistanceMeters;

        var response = await Save(SignedInClient(), payload);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UnreachableDatabase_Returns503_WithoutConnectionDetailsInTheBodyOrTheLogs()
    {
        var response = await Save(SignedInClient(), SavedRoutePayload.Valid());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(UnreachableDatabaseFactory.DatabaseHost, body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UnreachableDatabaseFactory.DatabasePassword, body);

        // The API did log the failure — otherwise the checks below would pass on an empty sink.
        Assert.Contains(_factory.Logs, line => line.Contains("Saving a route failed"));

        // Npgsql's own message is "Failed to connect to 127.0.0.1:1"; EF logs it verbatim unless
        // told not to. The sink collects every host log line at the configured level, exceptions
        // included, across every test in this class.
        Assert.DoesNotContain(_factory.Logs, line => line.Contains(UnreachableDatabaseFactory.DatabaseHost));
        Assert.DoesNotContain(_factory.Logs, line => line.Contains(UnreachableDatabaseFactory.DatabasePassword));
    }

    [Fact]
    public void SuccessWireContract_IsTheOneTheClientConsumes()
    {
        // A successful save needs a database, so its shape is asserted by SavedRoutesPersistenceTests
        // — which skip unless RIDEFORGE_TEST_DB is set, and nothing sets it. That leaves the *shape*
        // of a 201/200 covered by nothing that runs: a renamed property or a naming policy would keep
        // this suite green while the client's response guard (src/api/saved-routes.ts) turned every
        // successful save into "unexpected response". Serializing through the host's own configured
        // options is the half of that contract a test can hold without Postgres.
        var options = _factory.Services
            .GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        var json = JsonSerializer.Serialize(
            new SavedRouteResponseDto(Guid.NewGuid(), "Loop from Kraków · 42 km", DateTimeOffset.UtcNow),
            options);

        Assert.Contains("\"id\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"createdAt\"", json);

        // The other direction: these are the names the client sends.
        var request = JsonSerializer.Deserialize<SaveRouteRequestDto>(
            SavedRoutePayload.Valid().ToJsonString(), options);

        Assert.NotNull(request);
        Assert.NotNull(request.ClientRouteId);
        Assert.NotNull(request.Start);
        Assert.NotNull(request.StartLabel);
        Assert.NotNull(request.RequestedDistanceKm);
        Assert.NotNull(request.Geometry);
        Assert.NotNull(request.DistanceMeters);
        Assert.NotNull(request.DurationSeconds);
    }

    /// <summary>Collects each log line, with its exception rendered in full, as the host emits it.</summary>
    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                sink.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
