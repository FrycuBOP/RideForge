namespace RideForgeApi.Auth;

/// <summary>
/// Config for Supabase-issued JWT verification. Bound from the <c>Supabase</c> section.
/// On Railway the value arrives as env var <c>Supabase__ProjectUrl</c> (ASP.NET Core's
/// <c>__</c> → section convention).
/// <para>
/// Nothing here is a secret. Tokens are verified against the project's <em>public</em> JWKS, so
/// no shared signing secret ever reaches this service — which is the point: a secret that drifts
/// between Railway and local config produces hours of unexplained 401s, and there is no secret
/// here to drift.
/// </para>
/// </summary>
public sealed class SupabaseAuthOptions
{
    public const string SectionName = "Supabase";

    /// <summary>
    /// Project base URL, e.g. <c>https://abcdefgh.supabase.co</c> (no trailing path). Blank is a
    /// startup failure — see <c>Program.cs</c>.
    /// </summary>
    public string ProjectUrl { get; set; } = string.Empty;

    /// <summary>The <c>aud</c> claim Supabase stamps on a signed-in rider's access token.</summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>
    /// Token issuer and JWKS host: <c>{ProjectUrl}/auth/v1</c>. Supabase stamps this exact string
    /// as the <c>iss</c> claim.
    /// </summary>
    public string Authority => $"{ProjectUrl.TrimEnd('/')}/auth/v1";

    /// <summary>Where the project publishes the public halves of its asymmetric signing keys.</summary>
    public string JwksUri => $"{Authority}/.well-known/jwks.json";
}
