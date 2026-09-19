namespace Native.IdentityCore.Policy;

/// <summary>
/// Pure evaluator that turns raw Rekognition scores into a <see cref="BiometricDecisionResult"/>
/// according to a <see cref="BiometricPolicy"/>. No I/O, no AWS SDK dependency — this is the
/// single place tenant thresholds are enforced, so every caller (liveness-only, face-match-only,
/// or full enrollment/verification) evaluates the same way.
/// </summary>
public static class BiometricDecision
{
    /// <param name="policy">Tenant/product thresholds.</param>
    /// <param name="livenessConfidence">Confidence (0-100) from <c>GetFaceLivenessSessionResultsResponse.Confidence</c>, or <see langword="null"/> when the session did not produce one (failed/expired) or liveness was not part of this flow.</param>
    /// <param name="faceSimilarity">Best match similarity (0-100) from <see cref="FaceIndex.FaceSearchResult.BestMatch"/>, or <see langword="null"/> when face matching was not part of this flow.</param>
    /// <param name="faceMatchFound">Whether <see cref="FaceIndex.IFaceIndex.SearchAsync"/> returned a candidate at all. Ignored when <paramref name="faceSimilarity"/> is <see langword="null"/> (face matching not evaluated).</param>
    /// <param name="attemptNumber">1-based attempt number for this verification flow.</param>
    public static BiometricDecisionResult Evaluate(
        BiometricPolicy policy,
        double? livenessConfidence,
        double? faceSimilarity,
        bool faceMatchFound,
        int attemptNumber)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), attemptNumber, "Attempt number is 1-based.");
        }

        var reasons = new List<BiometricRejectionReason>(capacity: 3);

        if (livenessConfidence is not null)
        {
            if (livenessConfidence.Value < policy.MinLivenessConfidence)
            {
                reasons.Add(BiometricRejectionReason.LivenessBelowThreshold);
            }
        }

        if (faceSimilarity is not null)
        {
            if (!faceMatchFound)
            {
                reasons.Add(BiometricRejectionReason.NoFaceMatch);
            }
            else if (faceSimilarity.Value < policy.MinFaceSimilarity)
            {
                reasons.Add(BiometricRejectionReason.SimilarityBelowThreshold);
            }
        }

        if (reasons.Count == 0)
        {
            return BiometricDecisionResult.Verified;
        }

        var attemptsExhausted = attemptNumber >= policy.MaxAttempts;
        if (attemptsExhausted)
        {
            reasons.Add(BiometricRejectionReason.MaxAttemptsExceeded);
        }

        var outcome = attemptsExhausted ? BiometricOutcome.Rejected : BiometricOutcome.Retry;
        return new BiometricDecisionResult(outcome, reasons);
    }

    /// <summary>
    /// Convenience overload for a flow that only ran a liveness check (no face index lookup) —
    /// e.g. a self-service enrollment step before the face is indexed for the first time.
    /// </summary>
    public static BiometricDecisionResult EvaluateLivenessOnly(BiometricPolicy policy, double? livenessConfidence, int attemptNumber) =>
        Evaluate(policy, livenessConfidence, faceSimilarity: null, faceMatchFound: false, attemptNumber);

    /// <summary>
    /// Convenience overload for a flow that failed the liveness session outright — Rekognition
    /// returned <c>FAILED</c>/<c>EXPIRED</c> with no confidence score. Always rejects on the
    /// last attempt, retries otherwise.
    /// </summary>
    public static BiometricDecisionResult EvaluateLivenessSessionFailed(BiometricPolicy policy, int attemptNumber)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), attemptNumber, "Attempt number is 1-based.");
        }

        var reasons = new List<BiometricRejectionReason> { BiometricRejectionReason.LivenessSessionFailed };
        var attemptsExhausted = attemptNumber >= policy.MaxAttempts;
        if (attemptsExhausted)
        {
            reasons.Add(BiometricRejectionReason.MaxAttemptsExceeded);
        }

        var outcome = attemptsExhausted ? BiometricOutcome.Rejected : BiometricOutcome.Retry;
        return new BiometricDecisionResult(outcome, reasons);
    }
}
