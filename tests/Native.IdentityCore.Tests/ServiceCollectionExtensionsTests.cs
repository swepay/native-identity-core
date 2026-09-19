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
        var options = new KmsAssertionSignerOptions("kms-key", AssertionSigningAlgorithm.Es256);
        var provider = NewServices().AddNativeIdentityCoreAssertionSigner(options).BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<IAssertionSigner>();

        // Assert
        service.ShouldBeOfType<KmsAssertionSigner>();
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
}
