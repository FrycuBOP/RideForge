using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RideForgeApi.Routing;
using RideForgeApi.SavedRoutes;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the boundary of the two saved-route reads that needs no database: who may read (FR-010:
/// signed-in riders only), what a malformed id answers, what an unreachable database looks like from
/// outside, and the exact field names the app will parse.
/// <para>
/// Same host trick as <see cref="SavedRoutesSaveEndpointTests"/> — the database port refuses connections
/// — so every case here is meaningful in both directions: a request the boundary should refuse comes
/// back 401 or 404, and one that slips past reaches the database and comes back 503. It cannot pass
/// by accident.
/// </para>
/// <para>
/// Which rows come back is <em>not</em> here; that is <see cref="SavedRouteQueriesTests"/>, which
/// executes the real filter without a database, and <see cref="SavedRoutesPersistenceTests"/> for the
/// day a real one is configured.
/// </para>
/// </summary>
public class SavedRoutesReadEndpointTests : IClassFixture<UnreachableDatabaseFactory>
{
    private readonly UnreachableDatabaseFactory _factory;

    public SavedRoutesReadEndpointTests(UnreachableDatabaseFactory factory) =>
        _factory = factory;

    /// <summary>Both reads, so every boundary case is asserted against each of them.</summary>
    public static TheoryData<string> BothEndpoints =>
    [
        "/saved-routes",
        "/saved-routes/3f1c9c1e-6d5a-4a1b-9a2f-0f7d8e5b4c31",
    ];

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [MemberData(nameof(BothEndpoints))]
    public async Task NoToken_Returns401(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BothEndpoints))]
    public async Task ExpiredToken_Returns401(string path)
    {
        // An hour past expiry, comfortably outside the default 5-minute clock-skew allowance.
        var token = _factory.CreateToken(expires: DateTime.UtcNow.AddHours(-1));

        var response = await ClientWithToken(token).GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BothEndpoints))]
    public async Task TokenFromAnotherIssuer_Returns401(string path)
    {
        // A correctly signed token from a different Supabase project names a rider this API has
        // never heard of. Accepting it would hand that stranger a rider id namespace of their own
        // choosing — on a read whose only ownership guard is that id.
        var token = _factory.CreateToken(issuer: "https://someone-elses-project.supabase.co/auth/v1");

        var response = await ClientWithToken(token).GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BothEndpoints))]
    public async Task TokenForAnotherAudience_Returns401(string path)
    {
        var token = _factory.CreateToken(audience: "some-other-audience");

        var response = await ClientWithToken(token).GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BothEndpoints))]
    public async Task ValidlySignedTokenWhoseSubjectIsNotARiderId_Returns401(string path)
    {
        // The owner column is a UUID and Supabase subjects always are. A token naming someone in
        // some other shape identifies no rider whose routes this API could scope a read to — and the
        // one thing it must never do is answer with an unscoped read.
        var response = await ClientWithToken(_factory.CreateToken(subject: "not-a-uuid")).GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MalformedRouteId_Returns404_BeforeTheHandlerRuns()
    {
        // The :guid route constraint. 503 here would mean a malformed id reached the database.
        var response = await ClientWithToken(_factory.CreateToken()).GetAsync("/saved-routes/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BothEndpoints))]
    public async Task UnreachableDatabase_Returns503_WithoutConnectionDetailsInTheBodyOrTheLogs(string path)
    {
        var response = await ClientWithToken(_factory.CreateToken()).GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(UnreachableDatabaseFactory.DatabaseHost, body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UnreachableDatabaseFactory.DatabasePassword, body);

        // The API did log the failure — otherwise the checks below would pass on an empty sink. The
        // read says so in its own words; a line claiming a save failed would send whoever is
        // diagnosing this to the wrong endpoint.
        Assert.Contains(_factory.Logs, line => line.Contains("Reading saved routes failed"));

        Assert.DoesNotContain(
            _factory.Logs, line => line.Contains(UnreachableDatabaseFactory.DatabaseHost));
        Assert.DoesNotContain(
            _factory.Logs, line => line.Contains(UnreachableDatabaseFactory.DatabasePassword));
    }

    [Fact]
    public void ListWireContract_IsTheOneTheClientConsumes()
    {
        // A successful read needs a database, so its shape is asserted by SavedRoutesPersistenceTests
        // — which skip unless RIDEFORGE_TEST_DB is set, and nothing sets it. That leaves the *shape*
        // of a 200 covered by nothing that runs: a renamed property or a naming policy would keep
        // this suite green while the client's response guard (src/api/saved-routes.ts) turned every
        // successful list into "unexpected response". Serializing through the host's own configured
        // options is the half of that contract a test can hold without Postgres.
        var summary = new SavedRouteSummaryDto(
            Guid.NewGuid(), "Loop from Kraków · 42 km", 41_600, 3_120, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(new SavedRouteListResponseDto([summary]), HostJsonOptions());

        Assert.Contains("\"items\"", json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"distanceMeters\"", json);
        Assert.Contains("\"durationSeconds\"", json);
        Assert.Contains("\"createdAt\"", json);

        // The envelope's whole purpose is that a cursor can join it later without breaking the
        // client's parse guard. A bare array would make that a breaking change; this is what
        // notices if someone unwraps it.
        Assert.StartsWith("{", json);

        // Not a field the summary has, and the reason it does not: a list carrying geometry is
        // ~600 KB a row.
        Assert.DoesNotContain("geometry", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailWireContract_IsTheOneTheClientConsumes()
    {
        var detail = new SavedRouteDetailDto(
            Guid.NewGuid(),
            "Loop from Kraków · 42 km",
            new Coord(50.0647, 19.9450),
            "Kraków",
            40,
            41_600,
            3_120,
            [new Coord(50.0647, 19.9450), new Coord(50.0712, 19.9611)],
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(detail, HostJsonOptions());

        Assert.Contains("\"id\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"start\"", json);
        Assert.Contains("\"startLabel\"", json);
        Assert.Contains("\"requestedDistanceKm\"", json);
        Assert.Contains("\"distanceMeters\"", json);
        Assert.Contains("\"durationSeconds\"", json);
        Assert.Contains("\"geometry\"", json);
        Assert.Contains("\"createdAt\"", json);

        // The axes the map draws with. A swap or a rename here puts every revisited ride in the
        // wrong hemisphere, and nothing on the client would flag it as a parse failure.
        Assert.Contains("\"lat\"", json);
        Assert.Contains("\"lng\"", json);
    }

    private JsonSerializerOptions HostJsonOptions() =>
        _factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
}
