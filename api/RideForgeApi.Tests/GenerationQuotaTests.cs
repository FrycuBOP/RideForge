using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the anonymous generation quota on <c>POST /route/generate</c>: who gets counted, who does
/// not, and what a caller who sends nothing useful is charged against.
/// <para>
/// The bypass paths matter more than the happy path. A quota that counts correctly but exempts the
/// wrong caller — or waves through anyone who omits a header — is worse than no quota, because it
/// reads as protection while the billed provider call stays open.
/// </para>
/// <para>
/// The limiter's counters are process-wide and this class shares one host across its cases, so
/// every test that expects a fresh allowance mints its own install id. Reusing one would make the
/// results depend on execution order.
/// </para>
/// </summary>
public class GenerationQuotaTests : IClassFixture<GenerationQuotaTests.QuotaTestFactory>
{
    private readonly QuotaTestFactory _factory;

    public GenerationQuotaTests(QuotaTestFactory factory) => _factory = factory;

    /// <summary>
    /// Runs the real limiter at the committed limit, with the hermetic token setup inherited from
    /// <see cref="AuthenticatedApiFactory"/>.
    /// <para>
    /// Deliberately no <c>UseSetting</c> for the quota: this suite reads the COMMITTED
    /// configuration. Supplying "2" here would make every case below assert against a value the
    /// test itself provided, leaving the suite green if someone raised <c>appsettings.json</c> or
    /// the <c>GenerationQuotaOptions</c> default to 20 — which is precisely the regression it
    /// exists to catch.
    /// </para>
    /// </summary>
    public class QuotaTestFactory : AuthenticatedApiFactory;

    /// <summary>A generation request the endpoint accepts, so the only variable is the quota.</summary>
    private static Task<HttpResponseMessage> Generate(HttpClient client) =>
        client.PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 50.0647, lng = 19.9450 }, distanceKm = 40.0 });

    /// <summary>A stitch request the endpoint accepts, so the only variable is the quota.</summary>
    private static Task<HttpResponseMessage> Stitch(HttpClient client) =>
        client.PostAsJsonAsync(
            "/route/stitch",
            new
            {
                waypoints = new[]
                {
                    new { lat = 50.0647, lng = 19.9450 },
                    new { lat = 50.0700, lng = 19.9500 },
                },
            });

    /// <summary>
    /// A client identified as a specific install, or as none at all when
    /// <paramref name="installId"/> is null.
    /// </summary>
    private HttpClient ClientForInstall(string? installId)
    {
        var client = _factory.CreateClient();
        if (installId is not null)
        {
            client.DefaultRequestHeaders.Add(RideForgeApiFactory.InstallHeaderName, installId);
        }

        return client;
    }

    [Fact]
    public async Task AnonymousRider_GetsTwoGenerations_AndIsRefusedOnTheThird()
    {
        var client = ClientForInstall(Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, (await Generate(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Generate(client)).StatusCode);

        var third = await Generate(client);

        // 429 specifically, not a generic 503: the client maps this status to the "sign in to keep
        // riding" copy, and the limiter's own default rejection status is 503.
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task AnotherInstall_HasItsOwnAllowance()
    {
        var exhausted = ClientForInstall(Guid.NewGuid().ToString());
        await Generate(exhausted);
        await Generate(exhausted);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Generate(exhausted)).StatusCode);

        // A second rider on the same host must not inherit a spent allowance — that is the whole
        // difference between a per-rider quota and a global one.
        var fresh = ClientForInstall(Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, (await Generate(fresh)).StatusCode);
    }

    [Fact]
    public async Task SignedInRider_IsNotLimited_AfterTheAnonymousAllowanceIsSpent()
    {
        // Same install id throughout: the exemption has to come from the token, not from landing on
        // a different partition. Without this the test would still pass if authenticated callers
        // were merely counted separately instead of exempted.
        var installId = Guid.NewGuid().ToString();

        var anonymous = ClientForInstall(installId);
        await Generate(anonymous);
        await Generate(anonymous);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Generate(anonymous)).StatusCode);

        var signedIn = ClientForInstall(installId);
        signedIn.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateToken(subject: Guid.NewGuid().ToString()));

        // Three more, comfortably past the anonymous ceiling. One would not prove the absence of a
        // limit; it would only prove the limit is at least three.
        Assert.Equal(HttpStatusCode.OK, (await Generate(signedIn)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Generate(signedIn)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Generate(signedIn)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task CallerWithoutAUsableInstallId_IsStillLimited(string? installId)
    {
        // The failure this catches: partitioning on the raw header value, or skipping the limiter
        // when the header is absent, turns "send no header" into unlimited free generations. These
        // callers fall through to the shared IP partition instead — which is also why they share
        // one counter and this test cannot assume a fresh allowance.
        var client = ClientForInstall(installId);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await Generate(client);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return;
        }

        Assert.Fail(
            $"A caller sending install id '{installId ?? "(none)"}' was never limited within three " +
            "requests — the IP fallback is not charging them.");
    }

    [Fact]
    public async Task Health_StaysReachable_WhenTheAllowanceIsExhausted()
    {
        var client = ClientForInstall(Guid.NewGuid().ToString());

        await Generate(client);
        await Generate(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Generate(client)).StatusCode);

        // A stray global limiter, or a fallback policy applied to everything, would take /health
        // down with it — and the app's connectivity badge would read "offline" for a rider who is
        // merely capped. The two states must stay distinguishable.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task RouteStitch_SharesTheGenerationAllowance()
    {
        // /route/stitch makes the identical billed provider call, so it is limited under the same
        // policy AND the same partition — one budget across both endpoints, not one each.
        // Exhausting the allowance through /route/generate must therefore close /route/stitch too;
        // if it did not, the endpoint would be a free unmetered path to the same provider.
        var client = ClientForInstall(Guid.NewGuid().ToString());

        await Generate(client);
        await Generate(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Stitch(client)).StatusCode);
    }

    [Fact]
    public async Task RouteStitch_IsReachable_WhileAllowanceRemains()
    {
        // The other half of the pair: the endpoint still works, it is merely metered. Without this,
        // the test above would pass just as happily if /route/stitch were broken or removed.
        var client = ClientForInstall(Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, (await Stitch(client)).StatusCode);
    }
}
