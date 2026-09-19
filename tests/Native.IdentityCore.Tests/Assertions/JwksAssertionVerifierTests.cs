using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Native.IdentityCore.Assertions;
using Native.IdentityCore.Policy;
using NSubstitute;

namespace Native.IdentityCore.Tests.Assertions;

/// <summary>
/// Verifies <see cref="JwksAssertionVerifier"/> end-to-end against an in-memory ES256 key pair and
/// a fake <see cref="HttpMessageHandler"/> — no KMS, no real network call.
/// </summary>
public class JwksAssertionVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private const string TestJwksUrl = "https://biometrics.example.test/.well-known/jwks.json";
    private const string TestKid = "biometrics-key-1";
    private const string ExpectedIssuer = "https://biometrics.example.test";
    private const string ExpectedAudience = "urn:swepay:guard:realm-1";

    private static AssertionVerifierOptions Options(
        BiometricAssertionPurpose expectedPurpose = BiometricAssertionPurpose.Recovery,
        int maxAssertionAgeSeconds = 300,
        int clockSkewSeconds = 0) => new(
            JwksUrl: TestJwksUrl,
            ExpectedIssuer: ExpectedIssuer,
            ExpectedAudience: [ExpectedAudience],
            ExpectedPurpose: expectedPurpose,
            MaxAssertionAge: TimeSpan.FromSeconds(maxAssertionAgeSeconds),
            ClockSkew: TimeSpan.FromSeconds(clockSkewSeconds));

    private static JwksAssertionVerifier CreateVerifier(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Substitute.For<ILogger<JwksAssertionVerifier>>());

    [Fact]
    public async Task VerifyAsync_WithValidSignatureAndFreshClaims_ShouldReturnValidAssertion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.IsValid.ShouldBeTrue();
        result.Assertion!.TenantId.ShouldBe("realm-1");
        result.Assertion.UserRef.ShouldBe("user-1");
        result.Assertion.Purpose.ShouldBe(BiometricAssertionPurpose.Recovery);
        result.Assertion.Decision.ShouldBe(BiometricOutcome.Verified);
        result.Assertion.Issuer.ShouldBe(ExpectedIssuer);
        result.Assertion.Audience.ShouldBe([ExpectedAudience]);
        result.Assertion.Jti.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task VerifyAsync_WhenPayloadWasTamperedAfterSigning_ShouldReturnInvalidSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);

        var parts = jws.Split('.');
        var tamperedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            Base64UrlDecodeToString(parts[1]).Replace("realm-1", "evil-realm", StringComparison.Ordinal)));
        var tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), tampered, Now);

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe(AssertionVerificationError.InvalidSignature);
    }

    [Fact]
    public async Task VerifyAsync_WithUnknownKeyId_ShouldRefreshOnceThenReturnUnknownKey()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(signingKey, "kid-not-in-jwks", tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);

        var handler = new StubHttpMessageHandler(BuildJwksResponse(otherKey, "some-other-kid"));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe(AssertionVerificationError.UnknownKey);
        handler.RequestCount.ShouldBe(2); // initial fetch + forced refresh before giving up
    }

    [Fact]
    public async Task VerifyAsync_WhenIssuerDoesNotMatch_ShouldReturnIssuerMismatch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: "https://someone-else.example.test", audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.IssuerMismatch);
    }

    [Fact]
    public async Task VerifyAsync_WhenAudienceDoesNotMatchAnyExpectedValue_ShouldReturnAudienceMismatch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: "urn:swepay:guard:some-other-realm");
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.AudienceMismatch);
    }

    [Fact]
    public async Task VerifyAsync_WhenAudienceIsAnArrayContainingAnExpectedValue_ShouldReturnValid()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audiences: ["urn:swepay:guard:some-other-realm", ExpectedAudience]);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.IsValid.ShouldBeTrue();
        result.Assertion!.Audience.ShouldBe(["urn:swepay:guard:some-other-realm", ExpectedAudience]);
    }

    [Fact]
    public async Task VerifyAsync_WhenPurposeDoesNotMatchExpected_ShouldReturnPurposeMismatch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Challenge", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(expectedPurpose: BiometricAssertionPurpose.Recovery), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.PurposeMismatch);
    }

    [Theory]
    [InlineData("Retry")]
    [InlineData("Rejected")]
    public async Task VerifyAsync_WhenDecisionIsNotVerified_ShouldReturnNotVerifiedDecision(string decision)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: decision, iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.NotVerifiedDecision);
    }

    [Fact]
    public async Task VerifyAsync_WhenAssertionExpired_ShouldReturnExpired()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddMinutes(-10), exp: Now.AddMinutes(-1),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.Expired);
    }

    [Fact]
    public async Task VerifyAsync_WhenExpiredButWithinClockSkew_ShouldReturnValid()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-30), exp: Now.AddSeconds(-5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(clockSkewSeconds: 30), jws, Now);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WhenIssuedAtIsInTheFuture_ShouldReturnFutureIssuedAt()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddMinutes(5), exp: Now.AddMinutes(10),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.FutureIssuedAt);
    }

    [Fact]
    public async Task VerifyAsync_WhenIssuedAtOlderThanMaxAssertionAge_ShouldReturnTooOld()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddMinutes(-30), exp: Now.AddMinutes(30),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(maxAssertionAgeSeconds: 300), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.TooOld);
    }

    [Theory]
    [InlineData("not-a-jws")]
    [InlineData("only.two-parts")]
    public async Task VerifyAsync_WithMalformedJws_ShouldReturnMalformed(string malformed)
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), malformed, Now);

        result.Error.ShouldBe(AssertionVerificationError.Malformed);
    }

    [Fact]
    public async Task VerifyAsync_WithNonBase64HeaderSegment_ShouldReturnMalformed()
    {
        var jws = "not!base64url.eyJhIjoxfQ.c2ln";
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.Malformed);
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task VerifyAsync_WithNonEs256Algorithm_ShouldReturnMalformed()
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"alg":"RS256","kid":"k1"}"""));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"tenant_id":"realm-1"}"""));
        var jws = $"{header}.{payload}.c2ln";
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.Malformed);
    }

    [Fact]
    public async Task VerifyAsync_WithMissingKid_ShouldReturnMalformed()
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"alg":"ES256"}"""));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"tenant_id":"realm-1"}"""));
        var jws = $"{header}.{payload}.c2ln";
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.Malformed);
    }

    [Fact]
    public async Task VerifyAsync_WithMissingRequiredClaim_ShouldReturnMalformed()
    {
        // Well-formed header + ES256/kid, but payload omits jti — required for the caller's
        // replay guard, so treat it the same as any other malformed payload.
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"alg":"ES256","kid":"k1"}"""));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            /*lang=json,strict*/ """{"iss":"https://biometrics.example.test","aud":"realm-1","tenant_id":"realm-1","user_ref":"user-1","purpose":"Recovery","decision":"Verified"}"""));
        var jws = $"{header}.{payload}.c2ln";
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.Malformed);
    }

    [Fact]
    public async Task VerifyAsync_WhenJwksBodyIsMalformedJson_ShouldReturnJwksUnavailable()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-valid-json", Encoding.UTF8, "application/json"),
        });
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.JwksUnavailable);
    }

    [Fact]
    public async Task VerifyAsync_WhenJwksEndpointIsUnavailable_ShouldReturnJwksUnavailable()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jws = BuildAssertion(key, TestKid, tenantId: "realm-1", userRef: "user-1",
            purpose: "Recovery", decision: "Verified", iat: Now.AddSeconds(-5), exp: Now.AddMinutes(5),
            issuer: ExpectedIssuer, audience: ExpectedAudience);
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var verifier = CreateVerifier(handler);

        var result = await verifier.VerifyAsync(Options(), jws, Now);

        result.Error.ShouldBe(AssertionVerificationError.JwksUnavailable);
    }

    [Fact]
    public async Task VerifyAsync_ForTheSameJwksUrl_ShouldCacheAcrossCalls()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new StubHttpMessageHandler(BuildJwksResponse(key, TestKid));
        var verifier = CreateVerifier(handler);
        var options = Options();

        var first = BuildAssertion(key, TestKid, "realm-1", "user-1", "Recovery", "Verified", Now.AddSeconds(-5), Now.AddMinutes(5), ExpectedIssuer, ExpectedAudience);
        var second = BuildAssertion(key, TestKid, "realm-1", "user-2", "Recovery", "Verified", Now.AddSeconds(-5), Now.AddMinutes(5), ExpectedIssuer, ExpectedAudience);
        await verifier.VerifyAsync(options, first, Now);
        await verifier.VerifyAsync(options, second, Now);

        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public void VerifyAsync_NullOptions_ThrowsArgumentNullException()
    {
        var verifier = CreateVerifier(new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var act = async () => await verifier.VerifyAsync(null!, "a.b.c", Now);

        Should.ThrowAsync<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new JwksAssertionVerifier(null!, Substitute.For<ILogger<JwksAssertionVerifier>>());

        Should.Throw<ArgumentNullException>(act);
    }

    // Built by hand (no JsonSerializer.Serialize<T> of an anonymous type) — this test project has
    // the repo's trim/AOT analyzers enabled (Directory.Build.props) and reflection-based
    // serialization trips IL2026/IL3050 even though the test binary itself never trims/AOT-publishes.
    private static HttpResponseMessage BuildJwksResponse(ECDsa key, string kid)
    {
        var parameters = key.ExportParameters(false);
        var body = /*lang=json,strict*/ $$"""
            {"keys":[{"kty":"EC","crv":"P-256","kid":"{{kid}}","x":"{{Base64UrlEncode(parameters.Q.X!)}}","y":"{{Base64UrlEncode(parameters.Q.Y!)}}"}]}
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static string BuildAssertion(
        ECDsa key, string kid, string tenantId, string userRef, string purpose, string decision,
        DateTimeOffset iat, DateTimeOffset exp, string issuer, string audience) =>
        BuildAssertion(key, kid, tenantId, userRef, purpose, decision, iat, exp, issuer, audiences: [audience]);

    private static string BuildAssertion(
        ECDsa key, string kid, string tenantId, string userRef, string purpose, string decision,
        DateTimeOffset iat, DateTimeOffset exp, string issuer, string[] audiences)
    {
        var header = $$"""{"alg":"ES256","kid":"{{kid}}"}""";
        var audienceJson = audiences.Length == 1
            ? $"\"{audiences[0]}\""
            : $"[{string.Join(",", audiences.Select(a => $"\"{a}\""))}]";
        var payload = /*lang=json,strict*/ $$"""
            {"iss":"{{issuer}}","sub":"{{userRef}}","aud":{{audienceJson}},"tenant_id":"{{tenantId}}","user_ref":"{{userRef}}","purpose":"{{purpose}}","liveness_confidence":0.98,"face_similarity":0.95,"decision":"{{decision}}","iat":{{iat.ToUnixTimeSeconds()}},"exp":{{exp.ToUnixTimeSeconds()}},"jti":"{{Guid.NewGuid():N}}"}
            """;

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";
        var signature = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlDecodeToString(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    /// <summary>Fake handler — always returns the same canned response, no real network I/O.</summary>
    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
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
