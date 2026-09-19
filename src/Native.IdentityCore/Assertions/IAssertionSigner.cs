namespace Native.IdentityCore.Assertions;

/// <summary>Signs a <see cref="BiometricAssertion"/> into a compact JWS (<c>base64url(header).base64url(payload).base64url(signature)</c>).</summary>
public interface IAssertionSigner
{
    /// <summary>Validates <paramref name="assertion"/> and returns the compact JWS.</summary>
    ValueTask<string> SignAsync(BiometricAssertion assertion, CancellationToken cancellationToken = default);
}
