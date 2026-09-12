using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;

using RideForgeApi.SavedRoutes;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the rules of <c>POST /saved-routes</c> that live in the database: who owns a row, what makes
/// a save a repeat, and whether the ride comes back the way it went in.
/// <para>
/// Every case here is constraint or storage behaviour, which is exactly what a stubbed context would
/// fake — so these run against a real Postgres, opt-in via <c>RIDEFORGE_TEST_DB</c> (see
/// <see cref="PostgresApiFactory"/>), and are reported as skipped without it.
/// </para>
/// <para>
/// The oracle is the save-route plan's decisions, not the endpoint's output: the owner is the token's
/// <c>sub</c>; uniqueness is per owner, so the same client route id from two riders is two rows; a
/// repeat returns the first row unchanged; the name follows the naming rule.
/// </para>
/// </summary>
public class SavedRoutesPersistenceTests : IClassFixture<PostgresApiFactory>
{
    private readonly PostgresApiFactory _factory;

    public SavedRoutesPersistenceTests(PostgresApiFactory factory) => _factory = factory;

    private HttpClient ClientFor(Guid rider)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateToken(subject: rider.ToString()));
        return client;
    }

    private static async Task<(HttpStatusCode Status, SavedRouteResponseDto Body)> Save(
        HttpClient client, JsonObject payload)
    {
        var response = await client.PostAsJsonAsync("/saved-routes", payload);

        // Assert the status before parsing. SavedRouteResponseDto is a positional record, so
        // System.Text.Json fills missing constructor parameters with defaults instead of throwing: a
        // 400 or 503 ProblemDetails body parses into Id = Guid.Empty with a null Name and sails past
        // Assert.NotNull, surfacing much later as a confusing row count or id comparison. Failing
        // here, with the body in the message, names the real problem.
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected a successful save but got {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());

        var body = await response.Content.ReadFromJsonAsync<SavedRouteResponseDto>();
        Assert.NotNull(body);
        return (response.StatusCode, body);
    }

    private Task<int> RowCount(Guid clientRouteId) =>
        _factory.QueryAsync(db => db.SavedRoutes.CountAsync(r => r.ClientRouteId == clientRouteId));

    [PostgresFact]
    public async Task FirstSave_Returns201_AndStoresTheRideForTheTokenRider()
    {
        var rider = _factory.NewRider();

        var (status, body) = await Save(ClientFor(rider), SavedRoutePayload.Valid());

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Loop from Kraków · 42 km", body.Name);

        var row = await _factory.QueryAsync(db => db.SavedRoutes.AsNoTracking().SingleAsync(r => r.Id == body.Id));
        Assert.Equal(rider, row.OwnerId);
        Assert.Equal("Kraków", row.StartLabel);
        Assert.Equal(40.0, row.RequestedDistanceKm);
        Assert.Equal(SavedRoutePayload.DistanceMeters, row.DistanceMeters);
    }

    [PostgresFact]
    public async Task Geometry_IsStoredInOrder_WithLatAndLngUnswapped()
    {
        var (_, body) = await Save(ClientFor(_factory.NewRider()), SavedRoutePayload.Valid());

        // Read the jsonb column as text, not through EF: an axis swap or renamed key made
        // consistently on write and read would round-trip through the mapping and look fine, while
        // anything else reading the column (the list slice, SQL, an export) would get it wrong.
        var stored = await _factory.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT geometry::text AS \"Value\" FROM rideforge.saved_routes WHERE id = {body.Id}")
            .SingleAsync());
        var points = JsonNode.Parse(stored)!.AsArray();

        Assert.Equal(SavedRoutePayload.Geometry.Length, points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            Assert.Equal(SavedRoutePayload.Geometry[i].Lat, points[i]!["lat"]!.GetValue<double>());
            Assert.Equal(SavedRoutePayload.Geometry[i].Lng, points[i]!["lng"]!.GetValue<double>());
        }
    }

    [PostgresFact]
    public async Task RepeatSave_BySameRider_Returns200_WithTheSameId_AndLeavesOneRow()
    {
        var rider = _factory.NewRider();
        var clientRouteId = Guid.NewGuid();

        var first = await Save(ClientFor(rider), SavedRoutePayload.Valid(clientRouteId));
        var repeat = await Save(ClientFor(rider), SavedRoutePayload.Valid(clientRouteId));

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, repeat.Status);
        Assert.Equal(first.Body.Id, repeat.Body.Id);
        Assert.Equal(1, await RowCount(clientRouteId));
    }

    [PostgresFact]
    public async Task SameClientRouteId_FromAnotherRider_IsANewRow_AndTheFirstRidersRowIsUntouched()
    {
        // The IDOR guard on the write path. With uniqueness on client_route_id alone, rider B's save
        // would take the repeat branch and be handed rider A's row.
        var riderA = _factory.NewRider();
        var riderB = _factory.NewRider();
        var clientRouteId = Guid.NewGuid();

        var saveA = await Save(ClientFor(riderA), SavedRoutePayload.Valid(clientRouteId));

        var payloadB = SavedRoutePayload.Valid(clientRouteId);
        payloadB["startLabel"] = "Zakopane";
        var saveB = await Save(ClientFor(riderB), payloadB);

        Assert.Equal(HttpStatusCode.Created, saveB.Status);
        Assert.NotEqual(saveA.Body.Id, saveB.Body.Id);
        Assert.Equal("Loop from Zakopane · 42 km", saveB.Body.Name);

        var rowA = await _factory.QueryAsync(db => db.SavedRoutes.AsNoTracking().SingleAsync(r => r.Id == saveA.Body.Id));
        Assert.Equal(riderA, rowA.OwnerId);
        Assert.Equal("Kraków", rowA.StartLabel);
        Assert.Equal(2, await RowCount(clientRouteId));
    }

    [PostgresFact]
    public async Task OwnerNamedInTheBody_IsIgnored_TheTokenRiderOwnsTheRow()
    {
        var rider = _factory.NewRider();
        var victim = _factory.NewRider();

        var payload = SavedRoutePayload.Valid();
        payload["ownerId"] = victim.ToString();
        var (status, body) = await Save(ClientFor(rider), payload);

        Assert.Equal(HttpStatusCode.Created, status);
        var owner = await _factory.QueryAsync(db =>
            db.SavedRoutes.Where(r => r.Id == body.Id).Select(r => r.OwnerId).SingleAsync());
        Assert.Equal(rider, owner);
        Assert.False(await _factory.QueryAsync(db => db.SavedRoutes.AnyAsync(r => r.OwnerId == victim)));
    }

    [PostgresFact]
    public async Task RepeatSave_WithADifferentPayload_ReturnsTheOriginalRow_Unchanged()
    {
        // First write wins. A repeat is a retry of the same save, so whatever it carries must not
        // overwrite what was stored.
        var rider = _factory.NewRider();
        var clientRouteId = Guid.NewGuid();

        var first = await Save(ClientFor(rider), SavedRoutePayload.Valid(clientRouteId));

        var changed = SavedRoutePayload.Valid(clientRouteId);
        changed["startLabel"] = "Zakopane";
        changed["distanceMeters"] = 88_000.0;
        changed["geometry"] = SavedRoutePayload.Points(3);
        var repeat = await Save(ClientFor(rider), changed);

        Assert.Equal(HttpStatusCode.OK, repeat.Status);
        Assert.Equal(first.Body, repeat.Body);
        Assert.Equal("Loop from Kraków · 42 km", repeat.Body.Name);

        var row = await _factory.QueryAsync(db => db.SavedRoutes.AsNoTracking().SingleAsync(r => r.Id == first.Body.Id));
        Assert.Equal("Kraków", row.StartLabel);
        Assert.Equal(SavedRoutePayload.DistanceMeters, row.DistanceMeters);
        Assert.Equal(SavedRoutePayload.Geometry.Length, row.Geometry.Count);
    }

    [PostgresFact]
    public async Task List_ReturnsOnlyTheCallersRoutes_NewestFirst()
    {
        // The cross-rider read against a real database. SavedRouteQueriesTests proves the expression
        // is right; this is the one that proves the endpoint composes it — with a real RLS policy
        // that scopes nothing (USING (true)) doing none of the work for it.
        var riderA = _factory.NewRider();
        var riderB = _factory.NewRider();

        var older = await Save(ClientFor(riderA), SavedRoutePayload.Valid());
        var newer = await Save(ClientFor(riderA), SavedRoutePayload.Valid());
        var theirs = await Save(ClientFor(riderB), SavedRoutePayload.Valid());

        var listed = await List(ClientFor(riderA));

        Assert.Equal([newer.Body.Id, older.Body.Id], listed.Items.Select(i => i.Id));
        Assert.DoesNotContain(listed.Items, i => i.Id == theirs.Body.Id);
        Assert.Equal(SavedRoutePayload.DistanceMeters, listed.Items[0].DistanceMeters);
    }

    [PostgresFact]
    public async Task Detail_ForAnotherRidersRoute_Returns404()
    {
        // 404 and not 403: a 403 would confirm the id names a real route, which turns the endpoint
        // into a probe for other riders' route ids.
        var riderA = _factory.NewRider();
        var riderB = _factory.NewRider();

        var theirs = await Save(ClientFor(riderB), SavedRoutePayload.Valid());

        var response = await ClientFor(riderA).GetAsync($"/saved-routes/{theirs.Body.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [PostgresFact]
    public async Task Detail_ReturnsTheRideAsItWasSaved_GeometryInOrder()
    {
        // What makes a saved route revisitable: the polyline comes back through the jsonb mapping in
        // the order and on the axes it went in. An axis swap here draws every revisited ride in the
        // wrong hemisphere.
        var rider = _factory.NewRider();
        var saved = await Save(ClientFor(rider), SavedRoutePayload.Valid());

        var response = await ClientFor(rider).GetAsync($"/saved-routes/{saved.Body.Id}");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected the saved route but got {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());

        var detail = await response.Content.ReadFromJsonAsync<SavedRouteDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(saved.Body.Id, detail.Id);
        Assert.Equal("Loop from Kraków · 42 km", detail.Name);
        Assert.Equal("Kraków", detail.StartLabel);
        Assert.Equal(40.0, detail.RequestedDistanceKm);
        Assert.Equal(SavedRoutePayload.Geometry.Length, detail.Geometry.Count);
        Assert.Equal(
            SavedRoutePayload.Geometry.Select(p => (p.Lat, p.Lng)),
            detail.Geometry.Select(c => (c.Lat, c.Lng)));
    }

    private static async Task<SavedRouteListResponseDto> List(HttpClient client)
    {
        var response = await client.GetAsync("/saved-routes");

        // Assert the status before parsing, for the same reason Save does: a positional record fills
        // missing constructor parameters with defaults, so a ProblemDetails body would parse into an
        // empty envelope and surface much later as a confusing row count.
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected the saved-routes list but got {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());

        var body = await response.Content.ReadFromJsonAsync<SavedRouteListResponseDto>();
        Assert.NotNull(body);
        return body;
    }
}
