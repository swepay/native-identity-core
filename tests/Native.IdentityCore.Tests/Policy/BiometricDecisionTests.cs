using Native.IdentityCore.Policy;

namespace Native.IdentityCore.Tests.Policy;

public class BiometricDecisionTests
{
    private static readonly BiometricPolicy Policy = new(MinLivenessConfidence: 90d, MinFaceSimilarity: 95d, MaxAttempts: 3);

    [Fact]
    public void Evaluate_LivenessAndSimilarityAboveThreshold_ReturnsVerified()
    {
        // Arrange / Act
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: 95d, faceSimilarity: 98d, faceMatchFound: true, attemptNumber: 1);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Verified);
        result.Reasons.ShouldBeEmpty();
        result.ShouldBe(BiometricDecisionResult.Verified);
    }

    [Fact]
    public void Evaluate_LivenessBelowThresholdFirstAttempt_ReturnsRetryWithReason()
    {
        // Arrange / Act
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: 50d, faceSimilarity: 98d, faceMatchFound: true, attemptNumber: 1);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Retry);
        result.Reasons.ShouldBe([BiometricRejectionReason.LivenessBelowThreshold]);
    }

    [Fact]
    public void Evaluate_LivenessBelowThresholdLastAttempt_ReturnsRejectedWithMaxAttemptsExceeded()
    {
        // Arrange / Act
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: 50d, faceSimilarity: 98d, faceMatchFound: true, attemptNumber: 3);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Rejected);
        result.Reasons.ShouldBe([BiometricRejectionReason.LivenessBelowThreshold, BiometricRejectionReason.MaxAttemptsExceeded]);
    }

    [Fact]
    public void Evaluate_SimilarityBelowThreshold_ReturnsRetryWithReason()
    {
        // Arrange / Act
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: 95d, faceSimilarity: 60d, faceMatchFound: true, attemptNumber: 1);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Retry);
        result.Reasons.ShouldBe([BiometricRejectionReason.SimilarityBelowThreshold]);
    }

    [Fact]
    public void Evaluate_NoFaceMatchFound_ReturnsNoFaceMatchReason()
    {
        // Arrange / Act
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: 95d, faceSimilarity: 0d, faceMatchFound: false, attemptNumber: 1);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Retry);
        result.Reasons.ShouldBe([BiometricRejectionReason.NoFaceMatch]);
    }

    [Fact]
    public void Evaluate_LivenessAndSimilarityBothBelowThreshold_ReturnsBothReasons()
    {
        // Arrange / Act
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: 50d, faceSimilarity: 60d, faceMatchFound: true, attemptNumber: 1);

        // Assert
        result.Reasons.ShouldBe([BiometricRejectionReason.LivenessBelowThreshold, BiometricRejectionReason.SimilarityBelowThreshold]);
    }

    [Fact]
    public void Evaluate_ScoresNull_AreNotEvaluated()
    {
        // Arrange / Act — neither liveness nor face matching was part of this flow.
        var result = BiometricDecision.Evaluate(Policy, livenessConfidence: null, faceSimilarity: null, faceMatchFound: false, attemptNumber: 1);

        // Assert
        result.ShouldBe(BiometricDecisionResult.Verified);
    }

    [Fact]
    public void Evaluate_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => BiometricDecision.Evaluate(null!, 95d, 98d, true, 1);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void Evaluate_AttemptNumberBelowOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange / Act
        var act = () => BiometricDecision.Evaluate(Policy, 95d, 98d, true, attemptNumber: 0);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).ParamName.ShouldBe("attemptNumber");
    }

    [Fact]
    public void EvaluateLivenessOnly_DelegatesToEvaluateWithoutFaceMatching()
    {
        // Arrange / Act
        var result = BiometricDecision.EvaluateLivenessOnly(Policy, livenessConfidence: 96d, attemptNumber: 1);

        // Assert
        result.ShouldBe(BiometricDecisionResult.Verified);
    }

    [Fact]
    public void EvaluateLivenessSessionFailed_FirstAttempt_ReturnsRetry()
    {
        // Arrange / Act
        var result = BiometricDecision.EvaluateLivenessSessionFailed(Policy, attemptNumber: 1);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Retry);
        result.Reasons.ShouldBe([BiometricRejectionReason.LivenessSessionFailed]);
    }

    [Fact]
    public void EvaluateLivenessSessionFailed_LastAttempt_ReturnsRejected()
    {
        // Arrange / Act
        var result = BiometricDecision.EvaluateLivenessSessionFailed(Policy, attemptNumber: 3);

        // Assert
        result.Outcome.ShouldBe(BiometricOutcome.Rejected);
        result.Reasons.ShouldBe([BiometricRejectionReason.LivenessSessionFailed, BiometricRejectionReason.MaxAttemptsExceeded]);
    }

    [Fact]
    public void EvaluateLivenessSessionFailed_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => BiometricDecision.EvaluateLivenessSessionFailed(null!, 1);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void EvaluateLivenessSessionFailed_AttemptNumberBelowOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange / Act
        var act = () => BiometricDecision.EvaluateLivenessSessionFailed(Policy, attemptNumber: 0);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).ParamName.ShouldBe("attemptNumber");
    }
}
