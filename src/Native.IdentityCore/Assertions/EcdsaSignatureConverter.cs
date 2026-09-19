namespace Native.IdentityCore.Assertions;

/// <summary>
/// KMS's <c>Sign</c> API returns ECDSA signatures DER-encoded (ASN.1 <c>SEQUENCE { r INTEGER, s INTEGER }</c>);
/// JWS ES256/ES384/ES512 (RFC 7518 §3.4) requires the fixed-width, big-endian <c>R || S</c> concatenation
/// instead. This is a pure, allocation-light transform with no AWS/JSON dependency so it is unit-testable
/// in isolation from KMS.
/// </summary>
public static class EcdsaSignatureConverter
{
    /// <summary>Converts a DER-encoded ECDSA signature to the fixed-width JOSE <c>R || S</c> form.</summary>
    /// <param name="der">DER-encoded <c>SEQUENCE { r INTEGER, s INTEGER }</c> as returned by KMS.</param>
    /// <param name="fieldSizeBytes">Curve field size in bytes (32 for P-256/ES256, 48 for P-384/ES384, 66 for P-521/ES512).</param>
    public static byte[] DerToJose(ReadOnlySpan<byte> der, int fieldSizeBytes)
    {
        if (fieldSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldSizeBytes), fieldSizeBytes, "Must be positive.");
        }

        var offset = 0;
        ExpectTag(der, ref offset, tag: 0x30); // SEQUENCE
        ReadLength(der, ref offset); // sequence length (unused — we read exactly two INTEGERs)

        var r = ReadUnsignedInteger(der, ref offset);
        var s = ReadUnsignedInteger(der, ref offset);

        var jose = new byte[fieldSizeBytes * 2];
        CopyRightAligned(r, jose.AsSpan(0, fieldSizeBytes));
        CopyRightAligned(s, jose.AsSpan(fieldSizeBytes, fieldSizeBytes));
        return jose;
    }

    private static ReadOnlySpan<byte> ReadUnsignedInteger(ReadOnlySpan<byte> der, ref int offset)
    {
        ExpectTag(der, ref offset, tag: 0x02); // INTEGER
        var length = ReadLength(der, ref offset);
        var value = der.Slice(offset, length);
        offset += length;

        // DER INTEGER is signed two's-complement; a leading 0x00 pad byte is added whenever the
        // high bit of the first content byte would otherwise flip the sign. Strip it — JOSE wants
        // an unsigned magnitude.
        if (value.Length > 1 && value[0] == 0x00)
        {
            value = value[1..];
        }

        return value;
    }

    private static void CopyRightAligned(ReadOnlySpan<byte> value, Span<byte> destination)
    {
        if (value.Length > destination.Length)
        {
            throw new InvalidOperationException($"ECDSA integer of {value.Length} bytes does not fit the {destination.Length}-byte JOSE field — check fieldSizeBytes matches the signing curve.");
        }

        destination.Clear();
        value.CopyTo(destination[(destination.Length - value.Length)..]);
    }

    private static void ExpectTag(ReadOnlySpan<byte> der, ref int offset, byte tag)
    {
        if (offset >= der.Length || der[offset] != tag)
        {
            throw new FormatException($"Malformed DER signature: expected tag 0x{tag:X2} at offset {offset}.");
        }

        offset++;
    }

    private static int ReadLength(ReadOnlySpan<byte> der, ref int offset)
    {
        if (offset >= der.Length)
        {
            throw new FormatException("Malformed DER signature: unexpected end of data reading length.");
        }

        var first = der[offset++];
        if ((first & 0x80) == 0)
        {
            return first;
        }

        var byteCount = first & 0x7F;
        if (byteCount is 0 or > 4 || offset + byteCount > der.Length)
        {
            throw new FormatException("Malformed DER signature: unsupported long-form length.");
        }

        var length = 0;
        for (var i = 0; i < byteCount; i++)
        {
            length = (length << 8) | der[offset++];
        }

        return length;
    }
}
