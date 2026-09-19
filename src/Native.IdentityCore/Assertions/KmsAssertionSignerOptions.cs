namespace Native.IdentityCore.Assertions;

/// <summary>Configuration for <see cref="KmsAssertionSigner"/>.</summary>
/// <param name="KeyId">KMS key id/ARN/alias used to sign. Also emitted as the JWS <c>kid</c> header so a relying party can select the right verification key/alias.</param>
/// <param name="Algorithm">JWS algorithm — must match the KMS key's key spec (see <see cref="AssertionSigningAlgorithm"/>).</param>
public sealed record KmsAssertionSignerOptions(string KeyId, AssertionSigningAlgorithm Algorithm);
