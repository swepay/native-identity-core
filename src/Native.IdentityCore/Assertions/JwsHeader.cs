using System.Text.Json.Serialization;

namespace Native.IdentityCore.Assertions;

/// <summary>JOSE header for the compact JWS produced by <see cref="KmsAssertionSigner"/>.</summary>
public sealed record JwsHeader(
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("typ")] string Typ,
    [property: JsonPropertyName("kid")] string Kid);
