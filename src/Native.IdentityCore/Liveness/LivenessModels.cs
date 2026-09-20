using System.Text.Json.Serialization;

namespace Native.IdentityCore.Liveness;

/// <summary>Options for <see cref="ILivenessSessionService.CreateSessionAsync"/>, forwarded to <c>CreateFaceLivenessSessionRequest</c>.</summary>
/// <param name="AuditImagesLimit">Maximum number of audit images Rekognition should retain for the session (0-4). <see langword="null"/> uses the Rekognition default.</param>
/// <param name="KmsKeyId">KMS key used by Rekognition to encrypt session images at rest. Tenants with a dedicated key (GS-05) should pass it here.</param>
/// <param name="OutputS3Bucket">
/// When set with <paramref name="OutputS3KeyPrefix"/>, Rekognition writes reference/audit images
/// to this bucket instead of returning them inline — <see cref="LivenessSessionResult.ReferenceImageS3"/>/
/// <see cref="LivenessSessionResult.AuditImagesS3"/> carry the resulting locations, and
/// <see cref="LivenessSessionResult.ReferenceImage"/>/<see cref="LivenessSessionResult.AuditImages"/>
/// stay empty. This library never writes to storage itself — configuring this is the caller's
/// choice, not a default. <b>LGPD trade-off:</b> unlike inline mode (bytes flow through memory and
/// are never persisted by anyone in this call chain unless the caller chooses to), S3 output makes
/// Rekognition durably persist the captured biometric image in the caller's own bucket — the
/// caller MUST configure an S3 Lifecycle expiration rule (or another erasure mechanism) consistent
/// with its retention/right-to-erasure policy for biometric data (GS-09); this library has no way
/// to enforce or default one.
/// </param>
/// <param name="OutputS3KeyPrefix">See <paramref name="OutputS3Bucket"/>.</param>
/// <param name="ClientRequestToken">Idempotency token for session creation. Reusing the same token for the same tenant/attempt returns the existing session instead of creating a new one.</param>
public sealed record LivenessSessionOptions(
    int? AuditImagesLimit = null,
    string? KmsKeyId = null,
    string? OutputS3Bucket = null,
    string? OutputS3KeyPrefix = null,
    string? ClientRequestToken = null);

/// <summary>Handle returned by <see cref="ILivenessSessionService.CreateSessionAsync"/>. Hand the <see cref="SessionId"/> to the client SDK that drives the face capture UI.</summary>
public sealed record LivenessSessionHandle(string SessionId);

/// <summary>Mirrors Rekognition's <c>LivenessSessionStatus</c> constants as a closed enum for AOT-safe, exhaustive handling.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LivenessSessionStatus>))]
public enum LivenessSessionStatus
{
    Created,
    InProgress,
    Succeeded,
    Failed,
    Expired,
}

/// <summary>
/// Location of an image Rekognition wrote to S3 instead of returning inline, when
/// <see cref="LivenessSessionOptions.OutputS3Bucket"/> was configured for the session that
/// produced it. This library never reads, writes, or deletes this object itself — see the LGPD
/// note on <see cref="LivenessSessionOptions.OutputS3Bucket"/> for why the caller owns its
/// lifecycle/retention.
/// </summary>
/// <param name="Bucket">S3 bucket — the same value as <see cref="LivenessSessionOptions.OutputS3Bucket"/> for the session that produced this result.</param>
/// <param name="Key">Full S3 object key (already includes <see cref="LivenessSessionOptions.OutputS3KeyPrefix"/>).</param>
/// <param name="Version">S3 object version id, when the bucket has versioning enabled. <see langword="null"/> otherwise.</param>
public sealed record S3ImageReference(string Bucket, string Key, string? Version);

/// <summary>Result of <see cref="ILivenessSessionService.GetSessionResultAsync"/>.</summary>
/// <param name="SessionId">Echo of the queried session id.</param>
/// <param name="Status">Current session status.</param>
/// <param name="Confidence">Liveness confidence (0-100), present only once <see cref="Status"/> is <see cref="LivenessSessionStatus.Succeeded"/> (Rekognition may still omit it for a low-quality session).</param>
/// <param name="ReferenceImage">
/// Best frame captured during the session, present only in inline mode (no
/// <see cref="LivenessSessionOptions.OutputS3Bucket"/> configured for this session) — mutually
/// exclusive with <see cref="ReferenceImageS3"/>.
/// </param>
/// <param name="AuditImages">Additional frames captured during the session, for audit trail purposes, present only in inline mode — mirrors <see cref="AuditImagesS3"/> for S3 output mode.</param>
/// <param name="ReferenceImageS3">
/// S3 location of the best frame, present only when the session was created with
/// <see cref="LivenessSessionOptions.OutputS3Bucket"/> configured — mutually exclusive with
/// <see cref="ReferenceImage"/> (Rekognition returns either the bytes inline or the S3 location,
/// never both for the same session). <see langword="null"/> in inline mode.
/// </param>
/// <param name="AuditImagesS3">
/// S3 locations of the audit frames, populated only in S3 output mode (empty, never
/// <see langword="null"/>, when that mode was used but Rekognition returned no audit frames);
/// <see langword="null"/> in inline mode.
/// </param>
public sealed record LivenessSessionResult(
    string SessionId,
    LivenessSessionStatus Status,
    double? Confidence,
    ReadOnlyMemory<byte>? ReferenceImage,
    IReadOnlyList<ReadOnlyMemory<byte>> AuditImages,
    S3ImageReference? ReferenceImageS3 = null,
    IReadOnlyList<S3ImageReference>? AuditImagesS3 = null)
{
    public bool IsFinal => Status is LivenessSessionStatus.Succeeded or LivenessSessionStatus.Failed or LivenessSessionStatus.Expired;
}
