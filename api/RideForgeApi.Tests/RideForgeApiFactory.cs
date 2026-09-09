using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Supabase:ProjectUrl", ProjectUrl);
        builder.UseSetting("Supabase:Audience", Audience);
    }
}
