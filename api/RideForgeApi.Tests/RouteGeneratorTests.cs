using RideForgeApi.Routing;

namespace RideForgeApi.Tests;

/// <summary>
/// Tests for the first-cut loop generator. Assertions are grounded in US-01 (the route departs
/// from and returns to the start; total length is within ±20% of the requested distance) and the
/// Risk #1 concern that a route must be a real loop, not a monotone out-and-back — never in the
/// generator's own internal formula. Length is measured with the independent <see cref="GeoMath"/>
/// helper against the deterministic straight-line geometry.
/// </summary>
public class RouteGeneratorTests
{
    private static readonly Coord Krakow = new(50.0647, 19.9450);

    /// <summary>
    /// Straight-line → on-road inflation measured against OpenRouteService across 20–60 km (see the
    /// <see cref="RouteGenerator.DetourFactor"/> docs; change.md records 19.06 km returned for a 20 km
    /// request). Deliberately duplicated here rather than read from the generator: these are the
    /// external observation this suite judges the generator's tuning against.
    /// </summary>
    private const double MinMeasuredRoadInflation = 1.45;
    private const double MaxMeasuredRoadInflation = 1.71;

    [Fact]
    public void GenerateLoop_StartsAndEndsAtTheStart()
    {
        var loop = RouteGenerator.GenerateLoop(Krakow, 40);

        Assert.Equal(Krakow, loop[0]);
        Assert.Equal(Krakow, loop[^1]);
    }

    [Fact]
    public void GenerateLoop_HasAtLeastThreeDistinctWaypoints()
    {
        var loop = RouteGenerator.GenerateLoop(Krakow, 40);

        var distinct = loop.Distinct().Count();
        Assert.True(distinct >= 3, $"Expected a real loop of >= 3 distinct waypoints, got {distinct}.");
    }

    [Fact]
    public void GenerateLoop_EnclosesArea_NotAnOutAndBack()
    {
        // An out-and-back keeps every point on ~one bearing (near-zero extent on one axis); a real
        // loop spreads waypoints in both dimensions. Assert meaningful north-south AND east-west span.
        var loop = RouteGenerator.GenerateLoop(Krakow, 40);

        var latSpan = loop.Max(c => c.Lat) - loop.Min(c => c.Lat);
        var lngSpan = loop.Max(c => c.Lng) - loop.Min(c => c.Lng);
        Assert.True(latSpan > 0.01, $"Loop has no north-south extent (span {latSpan}).");
        Assert.True(lngSpan > 0.01, $"Loop has no east-west extent (span {lngSpan}).");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(40)]
    [InlineData(120)]
    public void GenerateLoop_StraightLineLengthMatchesGeometricTarget(double distanceKm)
    {
        // The straight-line loop should match the generator's geometric sizing target
        // (distanceKm / DetourFactor). The ±20%-of-request goal is on *real roads* and is verified
        // via ORS (manual, plan 2.5). Measured with haversine, independent of the planar placement.
        var loop = RouteGenerator.GenerateLoop(Krakow, distanceKm);

        var lengthMeters = GeoMath.PathLengthMeters(loop);
        var targetMeters = distanceKm * 1000.0 / RouteGenerator.DetourFactor;
        // Tight band on purpose: the straight-line geometry is fully deterministic, so any slack here
        // is slack for a geometry bug. Swapping the circumradius for the apothem — the classic N-gon
        // error — shifts length by cos(pi/8) = 7.6%, which a ±20% band would have waved through.
        Assert.InRange(lengthMeters / targetMeters, 0.99, 1.01);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(40)]
    [InlineData(120)]
    public void GeometricSizing_InflatedByMeasuredRoadFactor_SatisfiesUs01Band(double distanceKm)
    {
        // Oracle: US-01 wants the ON-ROAD length within ±20% of the request. What a unit test can check
        // is that the geometric sizing is chosen so ANY inflation in the measured range lands inside
        // that band — if it doesn't, no real-road run can satisfy US-01 regardless of tuning. This
        // deliberately does NOT read RouteGenerator.DetourFactor: that constant is what this guards.
        // The test above pins the geometry to the generator's own target; this one pins that target to
        // the outside world, so DetourFactor can no longer be changed without a test noticing.
        var straightMeters = GeoMath.PathLengthMeters(RouteGenerator.GenerateLoop(Krakow, distanceKm));
        var requestedMeters = distanceKm * 1000.0;

        Assert.InRange(straightMeters * MinMeasuredRoadInflation / requestedMeters, 0.8, 1.2);
        Assert.InRange(straightMeters * MaxMeasuredRoadInflation / requestedMeters, 0.8, 1.2);
    }

    [Fact]
    public void GenerateLoop_LargerDistanceProducesLongerLoop()
    {
        var small = GeoMath.PathLengthMeters(RouteGenerator.GenerateLoop(Krakow, 20));
        var large = GeoMath.PathLengthMeters(RouteGenerator.GenerateLoop(Krakow, 40));

        Assert.True(large > small, "A larger requested distance must yield a longer loop.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void GenerateLoop_RejectsNonPositiveDistance(double distanceKm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteGenerator.GenerateLoop(Krakow, distanceKm));
    }
}

/// <summary>Tests for the extracted generate-request validation.</summary>
public class RouteGenerateValidationTests
{
    [Fact]
    public void NullRequest_IsInvalid() =>
        Assert.NotNull(RouteValidation.Validate((GenerateRequestDto?)null));

    [Fact]
    public void NullStart_IsInvalid() =>
        Assert.NotNull(RouteValidation.Validate(new GenerateRequestDto(null, 40)));

    [Fact]
    public void OutOfRangeStart_IsInvalid() =>
        Assert.NotNull(RouteValidation.Validate(new GenerateRequestDto(new Coord(950, 19.94), 40)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100000)]
    public void BadDistance_IsInvalid(double distanceKm) =>
        Assert.NotNull(RouteValidation.Validate(new GenerateRequestDto(new Coord(50.06, 19.94), distanceKm)));

    [Theory]
    [InlineData(89.541008)] // puts the loop's centre exactly on the pole at the 500 km ceiling
    [InlineData(86.0)]
    [InlineData(-86.0)]
    public void NearPolarStart_IsInvalid(double lat) =>
        Assert.NotNull(RouteValidation.Validate(new GenerateRequestDto(new Coord(lat, 19.94), 500)));

    [Fact]
    public void EveryGeneratedWaypoint_StaysInsideTheLegalDomain()
    {
        // Property check over the whole accepted input surface: the generator projects on a plane, so
        // the guard has to hold at the extremes of what RouteValidation lets through, not just Krakow.
        foreach (var lat in new[] { -85.0, -60.0, 0.0, 50.0647, 85.0 })
        {
            foreach (var lng in new[] { -180.0, -90.0, 0.0, 90.0, 180.0 })
            {
                var loop = RouteGenerator.GenerateLoop(new Coord(lat, lng), RouteValidation.MaxDistanceKm);

                Assert.All(loop, c =>
                {
                    Assert.InRange(c.Lat, -90.0, 90.0);
                    Assert.InRange(c.Lng, -180.0, 180.0);
                });
            }
        }
    }

    [Fact]
    public void StartAndPositiveInRangeDistance_IsValid() =>
        Assert.Null(RouteValidation.Validate(new GenerateRequestDto(new Coord(50.06, 19.94), 40)));
}
