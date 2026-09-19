using Native.IdentityCore.Policy;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// A signed statement that a tenant's user completed (or failed) a biometric check — the payload
/// <see cref="IAssertionSigner"/> turns into a compact JWS for a relying party to verify offline.
/// Carries scores, never images (this library never persists or transmits raw biometric images
/// inside an assertion — GS-09).
/// </summary>
/// <param name="TenantId">Owning tenant (GS-05); relying parties must reject an assertion whose <see cref="TenantId"/> does not match the expected tenant.</param>
/// <param name="UserRef">Same user reference used with <see cref="FaceIndex.IFaceIndex"/>.</param>
/// <param name="Purpose">Narrows what this assertion may be used for.</param>
/// <param name="LivenessConfidence">Score fed into <see cref="BiometricDecision.Evaluate"/>, carried through for audit.</param>
/// <param name="FaceSimilarity">Score fed into <see cref="BiometricDecision.Evaluate"/>, carried through for audit.</param>
/// <param name="Decision">Outcome computed by <see cref="BiometricDecision.Evaluate"/>. Only <see cref="BiometricOutcome.Verified"/> assertions should be treated as authoritative by a relying party; <see cref="BiometricOutcome.Rejected"/>/<see cref="BiometricOutcome.Retry"/> assertions exist for audit trail, not authorization.</param>
/// <param name="IssuedAt">When the assertion was issued. Caller supplies this (typically from an injected <see cref="TimeProvider"/>) — this library never calls <c>DateTimeOffset.UtcNow</c> itself.</param>
/// <param name="ExpiresAt">When the assertion stops being valid. Must be after <paramref name="IssuedAt"/>.</param>
/// <param name="Jti">Request/trace id (GS-11), propagated into the JWS as the standard <c>jti</c> claim — doubles as the cross-system correlation id. Not a secret. A relying party is expected to use it for replay protection (this library does not track consumed <c>jti</c>s itself).</param>
/// <param name="Issuer">
/// The JWS <c>iss</c> claim — an absolute URI identifying the issuing product/environment.
/// When <em>signing</em>, leave this at its default (<c>""</c>): <see cref="KmsAssertionSigner"/> always
/// stamps <c>iss</c> from its own <see cref="KmsAssertionSignerOptions.Issuer"/>, never from this field
/// (a caller-supplied issuer must never be trusted as-is). This field exists so
/// <see cref="IAssertionVerifier"/> can hand back the <em>verified</em> issuer on the parsed result.
/// </param>
/// <param name="Audience">
/// The JWS <c>aud</c> claim — the relying party id(s) this assertion is scoped to. When <em>signing</em>
/// and left <c>null</c>/empty, <see cref="KmsAssertionSigner"/> defaults it to <c>[TenantId]</c> (a
/// tenant's own id is the default audience strategy); pass an explicit value to target a different,
/// tenant-configured audience instead. When <em>verifying</em>, this is the audience actually found on
/// the token (already checked against the caller's expected value/list).
/// </param>
public sealed record BiometricAssertion(
    string TenantId,
    string UserRef,
    BiometricAssertionPurpose Purpose,
    double? LivenessConfidence,
    double? FaceSimilarity,
    BiometricOutcome Decision,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Jti,
    string Issuer = "",
    IReadOnlyList<string>? Audience = null)
{
    /// <summary>Throws if any required field is missing or <see cref="ExpiresAt"/> is not after <see cref="IssuedAt"/>. <see cref="Issuer"/>/<see cref="Audience"/> are not checked here — they are optional at signing time (see their doc comments).</summary>
    public BiometricAssertion Validate()
    {
        if (string.IsNullOrEmpty(TenantId))
        {
            throw new ArgumentException("TenantId is required.", nameof(TenantId));
        }

        if (string.IsNullOrEmpty(UserRef))
        {
            throw new ArgumentException("UserRef is required.", nameof(UserRef));
        }

        if (string.IsNullOrEmpty(Jti))
        {
            throw new ArgumentException("Jti is required.", nameof(Jti));
        }

        if (ExpiresAt <= IssuedAt)
        {
            throw new ArgumentException("ExpiresAt must be after IssuedAt.", nameof(ExpiresAt));
        }

        return this;
    }
}
