namespace Native.IdentityCore.Assertions;

/// <summary>
/// Publishes this issuer's signing key(s) as a JWKS (RFC 7517) — the issuer-side counterpart to
/// <see cref="IAssertionSigner"/>, so a relying party's <see cref="IAssertionVerifier"/>/
/// <see cref="JwksAssertionVerifier"/> can fetch and verify assertions offline. Returns the same
/// <see cref="JwksDocumentDto"/>/<see cref="JwkDto"/> shape <see cref="JwksAssertionVerifier"/>
/// deserializes from HTTP, so the two sides of this library agree on the wire format without a
/// second, hand-rolled contract.
/// </summary>
public interface IAssertionKeyPublisher
{
    /// <summary>
    /// Returns the current JWKS document. Implementations are expected to cache it (see
    /// <see cref="KmsAssertionKeyPublisher"/>'s cache TTL) so a caller can serve it directly from
    /// a <c>GET /.well-known/jwks.json</c> handler without calling KMS on every request.
    /// </summary>
    ValueTask<JwksDocumentDto> GetJwksAsync(CancellationToken cancellationToken = default);
}
