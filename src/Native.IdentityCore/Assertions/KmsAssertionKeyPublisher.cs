using System.Security.Cryptography;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Logging;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// <see cref="IAssertionKeyPublisher"/> implementation over <see cref="IAmazonKeyManagementService"/>
/// (<c>GetPublicKey</c>) — the issuer-side counterpart to <see cref="KmsAssertionSigner"/>. Builds
/// one <see cref="JwkDto"/> per configured key id from the DER-encoded <c>SubjectPublicKeyInfo</c>
/// KMS returns (<see cref="ECDsa.ImportSubjectPublicKeyInfo"/> — no manual ASN.1 parsing, no
/// reflection, AOT-safe), caches the resulting <see cref="JwksDocumentDto"/> in memory, and never
/// logs a key's coordinates (they are public by definition once published, but this library still
/// never writes cryptographic material to logs on principle).
/// </summary>
public sealed class KmsAssertionKeyPublisher : IAssertionKeyPublisher
{
    private const string Es256Kty = "EC";
    private const string Es256Crv = "P-256";
    private const string Es256Alg = "ES256";
    private const string SigUse = "sig";

    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(15);

    private readonly IAmazonKeyManagementService _kms;
    private readonly KmsAssertionKeyPublisherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KmsAssertionKeyPublisher> _logger;

    // Deliberately a plain field, not a lock/ConcurrentDictionary: a concurrent cache miss calls
    // GetPublicKey more than once for the same, idempotent, side-effect-free KMS read — harmless,
    // and simpler than JwksAssertionVerifier's per-URL cache (this publisher has exactly one
    // cache entry: "the current JWKS").
    private CachedJwks? _cached;

    public KmsAssertionKeyPublisher(
        IAmazonKeyManagementService kms,
        KmsAssertionKeyPublisherOptions options,
        TimeProvider timeProvider,
        ILogger<KmsAssertionKeyPublisher> logger)
    {
        _kms = kms ?? throw new ArgumentNullException(nameof(kms));
        _options = ValidateOptions(options);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<JwksDocumentDto> GetJwksAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var cacheTtl = _options.CacheTtl ?? DefaultCacheTtl;

        var cached = _cached;
        if (cached is not null && now - cached.FetchedAt < cacheTtl)
        {
            return cached.Document;
        }

        var keys = new List<JwkDto>(_options.KeyIds.Count);
        foreach (var keyId in _options.KeyIds)
        {
            keys.Add(await BuildJwkAsync(keyId, cancellationToken).ConfigureAwait(false));
        }

        var document = new JwksDocumentDto(keys);
        _cached = new CachedJwks(now, document);
        _logger.LogInformation("Refreshed issuer JWKS with {KeyCount} key(s).", keys.Count);
        return document;
    }

    private async Task<JwkDto> BuildJwkAsync(string keyId, CancellationToken cancellationToken)
    {
        var response = await _kms.GetPublicKeyAsync(new GetPublicKeyRequest { KeyId = keyId }, cancellationToken).ConfigureAwait(false);
        if (response.KeySpec != KeySpec.ECC_NIST_P256)
        {
            throw new NotSupportedException(
                $"KMS key '{keyId}' has KeySpec '{response.KeySpec}' — this publisher only supports ECC_NIST_P256 (ES256) keys today.");
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(response.PublicKey.ToArray(), out _);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

        // `kid` is stable and derived directly from the KMS key id — the same value
        // KmsAssertionSigner already stamps as the JWS `kid` header, so a token this key signed
        // resolves here with no extra mapping.
        return new JwkDto(
            Kty: Es256Kty,
            Kid: keyId,
            Crv: Es256Crv,
            X: Base64UrlEncode(parameters.Q.X!),
            Y: Base64UrlEncode(parameters.Q.Y!),
            Alg: Es256Alg,
            Use: SigUse);
    }

    private static KmsAssertionKeyPublisherOptions ValidateOptions(KmsAssertionKeyPublisherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.KeyIds is not { Count: > 0 } || options.KeyIds.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                "KeyIds must contain at least one non-empty KMS key id (the active signing key first, then any previous key ids kept for rotation).",
                nameof(options));
        }

        return options;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record CachedJwks(DateTimeOffset FetchedAt, JwksDocumentDto Document);
}
