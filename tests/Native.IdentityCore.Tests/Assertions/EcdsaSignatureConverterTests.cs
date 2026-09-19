using Native.IdentityCore.Assertions;

namespace Native.IdentityCore.Tests.Assertions;

public class EcdsaSignatureConverterTests
{
    private const int P256FieldSizeBytes = 32;

    [Fact]
    public void DerToJose_FullWidthIntegersWithoutPadding_ReturnsConcatenatedRAndS()
    {
        // Arrange — r/s already 32 bytes, high bit clear (no DER 0x00 pad needed).
        var r = Enumerable.Repeat((byte)0x11, 32).ToArray();
        var s = Enumerable.Repeat((byte)0x22, 32).ToArray();
        var der = BuildDerSequence(r, s);

        // Act
        var jose = EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes);

        // Assert
        jose.Length.ShouldBe(64);
        jose[..32].ShouldBe(r);
        jose[32..].ShouldBe(s);
    }

    [Fact]
    public void DerToJose_IntegerWithHighBitSet_StripsDerPadButKeepsFullMagnitude()
    {
        // Arrange — r's first byte has the high bit set, so DER pads it with a leading 0x00.
        var r = new byte[32];
        r[0] = 0xFF;
        r[31] = 0x01;
        var s = Enumerable.Repeat((byte)0x05, 32).ToArray();
        var der = BuildDerSequence(r, s);

        // Act
        var jose = EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes);

        // Assert
        jose[..32].ShouldBe(r);
        jose[32..].ShouldBe(s);
    }

    [Fact]
    public void DerToJose_IntegerShorterThanFieldSize_IsLeftPaddedWithZeros()
    {
        // Arrange — r has genuine leading zero bytes (small magnitude), no DER pad involved.
        var r = new byte[32];
        r[30] = 0x01;
        r[31] = 0x02;
        var s = Enumerable.Repeat((byte)0x09, 32).ToArray();
        var der = BuildDerSequence(r, s);

        // Act
        var jose = EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes);

        // Assert
        jose[..32].ShouldBe(r);
    }

    [Fact]
    public void DerToJose_WrongSequenceTag_ThrowsFormatException()
    {
        // Arrange
        var der = new byte[] { 0x31, 0x00 };

        // Act
        var act = () => EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes);

        // Assert
        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void DerToJose_TruncatedData_ThrowsFormatException()
    {
        // Arrange
        var der = new byte[] { 0x30, 0x06, 0x02, 0x02, 0x01, 0x02 }; // declares 6 bytes of content, provides only 4

        // Act
        var act = () => EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes);

        // Assert
        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void DerToJose_IntegerLargerThanFieldSize_ThrowsInvalidOperationException()
    {
        // Arrange — r is 40 bytes, larger than the 32-byte P-256 field.
        var r = Enumerable.Repeat((byte)0x01, 40).ToArray();
        var s = Enumerable.Repeat((byte)0x02, 32).ToArray();
        var der = BuildDerSequence(r, s);

        // Act
        var act = () => EcdsaSignatureConverter.DerToJose(der, P256FieldSizeBytes);

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void DerToJose_NonPositiveFieldSize_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var der = BuildDerSequence([0x01], [0x02]);

        // Act
        var act = () => EcdsaSignatureConverter.DerToJose(der, fieldSizeBytes: 0);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    /// <summary>Builds a minimal <c>SEQUENCE { r INTEGER, s INTEGER }</c> DER encoding for test fixtures (short-form lengths only — sufficient for P-256/P-384 sized integers).</summary>
    private static byte[] BuildDerSequence(byte[] r, byte[] s)
    {
        var rEncoded = EncodeInteger(r);
        var sEncoded = EncodeInteger(s);
        var content = rEncoded.Concat(sEncoded).ToArray();
        return new byte[] { 0x30, checked((byte)content.Length) }.Concat(content).ToArray();
    }

    private static byte[] EncodeInteger(byte[] value)
    {
        var needsPad = value.Length > 0 && (value[0] & 0x80) != 0;
        var content = needsPad ? new byte[] { 0x00 }.Concat(value).ToArray() : value;
        return new byte[] { 0x02, checked((byte)content.Length) }.Concat(content).ToArray();
    }
}
