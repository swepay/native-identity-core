using System.Text;
using System.Text.Json;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Native.IdentityCore.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// <see cref="IAssertionSigner"/> implementation over <see cref="IAmazonKeyManagementService"/>
/// (<c>Sign</c>, <c>MessageType=RAW</c>). Serializes header/payload with the source-generated
/// <see cref="IdentityCoreJsonSerializerContext"/> only — no reflection-based JSON path (GS-02).
/// Never logs the assertion payload (it carries biometric scores) or the signature.
/// </summary>
public sealed class KmsAssertionSigner(IAmazonKeyManagementService kms, KmsAssertionSignerOptions options) : IAssertionSigner
{
    private const int P256FieldSizeBytes = 32;

    private readonly IAmazonKeyManagementService _kms = kms ?? throw new ArgumentNullException(nameof(kms));
    private readonly KmsAssertionSignerOptions _options = ValidateOptions(options);

    public async ValueTask<string> SignAsync(BiometricAssertion assertion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        assertion.Validate();

        var header = new JwsHeader(Alg: AlgHeaderValue(_options.Algorithm), Typ: "biometric-assertion+jws", Kid: _options.KeyId);
        var payload = AssertionPayload.FromAssertion(assertion, _options.Issuer);

        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header, IdentityCoreJsonSerializerContext.Default.JwsHeader);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, IdentityCoreJsonSerializerContext.Default.AssertionPayload);

        var signingInput = $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}";
        var signature = await SignAsync(signingInput, cancellationToken).ConfigureAwait(false);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private async Task<byte[]> SignAsync(string signingInput, CancellationToken cancellationToken)
    {
        using var messageStream = new MemoryStream(Encoding.UTF8.GetBytes(signingInput));
        var response = await _kms.SignAsync(new SignRequest
        {
            KeyId = _options.KeyId,
            Message = messageStream,
            MessageType = MessageType.RAW,
            SigningAlgorithm = KmsAlgorithmSpec(_options.Algorithm),
        }, cancellationToken).ConfigureAwait(false);

        var der = response.Signature.ToArray();
        return _options.Algorithm switch
        {
            AssertionSigningAlgorithm.Es256 => EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes),
            // RSASSA-PKCS1-v1_5 signatures from KMS are already the raw JWS-ready signature bytes.
            AssertionSigningAlgorithm.Rs256 => der,
            _ => throw UnsupportedAlgorithm(_options.Algorithm),
        };
    }

    private static string AlgHeaderValue(AssertionSigningAlgorithm algorithm) => algorithm switch
    {
        AssertionSigningAlgorithm.Es256 => "ES256",
        AssertionSigningAlgorithm.Rs256 => "RS256",
        _ => throw UnsupportedAlgorithm(algorithm),
    };

    private static SigningAlgorithmSpec KmsAlgorithmSpec(AssertionSigningAlgorithm algorithm) => algorithm switch
    {
        AssertionSigningAlgorithm.Es256 => SigningAlgorithmSpec.ECDSA_SHA_256,
        AssertionSigningAlgorithm.Rs256 => SigningAlgorithmSpec.RSASSA_PKCS1_V1_5_SHA_256,
        _ => throw UnsupportedAlgorithm(algorithm),
    };

    private static KmsAssertionSignerOptions ValidateOptions(KmsAssertionSignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Uri.TryCreate(.., Absolute) accepts rooted paths on Unix ("/x" => file:///x), so the
        // scheme check is what actually enforces "an https issuer" on every platform.
        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer)
            || issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"Issuer must be an absolute https URI (got \"{options.Issuer}\"). Configure it, never hardcode a domain.",
                nameof(options));
        }

        return options;
    }

    private static ArgumentOutOfRangeException UnsupportedAlgorithm(AssertionSigningAlgorithm algorithm) =>
        new(nameof(algorithm), algorithm, "Unsupported signing algorithm.");

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
