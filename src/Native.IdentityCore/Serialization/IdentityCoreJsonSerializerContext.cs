using System.Text.Json.Serialization;
using Native.IdentityCore.Assertions;

namespace Native.IdentityCore.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for every type this library serializes.
/// No <see cref="System.Text.Json.JsonSerializer"/> call in this library resolves a type any
/// other way (GS-02) — reflection-based serialization is never used, even as a fallback.
/// </summary>
[JsonSerializable(typeof(JwsHeader))]
[JsonSerializable(typeof(AssertionPayload))]
[JsonSerializable(typeof(JwkDto))]
[JsonSerializable(typeof(JwksDocumentDto))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class IdentityCoreJsonSerializerContext : JsonSerializerContext;
