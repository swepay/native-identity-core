using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Native.IdentityCore.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// Verifies Native Biometrics-style assertions (compact JWS, ES256) by fetching and caching the
/// configured JWKS. Manual base64url + <see cref="ECDsa"/> — no external JWT library, no
/// reflection (AOT-safe), same technique NativeGuard/Passly already hand-roll in their own
/// verifier copies (ADR-0003 predates this lib's verifier; those repos are not migrated by this
/// change). A JWS ES256 signature is already the raw P1363 (R‖S) format
/// <see cref="ECDsa.VerifyData"/> expects, so no DER conversion is needed on the verify side.
/// </summary>
public sealed class JwksAssertionVerifier : IAssertionVerifier
{
    /// <summary>
    /// Default handler for production DI registration (see <c>AddNativeIdentityCoreAssertionVerifier</c>).
    /// The <see cref="HttpClient"/> itself is constructor-injected (not a private static field) so
    /// tests can substitute a fake <see cref="HttpMessageHandler"/> with no real network call.
    /// </summary>
    public static HttpClient CreateDefaultHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        MaxConnectionsPerServer = 10,
    })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan JwksCacheTtl = TimeSpan.FromMinutes(15);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CachedJwks> _jwksCache = new();
    private readonly ILogger<JwksAssertionVerifier> _logger;

    public JwksAssertionVerifier(HttpClient httpClient, ILogger<JwksAssertionVerifier> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<AssertionVerificationResult> VerifyAsync(
        AssertionVerifierOptions options,
        string assertionJws,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(assertionJws);

        var parts = assertionJws.Split('.');
        if (parts.Length != 3)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.Malformed);
        }

        JwsHeader? header;
        AssertionPayload? payload;
        byte[] signature;
        try
        {
            header = JsonSerializer.Deserialize(
                Base64UrlDecodeBytes(parts[0]), IdentityCoreJsonSerializerContext.Default.JwsHeader);
            payload = JsonSerializer.Deserialize(
                Base64UrlDecodeBytes(parts[1]), IdentityCoreJsonSerializerContext.Default.AssertionPayload);
            signature = Base64UrlDecodeBytes(parts[2]);
        }
        catch (FormatException)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.Malformed);
        }
        catch (JsonException)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.Malformed);
        }

        if (header is null || payload is null
            || !string.Equals(header.Alg, "ES256", StringComparison.Ordinal)
            || string.IsNullOrEmpty(header.Kid)
            || string.IsNullOrEmpty(payload.TenantId)
            || string.IsNullOrEmpty(payload.UserRef)
            || string.IsNullOrEmpty(payload.Jti)
            || payload.Audience.Count == 0)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.Malformed);
        }

        var jwks = await GetJwksAsync(options.JwksUrl, cancellationToken).ConfigureAwait(false);
        if (jwks is null)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.JwksUnavailable);
        }

        if (!jwks.KeysByKid.TryGetValue(header.Kid, out var publicKey))
        {
            // Key rotated since our cached copy — refresh once before giving up.
            jwks = await GetJwksAsync(options.JwksUrl, cancellationToken, forceRefresh: true).ConfigureAwait(false);
            if (jwks is null || !jwks.KeysByKid.TryGetValue(header.Kid, out publicKey))
            {
                return AssertionVerificationResult.Invalid(AssertionVerificationError.UnknownKey);
            }
        }

        var signedData = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        if (!publicKey.VerifyData(signedData, signature, HashAlgorithmName.SHA256))
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.InvalidSignature);
        }

        if (!string.Equals(payload.Issuer, options.ExpectedIssuer, StringComparison.Ordinal))
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.IssuerMismatch);
        }

        if (!payload.Audience.Any(candidate => options.ExpectedAudience.Contains(candidate, StringComparer.Ordinal)))
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.AudienceMismatch);
        }

        if (payload.Purpose != options.ExpectedPurpose)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.PurposeMismatch);
        }

        if (payload.Decision != Policy.BiometricOutcome.Verified)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.NotVerifiedDecision);
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnixSeconds);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds);

        if (now > expiresAt + options.ClockSkew)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.Expired);
        }

        if (issuedAt > now + options.ClockSkew)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.FutureIssuedAt);
        }

        if (now - issuedAt > options.MaxAssertionAge + options.ClockSkew)
        {
            return AssertionVerificationResult.Invalid(AssertionVerificationError.TooOld);
        }

        return AssertionVerificationResult.Valid(new BiometricAssertion(
            TenantId: payload.TenantId,
            UserRef: payload.UserRef,
            Purpose: payload.Purpose,
            LivenessConfidence: payload.LivenessConfidence,
            FaceSimilarity: payload.FaceSimilarity,
            Decision: payload.Decision,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            Jti: payload.Jti,
            Issuer: payload.Issuer,
            Audience: payload.Audience));
    }

    private async Task<CachedJwks?> GetJwksAsync(string jwksUrl, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (!forceRefresh
            && _jwksCache.TryGetValue(jwksUrl, out var cached)
            && DateTimeOffset.UtcNow - cached.FetchedAt < JwksCacheTtl)
        {
            return cached;
        }

        try
        {
            using var response = await _httpClient.GetAsync(jwksUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize(body, IdentityCoreJsonSerializerContext.Default.JwksDocumentDto);
            if (document is null)
            {
                return null;
            }

            var keysByKid = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
            foreach (var jwk in document.Keys)
            {
                if (!string.Equals(jwk.Kty, "EC", StringComparison.Ordinal)
                    || string.IsNullOrEmpty(jwk.Kid) || jwk.X is null || jwk.Y is null)
                {
                    continue; // Not a curve we support (ES256) — skip rather than fail the whole set.
                }

                keysByKid[jwk.Kid] = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = Base64UrlDecodeBytes(jwk.X), Y = Base64UrlDecodeBytes(jwk.Y) },
                });
            }

            var fresh = new CachedJwks(DateTimeOffset.UtcNow, keysByKid);
            _jwksCache[jwksUrl] = fresh;
            return fresh;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch JWKS from {JwksUrl}", jwksUrl);
            return null;
        }
    }

    private static byte[] Base64UrlDecodeBytes(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }

        return Convert.FromBase64String(output);
    }

    private sealed record CachedJwks(DateTimeOffset FetchedAt, Dictionary<string, ECDsa> KeysByKid);
}
