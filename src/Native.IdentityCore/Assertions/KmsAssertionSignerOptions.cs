namespace Native.IdentityCore.Assertions;

/// <summary>Configuration for <see cref="KmsAssertionSigner"/>.</summary>
/// <param name="KeyId">KMS key id/ARN/alias used to sign. Also emitted as the JWS <c>kid</c> header so a relying party can select the right verification key/alias.</param>
/// <param name="Algorithm">JWS algorithm — must match the KMS key's key spec (see <see cref="AssertionSigningAlgorithm"/>).</param>
/// <param name="Issuer">
/// The JWS <c>iss</c> claim stamped on every assertion this signer produces — an absolute URI
/// identifying the issuing product/environment (e.g. <c>https://api.biometrics.example.com</c>).
/// Required; validated as an absolute URI at construction (see <see cref="KmsAssertionSigner"/>).
/// This is the only source of <c>iss</c> — a caller-supplied <see cref="BiometricAssertion.Issuer"/>
/// is never used for signing, only for the verifier's parsed output.
/// </param>
public sealed record KmsAssertionSignerOptions(string KeyId, AssertionSigningAlgorithm Algorithm, string Issuer);
