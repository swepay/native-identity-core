namespace Native.IdentityCore.Assertions;

/// <summary>
/// Verifies a compact JWS produced by <see cref="IAssertionSigner"/> — the counterpart a relying
/// party (NativeGuard, Passly, a third-party IdP) uses to check a <see cref="BiometricAssertion"/>
/// offline against a published JWKS. Implementations do not track consumed <c>jti</c>s: replay
/// protection is the caller's responsibility (see <see cref="AssertionVerificationResult"/>).
/// </summary>
public interface IAssertionVerifier
{
    /// <summary>
    /// Verifies <paramref name="assertionJws"/> against <paramref name="options"/> as of
    /// <paramref name="now"/> (caller-supplied — this library never calls
    /// <see cref="DateTimeOffset.UtcNow"/> itself, same convention as <see cref="IAssertionSigner"/>).
    /// </summary>
    ValueTask<AssertionVerificationResult> VerifyAsync(
        AssertionVerifierOptions options,
        string assertionJws,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>Per-call configuration for <see cref="IAssertionVerifier"/> — typically built from a tenant/realm/project's own settings.</summary>
/// <param name="JwksUrl">Where to fetch the issuer's JWKS (RFC 7517) from. Cached by the implementation (see <see cref="JwksAssertionVerifier"/>).</param>
/// <param name="ExpectedIssuer">The exact <c>iss</c> value the assertion must carry — configuration, never a hardcoded domain (there may be no confirmed public host yet).</param>
/// <param name="ExpectedAudience">The relying party's own id(s) — valid if the assertion's <c>aud</c> contains at least one of these.</param>
/// <param name="ExpectedPurpose">The only <see cref="BiometricAssertionPurpose"/> accepted for this call (e.g. a recovery endpoint only accepts <see cref="BiometricAssertionPurpose.Recovery"/>).</param>
/// <param name="MaxAssertionAge">How old (from <c>iat</c>) an assertion may be and still be accepted.</param>
/// <param name="ClockSkew">Tolerance applied to both <c>iat</c> (future) and <c>exp</c> (past) checks, to absorb clock drift between issuer and relying party.</param>
public sealed record AssertionVerifierOptions(
    string JwksUrl,
    string ExpectedIssuer,
    IReadOnlyList<string> ExpectedAudience,
    BiometricAssertionPurpose ExpectedPurpose,
    TimeSpan MaxAssertionAge,
    TimeSpan ClockSkew);

/// <summary>Why <see cref="IAssertionVerifier.VerifyAsync"/> rejected an assertion.</summary>
public enum AssertionVerificationError
{
    /// <summary>Not a well-formed 3-part JWS, not valid JSON, missing a required field, or an unsupported/missing <c>alg</c>/<c>kid</c> header.</summary>
    Malformed,

    /// <summary>The JWKS endpoint could not be fetched or its body was not a valid JWKS document.</summary>
    JwksUnavailable,

    /// <summary>The header's <c>kid</c> is not present in the (possibly just-refreshed) JWKS.</summary>
    UnknownKey,

    /// <summary>The ES256 signature did not verify against the resolved key.</summary>
    InvalidSignature,

    /// <summary><c>iss</c> did not match <see cref="AssertionVerifierOptions.ExpectedIssuer"/>.</summary>
    IssuerMismatch,

    /// <summary><c>aud</c> did not contain any of <see cref="AssertionVerifierOptions.ExpectedAudience"/>.</summary>
    AudienceMismatch,

    /// <summary><c>purpose</c> did not match <see cref="AssertionVerifierOptions.ExpectedPurpose"/>.</summary>
    PurposeMismatch,

    /// <summary><c>decision</c> was not <see cref="Policy.BiometricOutcome.Verified"/>.</summary>
    NotVerifiedDecision,

    /// <summary><c>exp</c> (plus <see cref="AssertionVerifierOptions.ClockSkew"/>) is in the past.</summary>
    Expired,

    /// <summary><c>iat</c> (minus <see cref="AssertionVerifierOptions.ClockSkew"/>) is in the future.</summary>
    FutureIssuedAt,

    /// <summary>The assertion is older than <see cref="AssertionVerifierOptions.MaxAssertionAge"/> (plus <see cref="AssertionVerifierOptions.ClockSkew"/>).</summary>
    TooOld,
}

/// <summary>Outcome of <see cref="IAssertionVerifier.VerifyAsync"/>.</summary>
public sealed record AssertionVerificationResult(bool IsValid, AssertionVerificationError? Error, BiometricAssertion? Assertion)
{
    public static AssertionVerificationResult Valid(BiometricAssertion assertion) => new(true, null, assertion);

    public static AssertionVerificationResult Invalid(AssertionVerificationError error) => new(false, error, null);
}
