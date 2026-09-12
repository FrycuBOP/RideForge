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
    private readonly ILogger<OpenRouteServiceStitcher> _logger;

    public OpenRouteServiceStitcher(
        HttpClient http,
        IOptions<RouteStitchingOptions> options,
        ILogger<OpenRouteServiceStitcher> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StitchedRoute> StitchAsync(RouteRequest request, CancellationToken ct)
    {
        // ORS expects coordinate pairs as [longitude, latitude].
        var coordinates = request.Waypoints.Select(w => new[] { w.Lng, w.Lat }).ToArray();
        var url = $"{_options.BaseUrl.TrimEnd('/')}/v2/directions/{_options.Profile}/geojson";

        // `radiuses` widens the provider's search for a routable road near each waypoint. ORS wants
        // one entry per coordinate; a single shared value is fine because the generated vertices
        // benefit from the same tolerance the start needs. Omitted entirely at 0 so the provider's
        // own default applies — see RouteStitchingOptions.SnapRadiusMeters.
        object payload = _options.SnapRadiusMeters > 0
            ? new
            {
                coordinates,
                radiuses = Enumerable
                    .Repeat((double)_options.SnapRadiusMeters, coordinates.Length)
                    .ToArray(),
            }
            : new { coordinates };

        HttpResponseMessage response;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                req.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey);
            }

            response = await _http.SendAsync(req, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller (mobile client) aborted — let the framework handle the
            // client-closed-request instead of fabricating a gateway-timeout response.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient.Timeout elapsed for the provider call (caller token not cancelled).
            throw new RouteStitchException(
                StitchFailure.Timeout, "The routing provider call timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RouteStitchException(
                StitchFailure.ProviderError, "The routing provider could not be reached.", ex);
        }

        // Dispose the response on every path (including the throws below) so the pooled
        // connection is released promptly instead of waiting for GC.
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Log the provider's own explanation before the response is discarded. ORS names
                // the offending waypoint and the radius it searched ("Could not find point 0:
                // 19.9373 50.0617 within a radius of 350.0 meters"), which is the one fact that
                // separates "this start isn't near a road" from "these points don't connect".
                // Without it, diagnosing a 422 means bisecting waypoints by hand from outside.
                // Logged, not returned: the client contract stays status-code-only, and provider
                // prose is operator diagnostics rather than rider-facing copy.
                _logger.LogWarning(
                    "Routing provider returned HTTP {Status} for {WaypointCount} waypoint(s) "
                        + "at snap radius {SnapRadiusMeters} m. Provider response: {ProviderBody}",
                    (int)response.StatusCode,
                    request.Waypoints.Count,
                    _options.SnapRadiusMeters,
                    await ReadErrorBodyAsync(response, ct));

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
    }

    /// <summary>
    /// Read an error response body for logging. Never throws and never propagates cancellation:
    /// a failure to read the explanation must not replace the real failure being reported. Capped
    /// because a misconfigured proxy can answer with a full HTML page.
    /// </summary>
    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int maxChars = 500;

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return "(empty)";
            return body.Length <= maxChars ? body : body[..maxChars] + "…(truncated)";
        }
        catch (Exception ex)
        {
            return $"(unreadable: {ex.GetType().Name})";
        }
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
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                {
                    throw new RouteStitchException(
                        StitchFailure.ProviderError, "The routing provider returned a malformed coordinate pair.");
                }

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
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Well-formed-but-wrong-typed JSON (e.g. a string where a coordinate number is
            // expected) throws InvalidOperationException/FormatException from the element
            // accessors — surface all of these as a provider error, not an unhandled 500.
            throw new RouteStitchException(
                StitchFailure.ProviderError, "The routing provider returned an unparseable response.", ex);
        }
    }
}
