using Native.IdentityCore.Assertions;
using Native.IdentityCore.Policy;
using Native.IdentityCore.Tests.Fakers;

namespace Native.IdentityCore.Tests.Assertions;

public class BiometricAssertionTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static BiometricAssertion CreateValid() => new(
        TenantId: IdentityCoreFakers.NewTenantId(),
        UserRef: IdentityCoreFakers.NewUserRef(),
        Purpose: BiometricAssertionPurpose.Verification,
        LivenessConfidence: 96d,
        FaceSimilarity: 98d,
        Decision: BiometricOutcome.Verified,
        IssuedAt: IssuedAt,
        ExpiresAt: IssuedAt.AddMinutes(5),
        CorrelationId: IdentityCoreFakers.NewCorrelationId());

    [Fact]
    public void Validate_AllFieldsPresent_ReturnsSameInstance()
    {
        // Arrange
        var assertion = CreateValid();

        // Act
        var result = assertion.Validate();

        // Assert
        result.ShouldBeSameAs(assertion);
    }

    [Fact]
    public void Validate_MissingTenantId_ThrowsArgumentException()
    {
        // Arrange
        var assertion = CreateValid() with { TenantId = string.Empty };

        // Act
        var act = () => assertion.Validate();

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Validate_MissingUserRef_ThrowsArgumentException()
    {
        // Arrange
        var assertion = CreateValid() with { UserRef = string.Empty };

        // Act
        var act = () => assertion.Validate();

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Validate_MissingCorrelationId_ThrowsArgumentException()
    {
        // Arrange
        var assertion = CreateValid() with { CorrelationId = string.Empty };

        // Act
        var act = () => assertion.Validate();

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Validate_ExpiresAtNotAfterIssuedAt_ThrowsArgumentException()
    {
        // Arrange
        var assertion = CreateValid() with { ExpiresAt = IssuedAt };

        // Act
        var act = () => assertion.Validate();

        // Assert
        Should.Throw<ArgumentException>(act);
    }
}
