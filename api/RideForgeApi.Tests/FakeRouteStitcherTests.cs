using RideForgeApi.Routing;

namespace RideForgeApi.Tests;

/// <summary>Tests for the fake provider's contract and the endpoint input validation.</summary>
public class FakeRouteStitcherTests
{
    private static readonly FakeRouteStitcher Fake = new();

    [Fact]
    public async Task EchoesInputGeometry_WithPositiveDistanceAndDuration()
    {
        var request = new RouteRequest(new[] { new Coord(50.06, 19.94), new Coord(50.15, 20.10) });

        var route = await Fake.StitchAsync(request, CancellationToken.None);

        Assert.Equal(request.Waypoints, route.Geometry);
        Assert.True(route.DistanceMeters > 0);
        Assert.True(route.DurationSeconds > 0);
    }

    [Fact]
    public async Task DistanceGrowsWhenWaypointsAreFartherApart()
    {
        var near = new RouteRequest(new[] { new Coord(50.06, 19.94), new Coord(50.07, 19.95) });
        var far = new RouteRequest(new[] { new Coord(50.06, 19.94), new Coord(50.60, 20.90) });

        var nearRoute = await Fake.StitchAsync(near, CancellationToken.None);
        var farRoute = await Fake.StitchAsync(far, CancellationToken.None);

        Assert.True(farRoute.DistanceMeters > nearRoute.DistanceMeters);
    }

    [Fact]
    public async Task FewerThanTwoWaypoints_ThrowsNoRoute()
    {
        var request = new RouteRequest(new[] { new Coord(50.06, 19.94) });

        var ex = await Assert.ThrowsAsync<RouteStitchException>(
            () => Fake.StitchAsync(request, CancellationToken.None));
        Assert.Equal(StitchFailure.NoRoute, ex.Kind);
    }
}

/// <summary>Tests for the extracted stitch-request validation.</summary>
public class RouteValidationTests
{
    [Fact]
    public void NullRequest_IsInvalid() =>
        Assert.NotNull(RouteValidation.Validate((StitchRequestDto?)null));

    [Fact]
    public void NullWaypoints_IsInvalid() =>
        Assert.NotNull(RouteValidation.Validate(new StitchRequestDto(null)));

    [Fact]
    public void SingleWaypoint_IsInvalid() =>
        Assert.NotNull(RouteValidation.Validate(new StitchRequestDto(new[] { new Coord(50.06, 19.94) })));

    [Fact]
    public void OutOfRangeCoordinate_IsInvalid()
    {
        var dto = new StitchRequestDto(new[] { new Coord(950, 19.94), new Coord(50.15, 20.10) });
        Assert.NotNull(RouteValidation.Validate(dto));
    }

    [Fact]
    public void TwoInRangeWaypoints_IsValid()
    {
        var dto = new StitchRequestDto(new[] { new Coord(50.06, 19.94), new Coord(50.15, 20.10) });
        Assert.Null(RouteValidation.Validate(dto));
    }
}
