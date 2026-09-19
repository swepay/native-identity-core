using Native.IdentityCore.FaceIndex;

namespace Native.IdentityCore.Tests.FaceIndex;

public class FaceCollectionNamingTests
{
    [Fact]
    public void Build_ValidSegments_ReturnsPrefixedCollectionId()
    {
        // Arrange / Act
        var collectionId = FaceCollectionNaming.Build("kyc", "hml", "tenant-42");

        // Assert
        collectionId.ShouldBe("swepay-kyc-hml-tenant-42");
    }

    [Theory]
    [InlineData("", "hml", "tenant-1")]
    [InlineData("kyc", "", "tenant-1")]
    [InlineData("kyc", "hml", "")]
    public void Build_EmptySegment_ThrowsArgumentException(string product, string environment, string tenantId)
    {
        // Arrange / Act
        var act = () => FaceCollectionNaming.Build(product, environment, tenantId);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Theory]
    [InlineData("KYC")]
    [InlineData("kyc_prod")]
    [InlineData("kyc prod")]
    [InlineData("kyc.prod")]
    public void Build_SegmentWithInvalidCharacters_ThrowsArgumentException(string invalidProduct)
    {
        // Arrange / Act
        var act = () => FaceCollectionNaming.Build(invalidProduct, "hml", "tenant-1");

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Build_CollectionIdExceeds255Characters_ThrowsArgumentException()
    {
        // Arrange
        var longTenantId = new string('a', 250);

        // Act
        var act = () => FaceCollectionNaming.Build("kyc", "hml", longTenantId);

        // Assert
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("tenantId");
    }

    [Fact]
    public void TryParse_CollectionBuiltByBuild_RoundTrips()
    {
        // Arrange
        var collectionId = FaceCollectionNaming.Build("kyc", "prd", "tenant-42");

        // Act
        var parsed = FaceCollectionNaming.TryParse(collectionId, out var product, out var environment, out var tenantId);

        // Assert
        parsed.ShouldBeTrue();
        product.ShouldBe("kyc");
        environment.ShouldBe("prd");
        tenantId.ShouldBe("tenant-42");
    }

    [Fact]
    public void TryParse_TenantIdContainingDashes_KeepsFullTenantIdTogether()
    {
        // Arrange
        var collectionId = FaceCollectionNaming.Build("kyc", "prd", "a1b2c3d4-e5f6-7890");

        // Act
        var parsed = FaceCollectionNaming.TryParse(collectionId, out _, out _, out var tenantId);

        // Assert
        parsed.ShouldBeTrue();
        tenantId.ShouldBe("a1b2c3d4-e5f6-7890");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-swepay-kyc-hml-tenant-1")]
    [InlineData("swepay-onlyoneseg")]
    [InlineData(null)]
    public void TryParse_NotBuiltBySwepay_ReturnsFalse(string? collectionId)
    {
        // Arrange / Act
        var parsed = FaceCollectionNaming.TryParse(collectionId!, out var product, out var environment, out var tenantId);

        // Assert
        parsed.ShouldBeFalse();
        product.ShouldBe(string.Empty);
        environment.ShouldBe(string.Empty);
        tenantId.ShouldBe(string.Empty);
    }
}
