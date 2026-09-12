using System.Security.Claims;

namespace RideForgeApi.Auth;

/// <summary>
/// Who is calling, as the verified token states it. One rule, in one place: <c>/me</c> and all three
/// saved-route endpoints answer this question, and they have to answer it identically or a rider's
/// routes end up filed under an identity the rest of the API does not recognise.
/// </summary>
public static class RiderIdentity
{
    /// <summary>
    /// The rider's id as the verified token states it. Read under both names because inbound claim
    /// mapping may or may not have renamed <c>sub</c> to <see cref="ClaimTypes.NameIdentifier"/>.
    /// <para>
    /// <c>sub</c> is read <em>first</em> deliberately. With default inbound claim mapping a literal
    /// <c>nameid</c> claim also lands on <see cref="ClaimTypes.NameIdentifier"/>, so a token
    /// carrying both would otherwise resolve to whichever happened to come first in claim order.
    /// Nothing issues such a token on this Supabase configuration today, but the RLS policy on
    /// <c>rideforge.saved_routes</c> is <c>USING (true)</c> — this value <em>is</em> the guard
    /// between two riders, and a guard should not depend on claim ordering.
    /// </para>
    /// </summary>
    public static string? SubjectOf(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// The rider's id as a <see cref="Guid"/>, or <see langword="false"/> when the token identifies
    /// nobody this API can store or read a route for. Supabase subjects are always UUIDs, so a
    /// validly signed token whose <c>sub</c> is not one is a 401 at every call site — which is
    /// exactly why the parse lives here rather than being repeated in three handlers.
    /// <para>
    /// <see cref="Guid.Empty"/> is rejected as well: <see cref="Guid.TryParse"/> accepts the all-zero
    /// UUID, and no rider is ever that, so letting it through would hand a degenerate subject its own
    /// owner namespace to read and write.
    /// </para>
    /// </summary>
    public static bool TryGetRiderId(ClaimsPrincipal user, out Guid riderId) =>
        Guid.TryParse(SubjectOf(user), out riderId) && riderId != Guid.Empty;
}
