namespace RideForgeApi.RateLimiting;

/// <summary>
/// Config for the anonymous route-generation quota. Bound from the <c>GenerationQuota</c> section;
/// on Railway the values arrive as <c>GenerationQuota__PermitLimit</c> /
/// <c>GenerationQuota__WindowMinutes</c> (ASP.NET Core's <c>__</c> → section convention).
/// <para>
/// The numbers live here rather than inline in <c>Program.cs</c> for two reasons: the limit is a
/// product decision that will be tuned without a code change, and the quota tests need to drive it
/// down to something a test can exhaust in a few requests.
/// </para>
/// </summary>
public sealed class GenerationQuotaOptions
{
    public const string SectionName = "GenerationQuota";

    /// <summary>
    /// Name of the rate-limiting policy this quota configures. It lives on the options rather than
    /// in <c>Program.cs</c> because the endpoints that opt into it are declared elsewhere, and a
    /// policy name that does not match the registered one fails at request time, not at build.
    /// </summary>
    public const string PolicyName = "generation-quota";

    /// <summary>Generations an anonymous rider may make per window. Signed-in riders are exempt.</summary>
    public int PermitLimit { get; set; } = 2;

    /// <summary>Length of the fixed window the allowance resets on.</summary>
    public int WindowMinutes { get; set; } = 60;
}
