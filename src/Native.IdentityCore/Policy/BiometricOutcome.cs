using System.Text.Json.Serialization;

namespace Native.IdentityCore.Policy;

/// <summary>Final outcome of a biometric verification attempt evaluated by <see cref="BiometricDecision"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BiometricOutcome>))]
public enum BiometricOutcome
{
    /// <summary>Liveness and face-match thresholds were both met.</summary>
    Verified,

    /// <summary>Thresholds were not met and no further attempt is allowed (final).</summary>
    Rejected,

    /// <summary>Thresholds were not met but the caller may attempt again (<see cref="BiometricPolicy.MaxAttempts"/> not yet reached).</summary>
    Retry,
}
