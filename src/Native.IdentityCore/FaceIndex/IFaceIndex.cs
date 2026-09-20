namespace Native.IdentityCore.FaceIndex;

/// <summary>
/// Per-tenant face collection backed by Amazon Rekognition (<c>IndexFaces</c> /
/// <c>SearchFacesByImage</c> / <c>DeleteFaces</c>). Every method takes <c>tenantId</c> explicitly
/// and resolves it to a dedicated collection (<see cref="FaceCollectionNaming"/>) — there is no
/// method that can address a collection across tenants (GS-05).
/// <para>
/// This abstraction never persists image bytes; callers pass image bytes in-memory and Rekognition
/// discards them after detection (only the extracted face template is stored, inside the AWS account).
/// </para>
/// </summary>
public interface IFaceIndex
{
    /// <summary>Creates the tenant's face collection if it does not already exist. Idempotent.</summary>
    ValueTask EnsureCollectionAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a face for <paramref name="userRef"/> in the tenant collection (enrollment).
    /// Calls <see cref="EnsureCollectionAsync"/> is the caller's responsibility up front; this
    /// method does not implicitly create the collection so callers can distinguish
    /// "unknown tenant" from "no face detected" failures.
    /// </summary>
    /// <param name="imageBytes">JPEG/PNG bytes of a single face. Not persisted by this library.</param>
    ValueTask<FaceIndexResult> IndexAsync(string tenantId, string userRef, ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default);

    /// <summary>Searches the tenant collection for the best match to <paramref name="imageBytes"/> (verification).</summary>
    ValueTask<FaceSearchResult> SearchAsync(string tenantId, ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default);

    /// <summary>Removes every indexed face for <paramref name="userRef"/> from the tenant collection (e.g. LGPD erasure request, re-enrollment). Scans the collection (<c>ListFaces</c>) to resolve <paramref name="userRef"/> to its face id(s) first — prefer <see cref="DeleteByFaceIdAsync"/> when the caller already persisted <see cref="FaceIndexResult.FaceId"/> at enrollment time.</summary>
    ValueTask DeleteAsync(string tenantId, string userRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a single indexed face by its Rekognition-assigned <paramref name="faceId"/> — a
    /// direct <c>DeleteFaces</c> call, no <c>ListFaces</c> scan. Use this when the caller already
    /// persisted <see cref="FaceIndexResult.FaceId"/> from <see cref="IndexAsync"/> (the common
    /// enrollment pattern); fall back to <see cref="DeleteAsync(string,string,CancellationToken)"/>
    /// when only the <c>userRef</c> is known.
    /// </summary>
    ValueTask DeleteByFaceIdAsync(string tenantId, string faceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the tenant's entire face collection (e.g. LGPD erasure of a whole tenant — GS-12
    /// purge). Idempotent: a collection that does not exist (already deleted, or never created)
    /// is not an error.
    /// </summary>
    ValueTask DeleteCollectionAsync(string tenantId, CancellationToken cancellationToken = default);
}
