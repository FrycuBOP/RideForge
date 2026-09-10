using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

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
    /// Runs the real limiter at the committed limit of 2/hour, with the signing key swapped for a
    /// local one so an authenticated case can be exercised without reaching Supabase.
    /// </summary>
    public class QuotaTestFactory : RideForgeApiFactory
    {
        private readonly RSA _signingRsa = RSA.Create(2048);

        public SecurityKey SigningKey => new RsaSecurityKey(_signingRsa) { KeyId = "rideforge-quota-test" };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Assert against the shipped numbers rather than test-only ones: the value of this
            // suite is that it fails when someone changes the default from 2 without meaning to.
            builder.UseSetting("GenerationQuota:PermitLimit", "2");
            builder.UseSetting("GenerationQuota:WindowMinutes", "60");

            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.ConfigurationManager = null;

                    options.TokenValidationParameters.IssuerSigningKeyResolver = null;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                });
            });
        }

        public string CreateToken()
        {
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity(
                    [new Claim("sub", Guid.NewGuid().ToString()), new Claim("email", "rider@example.com")]),
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _signingRsa.Dispose();
        }
    }

    /// <summary>A generation request the endpoint accepts, so the only variable is the quota.</summary>
    private static Task<HttpResponseMessage> Generate(HttpClient client) =>
        client.PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 50.0647, lng = 19.9450 }, distanceKm = 40.0 });

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
            new AuthenticationHeaderValue("Bearer", _factory.CreateToken());

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
    public async Task UnlimitedEndpoints_AreNotAffectedByAnExhaustedAllowance()
    {
        var client = ClientForInstall(Guid.NewGuid().ToString());

        await Generate(client);
        await Generate(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Generate(client)).StatusCode);

        // The policy is attached to /route/generate alone. A stray global limiter, or a fallback
        // policy, would take these down with it and the app would look offline rather than capped.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var stitch = await client.PostAsJsonAsync(
            "/route/stitch",
            new
            {
                waypoints = new[]
                {
                    new { lat = 50.0647, lng = 19.9450 },
                    new { lat = 50.0700, lng = 19.9500 },
                },
            });
        Assert.Equal(HttpStatusCode.OK, stitch.StatusCode);
    }
}
