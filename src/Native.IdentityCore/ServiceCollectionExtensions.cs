using Amazon.KeyManagementService;
using Amazon.Rekognition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Native.IdentityCore.Assertions;
using Native.IdentityCore.FaceIndex;
using Native.IdentityCore.Liveness;

namespace Native.IdentityCore;

/// <summary>
/// Explicit DI registration for this library's services — no assembly scanning, matching the
/// ecosystem's <c>[Register]</c>/source-generator convention of registering exactly what is
/// requested (GS-03). Callers still register <see cref="IAmazonRekognition"/> and
/// <see cref="IAmazonKeyManagementService"/> themselves (typically via
/// <c>Native.SourceGenerator.DI</c> in a consuming backend); this library only wires its own
/// abstractions on top of them.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ILivenessSessionService"/> backed by Rekognition. Requires <see cref="IAmazonRekognition"/> to already be registered.</summary>
    public static IServiceCollection AddNativeIdentityCoreLiveness(this IServiceCollection services) =>
        services.AddSingleton<ILivenessSessionService, RekognitionLivenessSessionService>();

    /// <summary>Registers <see cref="IFaceIndex"/> backed by Rekognition. Requires <see cref="IAmazonRekognition"/> to already be registered.</summary>
    public static IServiceCollection AddNativeIdentityCoreFaceIndex(this IServiceCollection services, RekognitionFaceIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services
            .AddSingleton(options)
            .AddSingleton<IFaceIndex, RekognitionFaceIndex>();
    }

    /// <summary>Registers <see cref="IAssertionSigner"/> backed by KMS. Requires <see cref="IAmazonKeyManagementService"/> to already be registered.</summary>
    public static IServiceCollection AddNativeIdentityCoreAssertionSigner(this IServiceCollection services, KmsAssertionSignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services
            .AddSingleton(options)
            .AddSingleton<IAssertionSigner, KmsAssertionSigner>();
    }

    /// <summary>
    /// Registers <see cref="IAssertionVerifier"/> backed by JWKS fetch/cache — no KMS, no
    /// reflection. Registers its own <see cref="HttpClient"/> (pooled <see cref="SocketsHttpHandler"/>,
    /// same convention as the ecosystem's other JWKS verifiers); callers that already manage an
    /// <see cref="HttpClient"/> for this purpose can instead register <see cref="JwksAssertionVerifier"/>
    /// directly with their own instance.
    /// </summary>
    public static IServiceCollection AddNativeIdentityCoreAssertionVerifier(this IServiceCollection services) =>
        services
            .AddSingleton(_ => JwksAssertionVerifier.CreateDefaultHttpClient())
            .AddSingleton<IAssertionVerifier, JwksAssertionVerifier>();

    /// <summary>
    /// Registers <see cref="IAssertionKeyPublisher"/> backed by KMS <c>GetPublicKey</c> — the
    /// issuer-side counterpart to <see cref="AddNativeIdentityCoreAssertionSigner"/>. Requires
    /// <see cref="IAmazonKeyManagementService"/> to already be registered; registers
    /// <see cref="TimeProvider.System"/> only if the caller has not already registered a
    /// <see cref="TimeProvider"/> of its own (e.g. for testing).
    /// </summary>
    public static IServiceCollection AddNativeIdentityCoreAssertionKeyPublisher(this IServiceCollection services, KmsAssertionKeyPublisherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddSingleton(options)
            .AddSingleton<IAssertionKeyPublisher, KmsAssertionKeyPublisher>();
    }
}
