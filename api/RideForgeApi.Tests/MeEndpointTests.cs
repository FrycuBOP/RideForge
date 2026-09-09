using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the trust boundary on <c>GET /me</c>: which tokens this API accepts and which it refuses.
/// <para>
/// This is the class of bug that fails silently and expensively. A validation parameter switched
/// off — issuer, lifetime, signature — leaves every happy-path test green while the API hands a
/// rider's identity to anyone who can forge a plausible token. So the cases here are mostly
/// negative, and each one removes a different guarantee.
/// </para>
/// <para>
/// Hermetic by construction: the signing key is generated in-process and the JWKS resolver is
/// removed, so no test reaches Supabase. That also means these tests cannot tell you the real
/// project is configured correctly — criteria 3.4/3.5 cover that against the deploy.
/// </para>
/// </summary>
public class MeEndpointTests : IClassFixture<MeEndpointTests.AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public MeEndpointTests(AuthTestFactory factory) => _factory = factory;

    /// <summary>
    /// Swaps the keys Supabase publishes for one generated here. Everything else about validation —
    /// issuer, audience, lifetime, signature — stays exactly as <c>Program.cs</c> configures it,
    /// which is the only reason these results say anything about production.
    /// </summary>
    public class AuthTestFactory : RideForgeApiFactory
    {
        private readonly RSA _signingRsa = RSA.Create(2048);
        private readonly RSA _impostorRsa = RSA.Create(2048);

        /// <summary>The key the API will trust, standing in for the project's published public key.</summary>
        public SecurityKey SigningKey => new RsaSecurityKey(_signingRsa) { KeyId = "rideforge-test" };

        /// <summary>A well-formed key the API has never heard of.</summary>
        public SecurityKey ImpostorKey => new RsaSecurityKey(_impostorRsa) { KeyId = "impostor" };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    // Belt and braces against a future edit to Program.cs quietly reintroducing a
                    // network fetch: with these cleared there is nothing left to fetch from.
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.ConfigurationManager = null;

                    options.TokenValidationParameters.IssuerSigningKeyResolver = null;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                });
            });
        }

        /// <summary>
        /// Mints a token the way Supabase would, so each test only has to say what is wrong with it.
        /// </summary>
        public string CreateToken(
            SecurityKey? key = null,
            string subject = "3f1c9c1e-6d5a-4a1b-9a2f-0f7d8e5b4c31",
            string email = "rider@example.com",
            DateTime? expires = null,
            string? issuer = null,
            string? audience = null)
        {
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer ?? Issuer,
                Audience = audience ?? Audience,
                Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("email", email)]),
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                Expires = expires ?? DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _signingRsa.Dispose();
                _impostorRsa.Dispose();
            }
        }
    }

    private HttpClient ClientWithToken(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await ClientWithToken(null).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MalformedToken_Returns401()
    {
        var response = await ClientWithToken("not-a-jwt.at.all").GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedWithAnUntrustedKey_Returns401()
    {
        // Structurally perfect — right issuer, right audience, unexpired. Only the signature is
        // wrong, which is the whole reason the API verifies one.
        var token = _factory.CreateToken(key: _factory.ImpostorKey);

        var response = await ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        // An hour past expiry, comfortably outside the default 5-minute clock-skew allowance.
        var token = _factory.CreateToken(expires: DateTime.UtcNow.AddHours(-1));

        var response = await ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenFromAnotherIssuer_Returns401()
    {
        // Signature alone is not the whole boundary. If ValidateIssuer is ever switched off, this
        // is the case that notices — and nothing else here would, because every other token in
        // this file carries the right issuer.
        var token = _factory.CreateToken(issuer: "https://someone-elses-project.supabase.co/auth/v1");

        var response = await ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenForAnotherAudience_Returns401()
    {
        // Supabase mints tokens for audiences other than a signed-in rider. Accepting any of them
        // on a rider endpoint widens the boundary silently; the same argument as the issuer case.
        var token = _factory.CreateToken(audience: "some-other-audience");

        var response = await ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_Returns200_WithTheRidersIdAndEmail()
    {
        const string subject = "9b2d4f60-1a3c-4e88-b7d5-2c6f0a9e4413";
        const string email = "kasia@example.com";
        var token = _factory.CreateToken(subject: subject, email: email);

        var response = await ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);
        Assert.Equal(subject, body.Id);
        Assert.Equal(email, body.Email);
    }

    [Fact]
    public async Task ValidTokenResponse_UsesTheCamelCaseWireContractTheClientConsumes()
    {
        var response = await ClientWithToken(_factory.CreateToken()).GetAsync("/me");
        var json = await response.Content.ReadAsStringAsync();

        // The mobile client reads these exact names (src/api/me.ts, phase 4).
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"email\"", json);
    }

    [Fact]
    public async Task AnonymousRouteGenerate_StillReturns200()
    {
        // Regression against over-broad auth. Adding authentication to the pipeline must not close
        // a door that was deliberately left open: US-01 says a rider generates a route without an
        // account, and a blanket RequireAuthorization or an authenticated fallback policy would
        // break that while every test above stayed green.
        var response = await ClientWithToken(null).PostAsJsonAsync(
            "/route/generate",
            new { start = new { lat = 50.0647, lng = 19.9450 }, distanceKm = 40.0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void BlankProjectUrl_FailsFastAtStartup()
    {
        // Deliberately a bare factory, not RideForgeApiFactory: the committed appsettings.json
        // ships an empty ProjectUrl, so this is what a deploy that never sets
        // Supabase__ProjectUrl actually does. Without the throw it would boot happily and then
        // 401 every signed-in rider, with nothing in the logs pointing at configuration —
        // and the JWKS fetch it would be attempting is against ".../auth/v1/.well-known/..." .
        using var factory = new WebApplicationFactory<Program>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Supabase:ProjectUrl", ex.Message);
    }

    private sealed record MeResponse(string Id, string? Email);
}
