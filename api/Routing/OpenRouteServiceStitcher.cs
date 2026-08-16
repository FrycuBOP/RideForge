using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RideForgeApi.Routing;

/// <summary>
/// Real stitcher backed by the OpenRouteService directions API (pattern A: directions-with-
/// waypoints). Sends the whole waypoint list in one call, decodes the returned GeoJSON into
/// our provider-agnostic <see cref="StitchedRoute"/>, and translates transport/HTTP outcomes
/// into <see cref="RouteStitchException"/> so the endpoint can pick a status code.
/// All ORS vocabulary (the [lng,lat] axis order, the GeoJSON shape) stays inside this class.
/// </summary>
public sealed class OpenRouteServiceStitcher : IRouteStitcher
{
    private readonly HttpClient _http;
    private readonly RouteStitchingOptions _options;

    public OpenRouteServiceStitcher(HttpClient http, IOptions<RouteStitchingOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<StitchedRoute> StitchAsync(RouteRequest request, CancellationToken ct)
    {
        // ORS expects coordinate pairs as [longitude, latitude].
        var coordinates = request.Waypoints.Select(w => new[] { w.Lng, w.Lat }).ToArray();
        var url = $"{_options.BaseUrl.TrimEnd('/')}/v2/directions/{_options.Profile}/geojson";

        HttpResponseMessage response;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { coordinates }),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                req.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey);
            }

            response = await _http.SendAsync(req, ct);
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient.Timeout or a caller cancel both land here.
            throw new RouteStitchException(
                StitchFailure.Timeout, "The routing provider call timed out or was cancelled.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RouteStitchException(
                StitchFailure.ProviderError, "The routing provider could not be reached.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // ORS returns 404 when the waypoints can't be connected into a route.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RouteStitchException(
                    StitchFailure.NoRoute, "The provider could not connect the given waypoints into a route.");
            }

            throw new RouteStitchException(
                StitchFailure.ProviderError, $"The routing provider returned HTTP {(int)response.StatusCode}.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseGeoJson(json);
    }

    /// <summary>
    /// Decode an ORS directions GeoJSON FeatureCollection into a <see cref="StitchedRoute"/>.
    /// Swaps the [lng,lat] axis order into <c>Coord(lat, lng)</c>. Internal + static so it is
    /// unit-testable directly against canned provider responses.
    /// </summary>
    /// <exception cref="RouteStitchException">
    /// <see cref="StitchFailure.NoRoute"/> when the response carries no route geometry;
    /// <see cref="StitchFailure.ProviderError"/> when the JSON is malformed/unexpected.
    /// </exception>
    internal static StitchedRoute ParseGeoJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array
                || features.GetArrayLength() == 0)
            {
                throw new RouteStitchException(
                    StitchFailure.NoRoute, "The provider returned no route for the given waypoints.");
            }

            var feature = features[0];

            if (!feature.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("coordinates", out var coords)
                || coords.ValueKind != JsonValueKind.Array
                || coords.GetArrayLength() == 0)
            {
                throw new RouteStitchException(
                    StitchFailure.NoRoute, "The provider returned an empty route geometry.");
            }

            var points = new List<Coord>(coords.GetArrayLength());
            foreach (var pair in coords.EnumerateArray())
            {
                // GeoJSON is [lng, lat]; swap into Coord(Lat, Lng).
                var lng = pair[0].GetDouble();
                var lat = pair[1].GetDouble();
                points.Add(new Coord(lat, lng));
            }

            double distance = 0, duration = 0;
            if (feature.TryGetProperty("properties", out var props)
                && props.TryGetProperty("summary", out var summary))
            {
                if (summary.TryGetProperty("distance", out var d)) distance = d.GetDouble();
                if (summary.TryGetProperty("duration", out var t)) duration = t.GetDouble();
            }

            return new StitchedRoute(points, distance, duration);
        }
        catch (JsonException ex)
        {
            throw new RouteStitchException(
                StitchFailure.ProviderError, "The routing provider returned an unparseable response.", ex);
        }
    }
}
