using Amazon.KeyManagementService;
using Amazon.Rekognition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Native.IdentityCore.Assertions;
using Native.IdentityCore.FaceIndex;
using Native.IdentityCore.Liveness;
using NSubstitute;

namespace Native.IdentityCore.Tests;

public class ServiceCollectionExtensionsTests
{
    private static IServiceCollection NewServices() => new ServiceCollection()
        .AddSingleton<ILogger<RekognitionLivenessSessionService>>(NullLogger<RekognitionLivenessSessionService>.Instance)
        .AddSingleton<ILogger<RekognitionFaceIndex>>(NullLogger<RekognitionFaceIndex>.Instance)
        .AddSingleton(Substitute.For<IAmazonRekognition>())
        .AddSingleton(Substitute.For<IAmazonKeyManagementService>());

    [Fact]
    public void AddNativeIdentityCoreLiveness_ResolvesRekognitionImplementation()
    {
        // Arrange
        var provider = NewServices().AddNativeIdentityCoreLiveness().BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<ILivenessSessionService>();

        // Assert
        service.ShouldBeOfType<RekognitionLivenessSessionService>();
    }

    [Fact]
    public void AddNativeIdentityCoreFaceIndex_ResolvesRekognitionImplementation()
    {
        // Arrange
        var options = new RekognitionFaceIndexOptions(Product: "kyc", Environment: "hml");
        var provider = NewServices().AddNativeIdentityCoreFaceIndex(options).BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<IFaceIndex>();

        // Assert
        service.ShouldBeOfType<RekognitionFaceIndex>();
    }

    [Fact]
    public void AddNativeIdentityCoreAssertionSigner_ResolvesKmsImplementation()
    {
        // Arrange
        var options = new KmsAssertionSignerOptions("kms-key", AssertionSigningAlgorithm.Es256, "https://biometrics.example.test");
        var provider = NewServices().AddNativeIdentityCoreAssertionSigner(options).BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<IAssertionSigner>();

        // Assert
        service.ShouldBeOfType<KmsAssertionSigner>();
    }

    [Fact]
    public void AddNativeIdentityCoreAssertionVerifier_ResolvesJwksImplementation()
    {
        // Arrange
        var provider = NewServices()
            .AddSingleton<ILogger<JwksAssertionVerifier>>(NullLogger<JwksAssertionVerifier>.Instance)
            .AddNativeIdentityCoreAssertionVerifier()
            .BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<IAssertionVerifier>();

        // Assert
        service.ShouldBeOfType<JwksAssertionVerifier>();
    }

    [Fact]
    public void AddNativeIdentityCoreFaceIndex_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => NewServices().AddNativeIdentityCoreFaceIndex(null!);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void AddNativeIdentityCoreAssertionSigner_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => NewServices().AddNativeIdentityCoreAssertionSigner(null!);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void AddNativeIdentityCoreAssertionKeyPublisher_ResolvesKmsImplementation()
    {
        // Arrange
        var options = new KmsAssertionKeyPublisherOptions(["kms-key"]);
        var provider = NewServices()
            .AddSingleton<ILogger<KmsAssertionKeyPublisher>>(NullLogger<KmsAssertionKeyPublisher>.Instance)
            .AddNativeIdentityCoreAssertionKeyPublisher(options)
            .BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<IAssertionKeyPublisher>();

        // Assert
        service.ShouldBeOfType<KmsAssertionKeyPublisher>();
        provider.GetRequiredService<TimeProvider>().ShouldBe(TimeProvider.System);
    }

    [Fact]
    public void AddNativeIdentityCoreAssertionKeyPublisher_CallerAlreadyRegisteredTimeProvider_DoesNotOverrideIt()
    {
        // Arrange
        var customTimeProvider = Substitute.For<TimeProvider>();
        var options = new KmsAssertionKeyPublisherOptions(["kms-key"]);
        var provider = NewServices()
            .AddSingleton<ILogger<KmsAssertionKeyPublisher>>(NullLogger<KmsAssertionKeyPublisher>.Instance)
            .AddSingleton(customTimeProvider)
            .AddNativeIdentityCoreAssertionKeyPublisher(options)
            .BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredService<TimeProvider>();

        // Assert
        resolved.ShouldBe(customTimeProvider);
    }

    [Fact]
    public void AddNativeIdentityCoreAssertionKeyPublisher_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => NewServices().AddNativeIdentityCoreAssertionKeyPublisher(null!);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }
}
