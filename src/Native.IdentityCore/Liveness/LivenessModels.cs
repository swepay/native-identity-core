using System.Text.Json.Serialization;

namespace Native.IdentityCore.Liveness;

/// <summary>Options for <see cref="ILivenessSessionService.CreateSessionAsync"/>, forwarded to <c>CreateFaceLivenessSessionRequest</c>.</summary>
/// <param name="AuditImagesLimit">Maximum number of audit images Rekognition should retain for the session (0-4). <see langword="null"/> uses the Rekognition default.</param>
/// <param name="KmsKeyId">KMS key used by Rekognition to encrypt session images at rest. Tenants with a dedicated key (GS-05) should pass it here.</param>
/// <param name="OutputS3Bucket">When set with <paramref name="OutputS3KeyPrefix"/>, Rekognition writes reference/audit images to this bucket instead of returning them inline. This library never writes to storage itself — configuring this is the caller's choice, not a default.</param>
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

/// <summary>Result of <see cref="ILivenessSessionService.GetSessionResultAsync"/>.</summary>
/// <param name="SessionId">Echo of the queried session id.</param>
/// <param name="Status">Current session status.</param>
/// <param name="Confidence">Liveness confidence (0-100), present only once <see cref="Status"/> is <see cref="LivenessSessionStatus.Succeeded"/> (Rekognition may still omit it for a low-quality session).</param>
/// <param name="ReferenceImage">Best frame captured during the session, if Rekognition returned it inline (not configured for S3 output).</param>
/// <param name="AuditImages">Additional frames captured during the session, for audit trail purposes, if returned inline.</param>
public sealed record LivenessSessionResult(
    string SessionId,
    LivenessSessionStatus Status,
    double? Confidence,
    ReadOnlyMemory<byte>? ReferenceImage,
    IReadOnlyList<ReadOnlyMemory<byte>> AuditImages)
{
    public bool IsFinal => Status is LivenessSessionStatus.Succeeded or LivenessSessionStatus.Failed or LivenessSessionStatus.Expired;
}
