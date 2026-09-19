using System.Text.Json.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>A single JSON Web Key as returned by a Native Biometrics-style <c>/.well-known/jwks.json</c>.</summary>
public sealed record JwkDto(
    [property: JsonPropertyName("kty")] string? Kty,
    [property: JsonPropertyName("kid")] string? Kid,
    [property: JsonPropertyName("crv")] string? Crv,
    [property: JsonPropertyName("x")] string? X,
    [property: JsonPropertyName("y")] string? Y);

/// <summary>The JWKS document (RFC 7517) fetched from <see cref="AssertionVerifierOptions.JwksUrl"/>.</summary>
public sealed record JwksDocumentDto(
    [property: JsonPropertyName("keys")] List<JwkDto> Keys);
