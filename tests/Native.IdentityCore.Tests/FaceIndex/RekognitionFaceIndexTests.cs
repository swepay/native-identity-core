using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Native.IdentityCore.FaceIndex;
using Native.IdentityCore.Tests.Fakers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Native.IdentityCore.Tests.FaceIndex;

public class RekognitionFaceIndexTests
{
    // Rekognition's compile-time model validators require FaceId to look like a UUID even in
    // test fixtures — plain "face-1" literals fail the SDK's own Rekognition1002 analyzer.
    private const string FaceId1 = "11111111-1111-1111-1111-111111111111";
    private const string FaceIdLow = "22222222-2222-2222-2222-222222222222";
    private const string FaceIdHigh = "33333333-3333-3333-3333-333333333333";
    private const string FaceIdMine = "44444444-4444-4444-4444-444444444444";
    private const string FaceIdOther = "55555555-5555-5555-5555-555555555555";
    private const string FaceIdMine2 = "66666666-6666-6666-6666-666666666666";

    private static readonly RekognitionFaceIndexOptions Options = new(Product: "kyc", Environment: "hml");

    private static RekognitionFaceIndex CreateSut(IAmazonRekognition rekognition) =>
        new(rekognition, Options, NullLogger<RekognitionFaceIndex>.Instance);

    [Fact]
    public async Task EnsureCollectionAsync_CollectionDoesNotExist_CreatesIt()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        var tenantId = IdentityCoreFakers.NewTenantId();
        var sut = CreateSut(rekognition);

        // Act
        await sut.EnsureCollectionAsync(tenantId);

        // Assert
        await rekognition.Received(1).CreateCollectionAsync(
            Arg.Is<CreateCollectionRequest>(r => r.CollectionId == Options.BuildCollectionId(tenantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureCollectionAsync_CollectionAlreadyExists_DoesNotThrow()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.CreateCollectionAsync(Arg.Any<CreateCollectionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceAlreadyExistsException("already exists"));
        var sut = CreateSut(rekognition);

        // Act
        var act = async () => await sut.EnsureCollectionAsync(IdentityCoreFakers.NewTenantId());

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task IndexAsync_FaceDetected_ReturnsFaceIndexResult()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        var tenantId = IdentityCoreFakers.NewTenantId();
        var userRef = IdentityCoreFakers.NewUserRef();
        rekognition.IndexFacesAsync(Arg.Any<IndexFacesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IndexFacesResponse
            {
                FaceRecords =
                [
                    new FaceRecord { Face = new Face { FaceId = FaceId1, Confidence = 99.5f } },
                ],
            });
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.IndexAsync(tenantId, userRef, IdentityCoreFakers.NewImageBytes());

        // Assert
        result.FaceId.ShouldBe(FaceId1);
        result.UserRef.ShouldBe(userRef);
        result.Confidence.ShouldBe(99.5d, tolerance: 0.001);
    }

    [Fact]
    public async Task IndexAsync_NoFaceDetected_ThrowsFaceIndexException()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.IndexFacesAsync(Arg.Any<IndexFacesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IndexFacesResponse
            {
                FaceRecords = [],
                UnindexedFaces = [new UnindexedFace { Reasons = ["LOW_CONFIDENCE"] }],
            });
        var sut = CreateSut(rekognition);

        // Act
        var act = async () => await sut.IndexAsync(IdentityCoreFakers.NewTenantId(), IdentityCoreFakers.NewUserRef(), IdentityCoreFakers.NewImageBytes());

        // Assert
        var exception = await Should.ThrowAsync<FaceIndexException>(act);
        exception.Message.ShouldContain("LOW_CONFIDENCE");
    }

    [Fact]
    public async Task SearchAsync_MatchesFound_ReturnsBestMatchFirst()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.SearchFacesByImageAsync(Arg.Any<SearchFacesByImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SearchFacesByImageResponse
            {
                FaceMatches =
                [
                    new FaceMatch { Similarity = 80f, Face = new Face { FaceId = FaceIdLow, ExternalImageId = "user-low" } },
                    new FaceMatch { Similarity = 99f, Face = new Face { FaceId = FaceIdHigh, ExternalImageId = "user-high" } },
                ],
            });
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.SearchAsync(IdentityCoreFakers.NewTenantId(), IdentityCoreFakers.NewImageBytes());

        // Assert
        result.Found.ShouldBeTrue();
        result.BestMatch.ShouldNotBeNull();
        result.BestMatch!.UserRef.ShouldBe("user-high");
        result.Matches.Count.ShouldBe(2);
        result.Matches[0].UserRef.ShouldBe("user-high");
    }

    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsNoMatchResult()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.SearchFacesByImageAsync(Arg.Any<SearchFacesByImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SearchFacesByImageResponse { FaceMatches = [] });
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.SearchAsync(IdentityCoreFakers.NewTenantId(), IdentityCoreFakers.NewImageBytes());

        // Assert
        result.ShouldBe(FaceSearchResult.NoMatch);
    }

    [Fact]
    public async Task SearchAsync_NoFaceDetectedInProbeImage_ReturnsNoMatchResult()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.SearchFacesByImageAsync(Arg.Any<SearchFacesByImageRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidParameterException("no face detected"));
        var sut = CreateSut(rekognition);

        // Act
        var result = await sut.SearchAsync(IdentityCoreFakers.NewTenantId(), IdentityCoreFakers.NewImageBytes());

        // Assert
        result.ShouldBe(FaceSearchResult.NoMatch);
    }

    [Fact]
    public async Task DeleteAsync_MatchingFacesAcrossPages_DeletesOnlyMatchingFaceIds()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        var userRef = IdentityCoreFakers.NewUserRef();

        rekognition.ListFacesAsync(Arg.Is<ListFacesRequest>(r => r.NextToken == null), Arg.Any<CancellationToken>())
            .Returns(new ListFacesResponse
            {
                Faces = [new Face { FaceId = FaceIdMine, ExternalImageId = userRef }, new Face { FaceId = FaceIdOther, ExternalImageId = "someone-else" }],
                NextToken = "page-2",
            });
        rekognition.ListFacesAsync(Arg.Is<ListFacesRequest>(r => r.NextToken == "page-2"), Arg.Any<CancellationToken>())
            .Returns(new ListFacesResponse
            {
                Faces = [new Face { FaceId = FaceIdMine2, ExternalImageId = userRef }],
                NextToken = null,
            });
        var sut = CreateSut(rekognition);

        // Act
        await sut.DeleteAsync(IdentityCoreFakers.NewTenantId(), userRef);

        // Assert
        await rekognition.Received(1).DeleteFacesAsync(
            Arg.Is<DeleteFacesRequest>(r => r.FaceIds.Count == 2 && r.FaceIds.Contains(FaceIdMine) && r.FaceIds.Contains(FaceIdMine2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NoMatchingFaces_DoesNotCallDeleteFaces()
    {
        // Arrange
        var rekognition = Substitute.For<IAmazonRekognition>();
        rekognition.ListFacesAsync(Arg.Any<ListFacesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListFacesResponse { Faces = [], NextToken = null });
        var sut = CreateSut(rekognition);

        // Act
        await sut.DeleteAsync(IdentityCoreFakers.NewTenantId(), IdentityCoreFakers.NewUserRef());

        // Assert
        await rekognition.DidNotReceive().DeleteFacesAsync(Arg.Any<DeleteFacesRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EnsureCollectionAsync_NullRekognition_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => new RekognitionFaceIndex(null!, Options, NullLogger<RekognitionFaceIndex>.Instance);

        // Assert
        Should.Throw<ArgumentNullException>(act);
    }
}
