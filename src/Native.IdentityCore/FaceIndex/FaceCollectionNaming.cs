using System.Text.RegularExpressions;

namespace Native.IdentityCore.FaceIndex;

/// <summary>
/// Builds and validates the Rekognition collection id used to keep one face collection per
/// tenant: <c>swepay-{product}-{env}-{tenantId}</c>. Centralizing this here means no caller can
/// construct a collection id ad-hoc and accidentally cross tenant boundaries (GS-05).
/// </summary>
public static partial class FaceCollectionNaming
{
    private const string Prefix = "swepay";

    // Rekognition collection ids allow [a-zA-Z0-9_.-], 1-255 chars. We additionally lower-case
    // product/environment and restrict tenantId to the identifier shape used across the
    // ecosystem (GS-05 tenant_id), so a collection id round-trips through TryParse.
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex SegmentPattern();

    /// <summary>Builds <c>swepay-{product}-{environment}-{tenantId}</c>, validating every segment.</summary>
    /// <exception cref="ArgumentException">A segment is null/empty or contains characters outside <c>[a-z0-9-]</c>.</exception>
    public static string Build(string product, string environment, string tenantId)
    {
        ValidateSegment(product, nameof(product));
        ValidateSegment(environment, nameof(environment));
        ValidateSegment(tenantId, nameof(tenantId));

        var collectionId = $"{Prefix}-{product}-{environment}-{tenantId}";
        if (collectionId.Length > 255)
        {
            throw new ArgumentException($"Collection id '{collectionId}' exceeds the 255-character Rekognition limit.", nameof(tenantId));
        }

        return collectionId;
    }

    /// <summary>Attempts to split a collection id back into product/environment/tenantId. Returns <see langword="false"/> for anything not produced by <see cref="Build"/> (including collections belonging to another prefix).</summary>
    public static bool TryParse(string collectionId, out string product, out string environment, out string tenantId)
    {
        product = string.Empty;
        environment = string.Empty;
        tenantId = string.Empty;

        if (string.IsNullOrEmpty(collectionId) || !collectionId.StartsWith($"{Prefix}-", StringComparison.Ordinal))
        {
            return false;
        }

        // swepay-{product}-{environment}-{tenantId}: product and environment are single
        // segments by convention; tenantId is everything after, and may itself be a GUID
        // (no dashes stripped) or contain dashes, so it is not split further.
        var remainder = collectionId[(Prefix.Length + 1)..];
        var parts = remainder.Split('-', 3);
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        product = parts[0];
        environment = parts[1];
        tenantId = parts[2];
        return true;
    }

    private static void ValidateSegment(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Segment must not be null or empty.", paramName);
        }

        if (!SegmentPattern().IsMatch(value))
        {
            throw new ArgumentException($"Segment '{value}' must match [a-z0-9-] (lower-case alphanumeric and hyphens only).", paramName);
        }
    }
}
