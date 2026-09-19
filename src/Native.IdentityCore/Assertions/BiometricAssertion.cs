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
/// <param name="CorrelationId">Request/trace id (GS-11) propagated into the JWS as <c>jti</c> for cross-system correlation — not a secret.</param>
public sealed record BiometricAssertion(
    string TenantId,
    string UserRef,
    BiometricAssertionPurpose Purpose,
    double? LivenessConfidence,
    double? FaceSimilarity,
    BiometricOutcome Decision,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string CorrelationId)
{
    /// <summary>Throws if any required field is missing or <see cref="ExpiresAt"/> is not after <see cref="IssuedAt"/>.</summary>
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

        if (string.IsNullOrEmpty(CorrelationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(CorrelationId));
        }

        if (ExpiresAt <= IssuedAt)
        {
            throw new ArgumentException("ExpiresAt must be after IssuedAt.", nameof(ExpiresAt));
        }

        return this;
    }
}
