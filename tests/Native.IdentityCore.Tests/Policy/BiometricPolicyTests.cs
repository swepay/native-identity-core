using Native.IdentityCore.Policy;

namespace Native.IdentityCore.Tests.Policy;

public class BiometricPolicyTests
{
    [Fact]
    public void Validate_DefaultPolicy_ReturnsSameInstance()
    {
        // Arrange
        var policy = new BiometricPolicy();

        // Act
        var result = policy.Validate();

        // Assert
        result.ShouldBeSameAs(policy);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(100.1d)]
    public void Validate_LivenessConfidenceOutOfRange_ThrowsArgumentOutOfRangeException(double minLivenessConfidence)
    {
        // Arrange
        var policy = new BiometricPolicy(MinLivenessConfidence: minLivenessConfidence);

        // Act
        var act = () => policy.Validate();

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).ParamName.ShouldBe("MinLivenessConfidence");
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(100.1d)]
    public void Validate_FaceSimilarityOutOfRange_ThrowsArgumentOutOfRangeException(double minFaceSimilarity)
    {
        // Arrange
        var policy = new BiometricPolicy(MinFaceSimilarity: minFaceSimilarity);

        // Act
        var act = () => policy.Validate();

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).ParamName.ShouldBe("MinFaceSimilarity");
    }

    [Fact]
    public void Validate_MaxAttemptsBelowOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var policy = new BiometricPolicy(MaxAttempts: 0);

        // Act
        var act = () => policy.Validate();

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).ParamName.ShouldBe("MaxAttempts");
    }
}
