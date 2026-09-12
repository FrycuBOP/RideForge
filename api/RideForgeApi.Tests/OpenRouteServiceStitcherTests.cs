using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RideForgeApi.Routing;

namespace RideForgeApi.Tests;

/// <summary>
/// Hermetic tests for the ORS provider: geometry decode (axis order + summary) and the
/// failure-class → RouteStitchException mapping. A stub HttpMessageHandler supplies canned
/// responses — no network, no key.
/// </summary>
public class OpenRouteServiceStitcherTests
{
    private static OpenRouteServiceStitcher BuildProvider(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder,
        int? snapRadiusMeters = null)
    {
        var http = new HttpClient(new StubHandler(responder));
        var settings = new RouteStitchingOptions
        {
            Provider = "openrouteservice",
            ApiKey = "test-key",
            BaseUrl = "https://example.test",
            Profile = "driving-car",
        };
        if (snapRadiusMeters is not null) settings.SnapRadiusMeters = snapRadiusMeters.Value;

        return new OpenRouteServiceStitcher(
            http, Options.Create(settings), NullLogger<OpenRouteServiceStitcher>.Instance);
    }

    /// <summary>Capture the outbound request body so the tests can assert on what ORS receives.</summary>
    private static OpenRouteServiceStitcher BuildCapturingProvider(
        string responseBody, out Func<string?> capturedBody, int? snapRadiusMeters = null)
    {
        string? sent = null;
        capturedBody = () => sent;
        return BuildProvider(
            (req, _) =>
            {
                sent = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.OK, responseBody);
            },
            snapRadiusMeters);
    }

    private const string MinimalRouteBody = """
    {
      "features": [{
        "geometry": { "type": "LineString", "coordinates": [[19.94, 50.06], [20.10, 50.15]] },
        "properties": { "summary": { "distance": 1.0, "duration": 1.0 } }
      }]
    }
    """;

    private static readonly RouteRequest SampleRequest =
        new(new[] { new Coord(50.06, 19.94), new Coord(50.15, 20.10) });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task ValidGeoJson_DecodesWithAxisSwap_AndReadsSummary()
    {
        // ORS emits [lng, lat]; the decode must yield Coord(lat, lng).
        const string body = """
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature",
            "geometry": { "type": "LineString", "coordinates": [[19.94, 50.06], [20.00, 50.10], [20.10, 50.15]] },
            "properties": { "summary": { "distance": 15200.5, "duration": 1100.0 } }
          }]
        }
        """;
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.OK, body));

        var route = await provider.StitchAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(3, route.Geometry.Count);
        // First point: input [19.94, 50.06] → Coord(Lat=50.06, Lng=19.94), not swapped.
        Assert.Equal(50.06, route.Geometry[0].Lat, 6);
        Assert.Equal(19.94, route.Geometry[0].Lng, 6);
        Assert.Equal(20.10, route.Geometry[2].Lng, 6);
        Assert.Equal(15200.5, route.DistanceMeters, 3);
        Assert.Equal(1100.0, route.DurationSeconds, 3);
    }

    [Fact]
    public async Task EmptyFeatures_MapsToNoRoute()
    {
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.OK, """{ "features": [] }"""));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.NoRoute, ex.Kind);
    }

    [Fact]
    public async Task NotFound_MapsToNoRoute()
    {
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.NotFound, """{ "error": { "code": 2010 } }"""));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.NoRoute, ex.Kind);
    }

    [Fact]
    public async Task Forbidden_MapsToProviderError()
    {
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.Forbidden, """{ "error": "invalid key" }"""));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task CancelledSend_MapsToTimeout()
    {
        var provider = BuildProvider((_, _) => throw new TaskCanceledException("simulated timeout"));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.Timeout, ex.Kind);
    }

    [Fact]
    public async Task MalformedJson_MapsToProviderError()
    {
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.OK, "this is not json"));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task WrongTypedCoordinate_MapsToProviderError()
    {
        // Valid JSON, but a coordinate value is a string — GetDouble would throw
        // InvalidOperationException, which must surface as ProviderError (not an unhandled 500).
        const string body = """
        {
          "features": [{
            "geometry": { "type": "LineString", "coordinates": [["oops", 50.06], [20.00, 50.10]] },
            "properties": { "summary": { "distance": 1.0, "duration": 1.0 } }
          }]
        }
        """;
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task ShortCoordinatePair_MapsToProviderError()
    {
        // A coordinate pair with a single element must not throw IndexOutOfRange → 500.
        const string body = """
        {
          "features": [{
            "geometry": { "type": "LineString", "coordinates": [[19.94], [20.00, 50.10]] },
            "properties": { "summary": { "distance": 1.0, "duration": 1.0 } }
          }]
        }
        """;
        var provider = BuildProvider((_, _) => Json(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => provider.StitchAsync(SampleRequest, CancellationToken.None));
        Assert.Equal(StitchFailure.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task SnapRadius_IsSentAsOneRadiusPerWaypoint()
    {
        // ORS's `radiuses` is a per-coordinate array; a length mismatch is rejected outright.
        var provider = BuildCapturingProvider(MinimalRouteBody, out var body, snapRadiusMeters: 1200);

        await provider.StitchAsync(SampleRequest, CancellationToken.None);

        using var doc = JsonDocument.Parse(body()!);
        var radiuses = doc.RootElement.GetProperty("radiuses");
        Assert.Equal(SampleRequest.Waypoints.Count, radiuses.GetArrayLength());
        Assert.All(radiuses.EnumerateArray(), r => Assert.Equal(1200.0, r.GetDouble(), 3));
    }

    [Fact]
    public async Task ZeroSnapRadius_OmitsRadiusesSoTheProviderDefaultApplies()
    {
        // The documented escape hatch: an ORS instance may cap the search radius below ours and
        // reject the parameter outright, which would fail every request. Zero must send no
        // `radiuses` key at all — not a zero-valued one, which would snap nothing.
        var provider = BuildCapturingProvider(MinimalRouteBody, out var body, snapRadiusMeters: 0);

        await provider.StitchAsync(SampleRequest, CancellationToken.None);

        using var doc = JsonDocument.Parse(body()!);
        Assert.False(doc.RootElement.TryGetProperty("radiuses", out _));
        Assert.True(doc.RootElement.TryGetProperty("coordinates", out _));
    }

    [Fact]
    public void DefaultSnapRadius_ExceedsTheProviderDefaultThatRejectedValidStarts()
    {
        // Regression guard for the reported bug: ORS searches 350 m by default, which is inside
        // Kraków's ~400-500 m pedestrian Old Town, so a rider starting on Rynek Główny was told
        // no route existed. Any default at or below 350 m silently reintroduces that failure.
        Assert.True(new RouteStitchingOptions().SnapRadiusMeters > 350);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
