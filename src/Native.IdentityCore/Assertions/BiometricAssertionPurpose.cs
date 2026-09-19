using System.Text.Json.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>Why a <see cref="BiometricAssertion"/> was issued — narrows what a relying party should accept it for.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BiometricAssertionPurpose>))]
public enum BiometricAssertionPurpose
{
    /// <summary>First-time face enrollment for a user.</summary>
    Enrollment,

    /// <summary>Step-up or transactional verification against an already-enrolled face.</summary>
    Verification,

    /// <summary>Account recovery flow (e.g. replacing a lost credential) gated by a face match.</summary>
    Recovery,

    /// <summary>One-off challenge (e.g. high-value transaction confirmation) that must not be reused for other purposes.</summary>
    Challenge,
}
