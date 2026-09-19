namespace Native.IdentityCore.Policy;

/// <summary>
/// Tenant-configurable thresholds used by <see cref="BiometricDecision"/> to turn raw
/// Rekognition scores into a <see cref="BiometricOutcome"/>. Immutable — build a new
/// instance with the `with` expression to override a threshold per tenant/product.
/// </summary>
/// <param name="MinLivenessConfidence">
/// Minimum face-liveness confidence (0-100) returned by
/// <c>GetFaceLivenessSessionResults</c> required to consider the session live.
/// </param>
/// <param name="MinFaceSimilarity">
/// Minimum face-match similarity (0-100) returned by <c>SearchFacesByImage</c>
/// required to consider the presented face a match for the enrolled user.
/// </param>
/// <param name="MaxAttempts">
/// Maximum number of attempts (1-based) allowed for a single verification flow
/// before a failing decision becomes final (<see cref="BiometricOutcome.Rejected"/>
/// instead of <see cref="BiometricOutcome.Retry"/>).
/// </param>
public sealed record BiometricPolicy(
    double MinLivenessConfidence = 90d,
    double MinFaceSimilarity = 95d,
    int MaxAttempts = 3)
{
    /// <summary>Validates the policy is internally consistent. Throws <see cref="ArgumentOutOfRangeException"/> otherwise.</summary>
    public BiometricPolicy Validate()
    {
        if (MinLivenessConfidence is < 0d or > 100d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinLivenessConfidence), MinLivenessConfidence, "Must be between 0 and 100.");
        }

        if (MinFaceSimilarity is < 0d or > 100d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinFaceSimilarity), MinFaceSimilarity, "Must be between 0 and 100.");
        }

        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "Must be at least 1.");
        }

        return this;
    }
}
