using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace RideForgeApi.Auth;

/// <summary>
/// Reads a bare JWKS document into a <see cref="JsonWebKeySet"/> for
/// <see cref="ConfigurationManager{T}"/>.
/// <para>
/// The IdentityModel package ships a retriever for the OIDC discovery document but not for a
/// plain JWKS URL, and Supabase documents the JWKS endpoint rather than promising discovery — so
/// pointing at the discovery document would add a dependency on something that may not be served.
/// This is the missing half-page that lets the cached, self-refreshing
/// <see cref="ConfigurationManager{T}"/> work directly against the URL Supabase does publish.
/// </para>
/// </summary>
public sealed class JwksRetriever : IConfigurationRetriever<JsonWebKeySet>
{
    public async Task<JsonWebKeySet> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
        return new JsonWebKeySet(document);
    }
}
