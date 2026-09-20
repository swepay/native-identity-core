using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Native.IdentityCore.Assertions;
using Native.IdentityCore.Serialization;
using NSubstitute;

namespace Native.IdentityCore.Tests.Assertions;

/// <summary>
/// Verifies <see cref="KmsAssertionKeyPublisher"/> against a mocked <see cref="IAmazonKeyManagementService"/>
/// — <c>GetPublicKey</c> returns the same shape KMS actually returns (a DER
/// <c>SubjectPublicKeyInfo</c>), built here from a real in-memory ECDsa P-256 key so
/// <see cref="ECDsa.ImportSubjectPublicKeyInfo"/> is exercised exactly as it would be in production.
/// </summary>
public class KmsAssertionKeyPublisherTests
{
    private const string KeyId1 = "11111111-1111-1111-1111-111111111111";
    private const string KeyId2 = "22222222-2222-2222-2222-222222222222";

    private static readonly DateTimeOffset FixedNow = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static KmsAssertionKeyPublisherOptions Options(params string[] keyIds) => new(keyIds);

    private static TimeProvider FixedTimeProvider(DateTimeOffset now)
    {
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(now);
        return timeProvider;
    }

    private static GetPublicKeyResponse BuildKmsResponse(ECDsa key, string keyId) => new()
    {
        KeyId = keyId,
        KeySpec = KeySpec.ECC_NIST_P256,
        KeyUsage = KeyUsageType.SIGN_VERIFY,
        SigningAlgorithms = [SigningAlgorithmSpec.ECDSA_SHA_256],
        PublicKey = new MemoryStream(key.ExportSubjectPublicKeyInfo()),
    };

    private static KmsAssertionKeyPublisher CreateSut(IAmazonKeyManagementService kms, KmsAssertionKeyPublisherOptions options, TimeProvider? timeProvider = null) =>
        new(kms, options, timeProvider ?? FixedTimeProvider(FixedNow), NullLogger<KmsAssertionKeyPublisher>.Instance);

    [Fact]
    public async Task GetJwksAsync_SingleKey_BuildsExpectedJwk()
    {
        // Arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kms = Substitute.For<IAmazonKeyManagementService>();
        kms.GetPublicKeyAsync(Arg.Is<GetPublicKeyRequest>(r => r.KeyId == KeyId1), Arg.Any<CancellationToken>())
            .Returns(BuildKmsResponse(key, KeyId1));
        var sut = CreateSut(kms, Options(KeyId1));

        // Act
        var jwks = await sut.GetJwksAsync();

        // Assert
        jwks.Keys.Count.ShouldBe(1);
        var jwk = jwks.Keys[0];
        jwk.Kid.ShouldBe(KeyId1);
        jwk.Kty.ShouldBe("EC");
        jwk.Crv.ShouldBe("P-256");
        jwk.Alg.ShouldBe("ES256");
        jwk.Use.ShouldBe("sig");

        var expected = key.ExportParameters(includePrivateParameters: false);
        Base64UrlDecode(jwk.X!).ShouldBe(expected.Q.X);
        Base64UrlDecode(jwk.Y!).ShouldBe(expected.Q.Y);
    }

    [Fact]
    public async Task GetJwksAsync_MultipleKeyIds_PublishesEachInOrder()
    {
        // Arrange
        using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var previous = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kms = Substitute.For<IAmazonKeyManagementService>();
        kms.GetPublicKeyAsync(Arg.Is<GetPublicKeyRequest>(r => r.KeyId == KeyId1), Arg.Any<CancellationToken>())
            .Returns(BuildKmsResponse(current, KeyId1));
        kms.GetPublicKeyAsync(Arg.Is<GetPublicKeyRequest>(r => r.KeyId == KeyId2), Arg.Any<CancellationToken>())
            .Returns(BuildKmsResponse(previous, KeyId2));
        var sut = CreateSut(kms, Options(KeyId1, KeyId2));

        // Act
        var jwks = await sut.GetJwksAsync();

        // Assert
        jwks.Keys.Count.ShouldBe(2);
        jwks.Keys[0].Kid.ShouldBe(KeyId1);
        jwks.Keys[1].Kid.ShouldBe(KeyId2);
    }

    [Fact]
    public async Task GetJwksAsync_WithinCacheTtl_DoesNotCallKmsAgain()
    {
        // Arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kms = Substitute.For<IAmazonKeyManagementService>();
        kms.GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(BuildKmsResponse(key, KeyId1));
        var timeProvider = FixedTimeProvider(FixedNow);
        var sut = CreateSut(kms, Options(KeyId1), timeProvider);

        // Act
        await sut.GetJwksAsync();
        await sut.GetJwksAsync();

        // Assert
        await kms.Received(1).GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJwksAsync_AfterCacheTtlElapses_CallsKmsAgain()
    {
        // Arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kms = Substitute.For<IAmazonKeyManagementService>();
        kms.GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(BuildKmsResponse(key, KeyId1));
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(FixedNow, FixedNow.Add(TimeSpan.FromMinutes(16)));
        var sut = CreateSut(kms, Options(KeyId1) with { CacheTtl = TimeSpan.FromMinutes(15) }, timeProvider);

        // Act
        await sut.GetJwksAsync();
        await sut.GetJwksAsync();

        // Assert
        await kms.Received(2).GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJwksAsync_KeySpecIsNotEccNistP256_ThrowsNotSupportedException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();
        kms.GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetPublicKeyResponse { KeyId = KeyId1, KeySpec = KeySpec.RSA_2048, PublicKey = new MemoryStream([1, 2, 3]) });
        var sut = CreateSut(kms, Options(KeyId1));

        // Act
        var act = async () => await sut.GetJwksAsync();

        // Assert
        await Should.ThrowAsync<NotSupportedException>(act);
    }

    [Fact]
    public void Constructor_NullKms_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new KmsAssertionKeyPublisher(null!, Options(KeyId1), FixedTimeProvider(FixedNow), NullLogger<KmsAssertionKeyPublisher>.Instance);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionKeyPublisher(kms, Options(KeyId1), null!, NullLogger<KmsAssertionKeyPublisher>.Instance);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionKeyPublisher(kms, Options(KeyId1), FixedTimeProvider(FixedNow), null!);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionKeyPublisher(kms, null!, FixedTimeProvider(FixedNow), NullLogger<KmsAssertionKeyPublisher>.Instance);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_EmptyKeyIds_ThrowsArgumentException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionKeyPublisher(kms, Options(), FixedTimeProvider(FixedNow), NullLogger<KmsAssertionKeyPublisher>.Instance);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Constructor_KeyIdsContainsOnlyAnEmptyString_ThrowsArgumentException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionKeyPublisher(kms, Options(""), FixedTimeProvider(FixedNow), NullLogger<KmsAssertionKeyPublisher>.Instance);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Constructor_KeyIdsContainsAnEmptyStringAmongValidOnes_ThrowsArgumentException()
    {
        // Arrange
        var kms = Substitute.For<IAmazonKeyManagementService>();

        // Act
        var act = () => new KmsAssertionKeyPublisher(kms, Options(KeyId1, ""), FixedTimeProvider(FixedNow), NullLogger<KmsAssertionKeyPublisher>.Instance);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public async Task GetJwksAsync_PublishedDocument_LetsJwksAssertionVerifierVerifyASignatureFromTheSameKmsKey()
    {
        // Arrange — publisher side: KMS hands back the SubjectPublicKeyInfo for a real P-256 key.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kms = Substitute.For<IAmazonKeyManagementService>();
        kms.GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(BuildKmsResponse(key, KeyId1));
        var publisher = CreateSut(kms, Options(KeyId1));

        var jwks = await publisher.GetJwksAsync();
        var jwksBody = JsonSerializer.Serialize(jwks, IdentityCoreJsonSerializerContext.Default.JwksDocumentDto);

        // Arrange — verifier side: the JWKS endpoint serves exactly the body the publisher produced.
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jwksBody, Encoding.UTF8, "application/json"),
        });
        var verifier = new JwksAssertionVerifier(new HttpClient(handler), Substitute.For<ILogger<JwksAssertionVerifier>>());

        var issuedAt = FixedNow;
        var jws = BuildAssertion(key, KeyId1, tenantId: "realm-1", userRef: "user-1", issuer: "https://biometrics.example.test",
            audience: "realm-1", purpose: "Verification", decision: "Verified", iat: issuedAt, exp: issuedAt.AddMinutes(5));

        // Act — the same key that "signed" via KMS is the one whose public half the publisher
        // just served; if publisher and verifier disagree on JWKS shape, this fails.
        var result = await verifier.VerifyAsync(
            new AssertionVerifierOptions(
                JwksUrl: "https://biometrics.example.test/.well-known/jwks.json",
                ExpectedIssuer: "https://biometrics.example.test",
                ExpectedAudience: ["realm-1"],
                ExpectedPurpose: BiometricAssertionPurpose.Verification,
                MaxAssertionAge: TimeSpan.FromMinutes(5),
                ClockSkew: TimeSpan.Zero),
            jws,
            issuedAt.AddSeconds(1));

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Assertion!.TenantId.ShouldBe("realm-1");
    }

    private static string BuildAssertion(
        ECDsa key, string kid, string tenantId, string userRef, string issuer, string audience,
        string purpose, string decision, DateTimeOffset iat, DateTimeOffset exp)
    {
        var header = $$"""{"alg":"ES256","kid":"{{kid}}"}""";
        var payload = /*lang=json,strict*/ $$"""
            {"iss":"{{issuer}}","sub":"{{userRef}}","aud":"{{audience}}","tenant_id":"{{tenantId}}","user_ref":"{{userRef}}","purpose":"{{purpose}}","liveness_confidence":0.98,"face_similarity":0.95,"decision":"{{decision}}","iat":{{iat.ToUnixTimeSeconds()}},"exp":{{exp.ToUnixTimeSeconds()}},"jti":"{{Guid.NewGuid():N}}"}
            """;

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";
        var signature = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    /// <summary>Fake handler — always returns the same canned response, no real network I/O.</summary>
    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpResponseMessage(response.StatusCode);
            if (response.Content is not null)
            {
                var bytes = response.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(bytes);
                clone.Content.Headers.ContentType = response.Content.Headers.ContentType;
            }

            return Task.FromResult(clone);
        }
    }
}
