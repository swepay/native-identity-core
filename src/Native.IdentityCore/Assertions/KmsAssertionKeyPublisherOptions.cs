namespace Native.IdentityCore.Assertions;

/// <summary>Configuration for <see cref="KmsAssertionKeyPublisher"/>.</summary>
/// <param name="KeyIds">
/// KMS key ids/ARNs/aliases to publish, in rotation order: the first entry MUST be the same
/// <see cref="KmsAssertionSignerOptions.KeyId"/> the paired <see cref="KmsAssertionSigner"/>
/// currently signs with (so its JWS <c>kid</c> resolves against this JWKS); any additional
/// entries are previous signing keys kept published only long enough for already-issued,
/// not-yet-expired assertions to still verify (rotation window) — drop a key from this list once
/// every assertion it ever signed has expired. Each key must be an EC P-256
/// (<c>ECC_NIST_P256</c>) asymmetric KMS key — this publisher only supports ES256 today, the same
/// restriction <see cref="JwksAssertionVerifier"/> already has on the verify side.
/// </param>
/// <param name="CacheTtl">
/// How long a published <see cref="JwksDocumentDto"/> is served from memory before this
/// publisher calls <c>kms:GetPublicKey</c> again. <see langword="null"/> uses the library
/// default (15 minutes — the same cache TTL <see cref="JwksAssertionVerifier"/> already uses on
/// the verify side).
/// </param>
public sealed record KmsAssertionKeyPublisherOptions(IReadOnlyList<string> KeyIds, TimeSpan? CacheTtl = null);
