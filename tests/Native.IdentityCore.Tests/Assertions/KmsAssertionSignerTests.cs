using System.Text;
using System.Text.Json;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Native.IdentityCore.Assertions;
using Native.IdentityCore.Policy;
using Native.IdentityCore.Serialization;
using Native.IdentityCore.Tests.Fakers;
using NSubstitute;

namespace Native.IdentityCore.Tests.Assertions;

public class KmsAssertionSignerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static BiometricAssertion CreateAssertion() => new(
        TenantId: IdentityCoreFakers.NewTenantId(),
        UserRef: IdentityCoreFakers.NewUserRef(),
        Purpose: BiometricAssertionPurpose.Enrollment,
        LivenessConfidence: 96d,
        FaceSimilarity: 98d,
        Decision: BiometricOutcome.Verified,
        IssuedAt: IssuedAt,
        ExpiresAt: IssuedAt.AddMinutes(5),
        Jti: IdentityCoreFakers.NewJti());

    private static KmsAssertionSignerOptions Options(string keyId, AssertionSigningAlgorithm algorithm) =>
        new(keyId, algorithm, IdentityCoreFakers.TestIssuer);

    private static (string Header, string Payload, string Signature) SplitJws(string jws)
    {
        var parts = jws.Split('.');
        parts.Length.ShouldBe(3);
        return (parts[0], parts[1], parts[2]);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    [Fact]
    public async Task SignAsync_Es256_ProducesCompactJwsWithConvertedSignature()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();
        var der = BuildDerSequence(Enumerable.Repeat((byte)0x03, 32).ToArray(), Enumerable.Repeat((byte)0x04, 32).ToArray());
        SignRequest? captured = null;
        byte[]? capturedMessageBytes = null;
        kms.SignAsync(Arg.Do<SignRequest>(r =>
            {
                captured = r;
                capturedMessageBytes = r.Message.ToArray();
            }), Arg.Any<CancellationToken>())
            .Returns(new SignResponse { Signature = new MemoryStream(der), KeyId = "kms-key" });
        var sut = new KmsAssertionSigner(kms, Options("kms-key", AssertionSigningAlgorithm.Es256));
        var assertion = CreateAssertion();

        // Act
        var jws = await sut.SignAsync(assertion);

        // Assert
        var (headerB64, payloadB64, signatureB64) = SplitJws(jws);
        var header = JsonSerializer.Deserialize(Base64UrlDecode(headerB64), IdentityCoreJsonSerializerContext.Default.JwsHeader);
        header!.Alg.ShouldBe("ES256");
        header.Kid.ShouldBe("kms-key");

        var payload = JsonSerializer.Deserialize(Base64UrlDecode(payloadB64), IdentityCoreJsonSerializerContext.Default.AssertionPayload);
        payload!.TenantId.ShouldBe(assertion.TenantId);
        payload.UserRef.ShouldBe(assertion.UserRef);
        payload.Decision.ShouldBe(BiometricOutcome.Verified);
        payload.IssuedAtUnixSeconds.ShouldBe(assertion.IssuedAt.ToUnixTimeSeconds());
        payload.Issuer.ShouldBe(IdentityCoreFakers.TestIssuer);
        payload.Subject.ShouldBe(assertion.UserRef);
        payload.Jti.ShouldBe(assertion.Jti);
        payload.Audience.ShouldBe([assertion.TenantId]); // default audience strategy: no explicit Audience -> [TenantId]

        var expectedSignature = EcdsaSignatureConverter.DerToJose(der, 32);
        Base64UrlDecode(signatureB64).ShouldBe(expectedSignature);

        captured.ShouldNotBeNull();
        captured!.KeyId.ShouldBe("kms-key");
        captured.MessageType.ShouldBe(MessageType.RAW);
        captured.SigningAlgorithm.ShouldBe(SigningAlgorithmSpec.ECDSA_SHA_256);
        Encoding.UTF8.GetString(capturedMessageBytes.ShouldNotBeNull()).ShouldBe($"{headerB64}.{payloadB64}");
    }

    [Fact]
    public async Task SignAsync_Rs256_PassesKmsSignatureThroughUnchanged()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();
        var rawSignature = IdentityCoreFakers.NewImageBytes(256);
        kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignResponse { Signature = new MemoryStream(rawSignature), KeyId = "rsa-key" });
        var sut = new KmsAssertionSigner(kms, Options("rsa-key", AssertionSigningAlgorithm.Rs256));

        // Act
        var jws = await sut.SignAsync(CreateAssertion());

        // Assert
        var (headerB64, _, signatureB64) = SplitJws(jws);
        var header = JsonSerializer.Deserialize(Base64UrlDecode(headerB64), IdentityCoreJsonSerializerContext.Default.JwsHeader);
        header!.Alg.ShouldBe("RS256");
        Base64UrlDecode(signatureB64).ShouldBe(rawSignature);

        await kms.Received(1).SignAsync(
            Arg.Is<SignRequest>(r => r.SigningAlgorithm == SigningAlgorithmSpec.RSASSA_PKCS1_V1_5_SHA_256),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignAsync_InvalidAssertion_ThrowsWithoutCallingKms()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();
        var sut = new KmsAssertionSigner(kms, Options("kms-key", AssertionSigningAlgorithm.Es256));
        var invalid = CreateAssertion() with { TenantId = string.Empty };

        // Act
        var act = async () => await sut.SignAsync(invalid);

        // Assert
        await Should.ThrowAsync<ArgumentException>(act);
        await kms.DidNotReceive().SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_NullKms_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => new KmsAssertionSigner(null!, Options("kms-key", AssertionSigningAlgorithm.Es256));

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionSigner(kms, null!);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Theory]
    [InlineData("native-biometrics")] // slug, not a URI — the pre-existing default this option must reject
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("/relative/path")]
    public void Constructor_IssuerNotAnAbsoluteUri_ThrowsArgumentException(string invalidIssuer)
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionSigner(kms, new KmsAssertionSignerOptions("kms-key", AssertionSigningAlgorithm.Es256, invalidIssuer));

        // Assert
        Should.Throw<ArgumentException>(act);
        kms.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SignAsync_WithExplicitAudience_OverridesTheDefaultTenantIdAudience()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();
        var der = BuildDerSequence(Enumerable.Repeat((byte)0x03, 32).ToArray(), Enumerable.Repeat((byte)0x04, 32).ToArray());
        kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignResponse { Signature = new MemoryStream(der), KeyId = "kms-key" });
        var sut = new KmsAssertionSigner(kms, Options("kms-key", AssertionSigningAlgorithm.Es256));
        var assertion = CreateAssertion() with { Audience = ["urn:swepay:passly:project-42"] };

        // Act
        var jws = await sut.SignAsync(assertion);

        // Assert
        var (_, payloadB64, _) = SplitJws(jws);
        var payload = JsonSerializer.Deserialize(Base64UrlDecode(payloadB64), IdentityCoreJsonSerializerContext.Default.AssertionPayload);
        payload!.Audience.ShouldBe(["urn:swepay:passly:project-42"]);
    }

    private static byte[] BuildDerSequence(byte[] r, byte[] s)
    {
        var rEncoded = EncodeInteger(r);
        var sEncoded = EncodeInteger(s);
        var content = rEncoded.Concat(sEncoded).ToArray();
        return new byte[] { 0x30, checked((byte)content.Length) }.Concat(content).ToArray();
    }

    private static byte[] EncodeInteger(byte[] value)
    {
        var needsPad = value.Length > 0 && (value[0] & 0x80) != 0;
        var content = needsPad ? new byte[] { 0x00 }.Concat(value).ToArray() : value;
        return new byte[] { 0x02, checked((byte)content.Length) }.Concat(content).ToArray();
    }
}
