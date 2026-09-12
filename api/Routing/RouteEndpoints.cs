using Microsoft.AspNetCore.RateLimiting;

using RideForgeApi.RateLimiting;

namespace RideForgeApi.Routing;

/// <summary>
/// The two route-building endpoints. They share a stitcher, a failure→status mapping and one
/// rate-limiting allowance, which is why they are declared side by side.
/// </summary>
public static class RouteEndpoints
{
    /// <summary>Maps the stitching and generation endpoints.</summary>
    public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
    {
        // Stitch an ordered waypoint list into a road-following route. Failure classes map to
        // distinct HTTP statuses so the mobile client (which reads only the status code, not the
        // body) can tell them apart: 400 bad input, 422 no route, 502 provider error, 504 timeout.
        //
        // Rate-limited under the same policy as /route/generate, and deliberately sharing one allowance
        // rather than getting its own: this endpoint makes the identical billed provider call, and the
        // cost argument that justifies the generation quota applies to it verbatim. No shipped client
        // calls it, so a shared budget costs real riders nothing and leaves no unmetered path open.
        app.MapPost("/route/stitch", async (StitchRequestDto dto, IRouteStitcher stitcher, CancellationToken ct) =>
        {
            var error = RouteValidation.Validate(dto);
            if (error is not null)
            {
                return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var route = await stitcher.StitchAsync(new RouteRequest(dto.Waypoints!), ct);
                return Results.Ok(new StitchResponseDto(route.Geometry, route.DistanceMeters, route.DurationSeconds));
            }
            catch (RouteStitchException ex)
            {
                return StitchFailureResult(ex);
            }
        }).RequireRateLimiting(GenerationQuotaOptions.PolicyName);

        // Generate a loop route from a start point + requested distance, then stitch it into a
        // road-following route. RideForge's own (curviness-agnostic for S-01) waypoint generator feeds
        // the same stitcher, so the success body matches /route/stitch and the failure→status mapping
        // is identical: 400 bad input, 422 no route, 502 provider error, 504 timeout.
        app.MapPost("/route/generate", async (GenerateRequestDto dto, IRouteStitcher stitcher, CancellationToken ct) =>
        {
            var error = RouteValidation.Validate(dto);
            if (error is not null)
            {
                return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
            }

            var waypoints = RouteGenerator.GenerateLoop(dto.Start!, dto.DistanceKm!.Value);

            // Re-validate the generated geometry, not just the request: the generator projects on a plane, so
            // a start the input check accepts could still place a waypoint outside the legal domain. Catching
            // it here yields a 400 instead of an opaque provider 502.
            var geometryError = RouteValidation.Validate(new StitchRequestDto(waypoints));
            if (geometryError is not null)
            {
                return Results.Problem(
                    detail: $"Generated route is not routable: {geometryError}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var route = await stitcher.StitchAsync(new RouteRequest(waypoints), ct);
                return Results.Ok(new StitchResponseDto(route.Geometry, route.DistanceMeters, route.DurationSeconds));
            }
            catch (RouteStitchException ex)
            {
                return StitchFailureResult(ex);
            }
        }).RequireRateLimiting(GenerationQuotaOptions.PolicyName);

        return app;
    }

    /// <summary>
    /// How a stitching failure reaches the client. One mapping for both endpoints deliberately: they
    /// make the same provider call, the client tells the cases apart by status code alone, and a
    /// divergence between the two would be a silent contract break rather than a visible one.
    /// </summary>
    private static IResult StitchFailureResult(RouteStitchException ex)
    {
        var status = ex.Kind switch
        {
            StitchFailure.NoRoute => StatusCodes.Status422UnprocessableEntity,
            StitchFailure.Timeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway,
        };
        return Results.Problem(detail: ex.Message, statusCode: status);
    }
}
