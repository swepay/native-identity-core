using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Native.IdentityCore.Liveness;
using Native.IdentityCore.Tests.Fakers;
using NSubstitute;

namespace Native.IdentityCore.Tests.Liveness;

public class RekognitionLivenessSessionServiceTests
{
    // Rekognition's compile-time model validators require SessionId to look like a UUID even in
    // test fixtures — plain "session-1" literals fail the SDK's own Rekognition1002 analyzer.
    private const string SessionId1 = "11111111-1111-1111-1111-111111111111";
    private const string SessionId2 = "22222222-2222-2222-2222-222222222222";

    private static RekognitionLivenessSessionService CreateSut(IAmazonRekognition rekognition) =>
        new(rekognition, NullLogger<RekognitionLivenessSessionService>.Instance);

    [Fact]
    public async Task CreateSessionAsync_MinimalOptions_ForwardsKmsKeyIdAndToken()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.CreateFaceLivenessSessionAsync(Arg.Any<CreateFaceLivenessSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateFaceLivenessSessionResponse { SessionId = SessionId1 });
        var sut = CreateSut(rekognition);
        var options = new LivenessSessionOptions(KmsKeyId: "kms-key", ClientRequestToken: "token-1");

        // Act
        var handle = await sut.CreateSessionAsync(IdentityCoreFakers.NewTenantId(), options);

        // Assert
        handle.SessionId.ShouldBe(SessionId1);
        await rekognition.Received(1).CreateFaceLivenessSessionAsync(
            Arg.Is<CreateFaceLivenessSessionRequest>(r => r.KmsKeyId == "kms-key" && r.ClientRequestToken == "token-1" && r.Settings == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSessionAsync_WithOutputBucket_SetsSettings()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.CreateFaceLivenessSessionAsync(Arg.Any<CreateFaceLivenessSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateFaceLivenessSessionResponse { SessionId = SessionId2 });
        var sut = CreateSut(rekognition);
        var options = new LivenessSessionOptions(AuditImagesLimit: 2, OutputS3Bucket: "bucket", OutputS3KeyPrefix: "prefix/");

        // Act
        await sut.CreateSessionAsync(IdentityCoreFakers.NewTenantId(), options);

        // Assert
        await rekognition.Received(1).CreateFaceLivenessSessionAsync(
            Arg.Is<CreateFaceLivenessSessionRequest>(r =>
                r.Settings!.AuditImagesLimit == 2 &&
                r.Settings.OutputConfig!.S3Bucket == "bucket" &&
                r.Settings.OutputConfig.S3KeyPrefix == "prefix/"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CREATED", Native.IdentityCore.Liveness.LivenessSessionStatus.Created)]
    [InlineData("IN_PROGRESS", Native.IdentityCore.Liveness.LivenessSessionStatus.InProgress)]
    [InlineData("SUCCEEDED", Native.IdentityCore.Liveness.LivenessSessionStatus.Succeeded)]
    [InlineData("FAILED", Native.IdentityCore.Liveness.LivenessSessionStatus.Failed)]
    [InlineData("EXPIRED", Native.IdentityCore.Liveness.LivenessSessionStatus.Expired)]
    public async Task GetSessionResultAsync_MapsEveryKnownStatus(string rawStatus, Native.IdentityCore.Liveness.LivenessSessionStatus expected)
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.GetFaceLivenessSessionResultsAsync(Arg.Any<GetFaceLivenessSessionResultsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetFaceLivenessSessionResultsResponse { SessionId = SessionId1, Status = rawStatus });
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.GetSessionResultAsync(IdentityCoreFakers.NewTenantId(), SessionId1);

        // Assert
        result.Status.ShouldBe(expected);
    }

    [Fact]
    public async Task GetSessionResultAsync_UnknownRekognitionStatus_ThrowsNotSupportedException()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.GetFaceLivenessSessionResultsAsync(Arg.Any<GetFaceLivenessSessionResultsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetFaceLivenessSessionResultsResponse { SessionId = SessionId1, Status = "SOMETHING_NEW" });
        var sut = CreateSut(rekognition);

        // Act
        var act = async () => await sut.GetSessionResultAsync(IdentityCoreFakers.NewTenantId(), SessionId1);

        // Assert
        await Should.ThrowAsync<NotSupportedException>(act);
    }

    [Fact]
    public async Task GetSessionResultAsync_Succeeded_MapsConfidenceAndImages()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        var referenceBytes = IdentityCoreFakers.NewImageBytes();
        var auditBytes = IdentityCoreFakers.NewImageBytes();
        rekognition.GetFaceLivenessSessionResultsAsync(Arg.Any<GetFaceLivenessSessionResultsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetFaceLivenessSessionResultsResponse
            {
                SessionId = SessionId1,
                Status = "SUCCEEDED",
                Confidence = 97.5f,
                ReferenceImage = new AuditImage { Bytes = new MemoryStream(referenceBytes) },
                AuditImages = [new AuditImage { Bytes = new MemoryStream(auditBytes) }],
            });
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.GetSessionResultAsync(IdentityCoreFakers.NewTenantId(), SessionId1);

        // Assert
        result.Confidence.ShouldNotBeNull().ShouldBe(97.5d, tolerance: 0.001);
        result.ReferenceImage!.Value.ToArray().ShouldBe(referenceBytes);
        result.AuditImages.Count.ShouldBe(1);
        result.AuditImages[0].ToArray().ShouldBe(auditBytes);
        result.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public async Task GetSessionResultAsync_InProgress_IsNotFinal()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.GetFaceLivenessSessionResultsAsync(Arg.Any<GetFaceLivenessSessionResultsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetFaceLivenessSessionResultsResponse { SessionId = SessionId1, Status = "IN_PROGRESS" });
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.GetSessionResultAsync(IdentityCoreFakers.NewTenantId(), SessionId1);

        // Assert
        result.IsFinal.ShouldBeFalse();
        result.ReferenceImage.ShouldBeNull();
        result.AuditImages.ShouldBeEmpty();
    }
}
