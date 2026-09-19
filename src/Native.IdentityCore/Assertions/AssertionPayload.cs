using System.Text.Json.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// Wire shape of a <see cref="BiometricAssertion"/> inside the JWS payload. Uses JWT-style
/// registered claim names (<c>iat</c>/<c>exp</c>/<c>jti</c>) where a direct equivalent exists,
/// so relying parties can reuse standard JWT tooling for expiry checks.
/// </summary>
public sealed record AssertionPayload(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("user_ref")] string UserRef,
    [property: JsonPropertyName("purpose")] BiometricAssertionPurpose Purpose,
    [property: JsonPropertyName("liveness_confidence")] double? LivenessConfidence,
    [property: JsonPropertyName("face_similarity")] double? FaceSimilarity,
    [property: JsonPropertyName("decision")] Policy.BiometricOutcome Decision,
    [property: JsonPropertyName("iat")] long IssuedAtUnixSeconds,
    [property: JsonPropertyName("exp")] long ExpiresAtUnixSeconds,
    [property: JsonPropertyName("jti")] string CorrelationId)
{
    public static AssertionPayload FromAssertion(BiometricAssertion assertion) => new(
        assertion.TenantId,
        assertion.UserRef,
        assertion.Purpose,
        assertion.LivenessConfidence,
        assertion.FaceSimilarity,
        assertion.Decision,
        assertion.IssuedAt.ToUnixTimeSeconds(),
        assertion.ExpiresAt.ToUnixTimeSeconds(),
        assertion.CorrelationId);
}
