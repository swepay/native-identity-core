using System.Text.Json.Serialization;

namespace Native.IdentityCore.Policy;

/// <summary>Reason codes explaining why a biometric attempt did not reach <see cref="BiometricOutcome.Verified"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BiometricRejectionReason>))]
public enum BiometricRejectionReason
{
    /// <summary>Liveness session did not complete successfully (session <c>FAILED</c>/<c>EXPIRED</c>, or no confidence returned).</summary>
    LivenessSessionFailed,

    /// <summary>Liveness confidence was below <see cref="BiometricPolicy.MinLivenessConfidence"/>.</summary>
    LivenessBelowThreshold,

    /// <summary><see cref="FaceIndex.IFaceIndex.SearchAsync"/> found no candidate face in the tenant collection.</summary>
    NoFaceMatch,

    /// <summary>Best face-match similarity was below <see cref="BiometricPolicy.MinFaceSimilarity"/>.</summary>
    SimilarityBelowThreshold,

    /// <summary><see cref="BiometricPolicy.MaxAttempts"/> was reached without a successful verification.</summary>
    MaxAttemptsExceeded,
}
