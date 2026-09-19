using System.Text.Json.Serialization;
using Native.IdentityCore.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// Wire shape of a <see cref="BiometricAssertion"/> inside the JWS payload. Uses JWT-style
/// registered claim names (<c>iss</c>/<c>sub</c>/<c>aud</c>/<c>iat</c>/<c>exp</c>/<c>jti</c>)
/// alongside this domain's own custom claims (<c>tenant_id</c>/<c>user_ref</c>/<c>purpose</c>/
/// <c>decision</c>/scores) — the custom claim names are unchanged from earlier versions of this
/// payload so a relying party that already parses them keeps working; <c>sub</c> simply mirrors
/// <c>user_ref</c> for tooling that expects the standard claim.
/// </summary>
public sealed record AssertionPayload(
    [property: JsonPropertyName("iss")] string Issuer,
    [property: JsonPropertyName("sub")] string Subject,
    [property: JsonPropertyName("aud"), JsonConverter(typeof(AudienceJsonConverter))] IReadOnlyList<string> Audience,
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("user_ref")] string UserRef,
    [property: JsonPropertyName("purpose")] BiometricAssertionPurpose Purpose,
    [property: JsonPropertyName("liveness_confidence")] double? LivenessConfidence,
    [property: JsonPropertyName("face_similarity")] double? FaceSimilarity,
    [property: JsonPropertyName("decision")] Policy.BiometricOutcome Decision,
    [property: JsonPropertyName("iat")] long IssuedAtUnixSeconds,
    [property: JsonPropertyName("exp")] long ExpiresAtUnixSeconds,
    [property: JsonPropertyName("jti")] string Jti)
{
    /// <summary>
    /// Builds the wire payload for signing. <paramref name="issuer"/> always wins over
    /// <paramref name="assertion"/>'s own <see cref="BiometricAssertion.Issuer"/> — the signer,
    /// not the caller, is the trust boundary for <c>iss</c> (see <see cref="KmsAssertionSigner"/>).
    /// <c>aud</c> follows the default audience strategy: an explicit, non-empty
    /// <see cref="BiometricAssertion.Audience"/> wins; otherwise it defaults to <c>[TenantId]</c>.
    /// </summary>
    public static AssertionPayload FromAssertion(BiometricAssertion assertion, string issuer) => new(
        Issuer: issuer,
        Subject: assertion.UserRef,
        Audience: assertion.Audience is { Count: > 0 } audience ? audience : [assertion.TenantId],
        TenantId: assertion.TenantId,
        UserRef: assertion.UserRef,
        Purpose: assertion.Purpose,
        LivenessConfidence: assertion.LivenessConfidence,
        FaceSimilarity: assertion.FaceSimilarity,
        Decision: assertion.Decision,
        IssuedAtUnixSeconds: assertion.IssuedAt.ToUnixTimeSeconds(),
        ExpiresAtUnixSeconds: assertion.ExpiresAt.ToUnixTimeSeconds(),
        Jti: assertion.Jti);
}
