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
/// Boots the real pipeline with the configuration every test needs just to reach a running host.
/// <para>
/// <c>Program.cs</c> throws at startup on a blank <c>Supabase:ProjectUrl</c> — deliberately, so a
/// misconfigured deploy fails loudly instead of 401-ing every request with nothing in the logs.
/// That check does not care which endpoint a test is aiming at, so every test class needs a value
/// here or it never gets a client. This is that value, in one place.
/// </para>
/// <para>
/// The URL is a syntactically valid placeholder and is never dialled: tests that exercise
/// authentication replace the signing-key resolution with a local key (see
/// <c>MeEndpointTests</c>), and tests that don't never present a token.
/// </para>
/// </summary>
public class RideForgeApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Well-formed but unroutable — a resolvable host here would invite a real fetch.</summary>
    public const string ProjectUrl = "https://rideforge-tests.invalid";

    /// <summary>The <c>iss</c> claim Supabase would stamp for <see cref="ProjectUrl"/>.</summary>
    public const string Issuer = $"{ProjectUrl}/auth/v1";

    /// <summary>The <c>aud</c> claim Supabase stamps on a signed-in rider's access token.</summary>
    public const string Audience = "authenticated";

    /// <summary>
    /// <c>Program.cs</c> refuses to boot without a connection string, for the same reason it refuses
    /// a blank project URL. Like <see cref="ProjectUrl"/>, this one is never dialled: registering the
    /// context opens no connection, and a hermetic test that accidentally reaches the database fails
    /// loudly on an unresolvable host rather than silently touching a real one.
    /// </summary>
    public const string ConnectionString =
        "Host=rideforge-tests.invalid;Port=5432;Database=rideforge;Username=rideforge;Password=unused";

    /// <summary>
    /// Install identifier the mobile client stamps on every request; the anonymous generation quota
    /// partitions its counters on it.
    /// </summary>
    public const string InstallHeaderName = "X-RideForge-Install";

    /// <summary>
    /// A client identifying as an install no other test has used.
    /// <para>
    /// Any test that calls <c>POST /route/generate</c> needs this, whatever it is actually
    /// asserting. The quota limits by install id and falls back to the caller's IP when there is
    /// none — and every in-memory test client shares one IP — so a suite of plain
    /// <c>CreateClient()</c> callers spends a single 2-request allowance between them and whichever
    /// test runs third gets a 429 instead of the status it asked about. The counters are
    /// process-wide (one host per <c>IClassFixture</c>), so this cannot be undone between tests;
    /// it has to be avoided by never sharing a partition in the first place.
    /// </para>
    /// <para>
    /// This also matches the real client, which always sends the header (see
    /// <c>src/api/client.ts</c>). Tests about the quota itself set the header themselves rather
    /// than using this.
    /// </para>
    /// </summary>
    public HttpClient CreateClientForFreshInstall()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(InstallHeaderName, Guid.NewGuid().ToString());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Supabase:ProjectUrl", ProjectUrl);
        builder.UseSetting("Supabase:Audience", Audience);
        builder.UseSetting("ConnectionStrings:RideForge", ConnectionString);
    }
}

/// <summary>
/// A host that trusts a locally generated signing key instead of Supabase's published one, for
/// suites that need to present a valid token.
/// <para>
/// Everything else about validation — issuer, audience, lifetime, signature — stays exactly as
/// <c>Program.cs</c> configures it. That is the only reason results from these suites say anything
/// about production: the key is swapped, the rules are not.
/// </para>
/// <para>
/// Hermetic by construction. <c>Authority</c>, <c>MetadataAddress</c> and
/// <c>ConfigurationManager</c> are cleared so a future edit to <c>Program.cs</c> cannot quietly
/// reintroduce a network fetch — with these cleared there is nothing left to fetch from.
/// </para>
/// </summary>
public class AuthenticatedApiFactory : RideForgeApiFactory
{
    private readonly RSA _signingRsa = RSA.Create(2048);

    /// <summary>The key the API will trust, standing in for the project's published public key.</summary>
    public SecurityKey SigningKey => new RsaSecurityKey(_signingRsa) { KeyId = "rideforge-test" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

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

    /// <summary>
    /// Mints a token the way Supabase would, so each test only has to say what is wrong with it.
    /// Every parameter defaults to a valid value; pass one to make exactly that thing invalid.
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
        if (disposing) _signingRsa.Dispose();
    }
}
