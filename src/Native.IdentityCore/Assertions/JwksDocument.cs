using System.Text.Json.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>
/// A single JSON Web Key as returned by a Native Biometrics-style <c>/.well-known/jwks.json</c>.
/// <see cref="JwksAssertionVerifier"/> only reads <see cref="Kty"/>/<see cref="Kid"/>/
/// <see cref="Crv"/>/<see cref="X"/>/<see cref="Y"/>; <see cref="Alg"/>/<see cref="Use"/> are
/// populated by <see cref="KmsAssertionKeyPublisher"/> (RFC 7517 §4.4/§4.2) for a relying party
/// that wants them, but are optional metadata this library itself never validates.
/// </summary>
public sealed record JwkDto(
    [property: JsonPropertyName("kty")] string? Kty,
    [property: JsonPropertyName("kid")] string? Kid,
    [property: JsonPropertyName("crv")] string? Crv,
    [property: JsonPropertyName("x")] string? X,
    [property: JsonPropertyName("y")] string? Y,
    [property: JsonPropertyName("alg")] string? Alg = null,
    [property: JsonPropertyName("use")] string? Use = null);

/// <summary>The JWKS document (RFC 7517) fetched from <see cref="AssertionVerifierOptions.JwksUrl"/>.</summary>
public sealed record JwksDocumentDto(
    [property: JsonPropertyName("keys")] List<JwkDto> Keys);
