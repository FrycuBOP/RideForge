using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using RideForgeApi.Routing;

namespace RideForgeApi.Tests;

/// <summary>
/// Exercises <c>POST /route/generate</c> over the real request pipeline — routing, camelCase model
/// binding, validation, the generator, and the fake stitcher — rather than calling the pieces
/// directly. Plan criterion 2.3 was previously verified only by hand with curl; these tests make it
/// re-run on every build. No network: the committed <c>appsettings.json</c> selects the fake provider.
/// </summary>
public class RouteGenerateEndpointTests : IClassFixture<RideForgeApiFactory>
{
    private readonly RideForgeApiFactory _factory;

    public RouteGenerateEndpointTests(RideForgeApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ValidRequest_Returns200_WithClosedNonEmptyGeometry()
    {
        var client = _factory.CreateClientForFreshInstall();

        var response = await client.PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 50.0647, lng = 19.9450 }, distanceKm = 40.0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StitchResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Geometry);
        // US-01: the ride departs from and returns to the start.
        Assert.Equal(body.Geometry[0], body.Geometry[^1]);
        Assert.True(body.DistanceMeters > 0, "A generated loop must have a positive length.");
        Assert.True(body.DurationSeconds > 0, "A generated loop must have a positive duration.");
    }

    [Fact]
    public async Task Response_UsesTheCamelCaseWireContractTheClientConsumes()
    {
        var client = _factory.CreateClientForFreshInstall();

        var response = await client.PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 50.0647, lng = 19.9450 }, distanceKm = 40.0 });
        var json = await response.Content.ReadAsStringAsync();

        // The mobile client reads these exact names (src/api/route.ts); a serializer-policy change
        // would break the app while every direct-call unit test stayed green.
        Assert.Contains("\"geometry\"", json);
        Assert.Contains("\"distanceMeters\"", json);
        Assert.Contains("\"durationSeconds\"", json);
        Assert.Contains("\"lat\"", json);
        Assert.Contains("\"lng\"", json);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(100_000.0)]
    public async Task OutOfRangeDistance_Returns400(double distanceKm)
    {
        var client = _factory.CreateClientForFreshInstall();

        var response = await client.PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 50.0647, lng = 19.9450 }, distanceKm });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingStart_Returns400()
    {
        var client = _factory.CreateClientForFreshInstall();

        var response = await client.PostAsJsonAsync("/route/generate", new { distanceKm = 40.0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void UnknownProviderName_FailsFastAtStartup()
    {
        // A typo used to fall through to the fake stitcher and serve straight-line polygons at
        // 1/DetourFactor of the requested distance, with a 200 and no indication anything was wrong.
        // Built on the shared factory so the only thing wrong with this host is the provider name.
        // A bare factory would also trip the blank-Supabase:ProjectUrl throw, and the assertion
        // below would then be passing for whichever check happens to run first in Program.cs.
        using var factory = new RideForgeApiFactory()
            .WithWebHostBuilder(b => b.UseSetting("RouteStitching:Provider", "openrouteserivce"));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("not a known provider", ex.Message);
    }

    [Fact]
    public async Task OutOfRangeStart_Returns400()
    {
        var client = _factory.CreateClientForFreshInstall();

        var response = await client.PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 950.0, lng = 19.9450 }, distanceKm = 40.0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
