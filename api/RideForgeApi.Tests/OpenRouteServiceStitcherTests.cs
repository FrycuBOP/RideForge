using System.Net;
using System.Text;
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
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var http = new HttpClient(new StubHandler(responder));
        var options = Options.Create(new RouteStitchingOptions
        {
            Provider = "openrouteservice",
            ApiKey = "test-key",
            BaseUrl = "https://example.test",
            Profile = "driving-car",
        });
        return new OpenRouteServiceStitcher(http, options);
    }

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

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
