namespace Native.IdentityCore.Policy;

/// <summary>Result produced by <see cref="BiometricDecision.Evaluate"/>: the outcome plus every reason that kept it from being <see cref="BiometricOutcome.Verified"/>.</summary>
/// <param name="Outcome">The final decision for this attempt.</param>
/// <param name="Reasons">Empty when <paramref name="Outcome"/> is <see cref="BiometricOutcome.Verified"/>; otherwise every threshold that was not met.</param>
public sealed record BiometricDecisionResult(BiometricOutcome Outcome, IReadOnlyList<BiometricRejectionReason> Reasons)
{
    public static BiometricDecisionResult Verified { get; } = new(BiometricOutcome.Verified, []);
}
