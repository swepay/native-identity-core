namespace Native.IdentityCore.Assertions;

/// <summary>JWS algorithms <see cref="KmsAssertionSigner"/> supports. Both map to a specific KMS asymmetric key spec/signing algorithm — the KMS key must be created with a matching key spec.</summary>
public enum AssertionSigningAlgorithm
{
    /// <summary>ECDSA P-256 / SHA-256. KMS key spec <c>ECC_NIST_P256</c>.</summary>
    Es256,

    /// <summary>RSASSA-PKCS1-v1_5 / SHA-256. KMS key spec <c>RSA_2048</c> or larger.</summary>
    Rs256,
}
