using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging;

namespace Native.IdentityCore.FaceIndex;

/// <summary>
/// <see cref="IFaceIndex"/> implementation over <see cref="IAmazonRekognition"/>. One collection
/// per tenant (<see cref="FaceCollectionNaming"/>); no cross-tenant calls are possible because
/// every request builds its <c>CollectionId</c> from the caller-supplied <c>tenantId</c> (GS-05).
/// Logs never include image bytes, <c>tenantId</c>-derived collection ids, or raw <c>userRef</c>
/// values beyond what is required for correlation — no biometric data is logged (GS-09).
/// </summary>
public sealed class RekognitionFaceIndex(IAmazonRekognition rekognition, RekognitionFaceIndexOptions options, ILogger<RekognitionFaceIndex> logger) : IFaceIndex
{
    private readonly IAmazonRekognition _rekognition = rekognition ?? throw new ArgumentNullException(nameof(rekognition));
    private readonly RekognitionFaceIndexOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RekognitionFaceIndex> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask EnsureCollectionAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        var collectionId = _options.BuildCollectionId(tenantId);

        try
        {
            await _rekognition.CreateCollectionAsync(new CreateCollectionRequest { CollectionId = collectionId }, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created Rekognition face collection for tenant.");
        }
        catch (ResourceAlreadyExistsException)
        {
            _logger.LogDebug("Rekognition face collection already exists for tenant.");
        }
    }

    public async ValueTask<FaceIndexResult> IndexAsync(string tenantId, string userRef, ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(userRef);
        var collectionId = _options.BuildCollectionId(tenantId);

        using var stream = ToStream(imageBytes);
        var response = await _rekognition.IndexFacesAsync(new IndexFacesRequest
        {
            CollectionId = collectionId,
            ExternalImageId = userRef,
            Image = new Image { Bytes = stream },
            MaxFaces = 1,
            QualityFilter = QualityFilter.AUTO,
        }, cancellationToken).ConfigureAwait(false);

        var record = response.FaceRecords?.FirstOrDefault()
            ?? throw new FaceIndexException($"No face detected while indexing (unindexed reasons: {DescribeUnindexed(response)}).");

        _logger.LogInformation("Indexed face for tenant user.");
        return new FaceIndexResult(record.Face.FaceId, userRef, record.Face.Confidence ?? 0d);
    }

    public async ValueTask<FaceSearchResult> SearchAsync(string tenantId, ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        var collectionId = _options.BuildCollectionId(tenantId);

        using var stream = ToStream(imageBytes);
        List<FaceMatch>? faceMatches;
        try
        {
            var raw = await _rekognition.SearchFacesByImageAsync(new SearchFacesByImageRequest
            {
                CollectionId = collectionId,
                Image = new Image { Bytes = stream },
                FaceMatchThreshold = (float)_options.FaceMatchThreshold,
                MaxFaces = _options.MaxFaces,
            }, cancellationToken).ConfigureAwait(false);
            faceMatches = raw.FaceMatches;
        }
        catch (InvalidParameterException)
        {
            // Rekognition throws InvalidParameterException when it cannot detect a face in the
            // probe image at all — treat as "no match" rather than surfacing an SDK exception.
            _logger.LogInformation("No face detected in search probe image for tenant.");
            return FaceSearchResult.NoMatch;
        }

        if (faceMatches is not { Count: > 0 })
        {
            _logger.LogInformation("No face match found for tenant.");
            return FaceSearchResult.NoMatch;
        }

        var matches = faceMatches
            .OrderByDescending(m => m.Similarity ?? 0d)
            .Select(m => new FaceSearchMatch(m.Face.ExternalImageId, m.Face.FaceId, m.Similarity ?? 0d))
            .ToList();

        _logger.LogInformation("Found {MatchCount} face match(es) for tenant.", matches.Count);
        return new FaceSearchResult(Found: true, BestMatch: matches[0], matches);
    }

    public async ValueTask DeleteAsync(string tenantId, string userRef, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(userRef);
        var collectionId = _options.BuildCollectionId(tenantId);

        var faceIds = await CollectFaceIdsAsync(collectionId, userRef, cancellationToken).ConfigureAwait(false);
        if (faceIds.Count == 0)
        {
            _logger.LogDebug("No indexed faces to delete for tenant user.");
            return;
        }

        await _rekognition.DeleteFacesAsync(new DeleteFacesRequest { CollectionId = collectionId, FaceIds = faceIds }, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Deleted {FaceCount} indexed face(s) for tenant user.", faceIds.Count);
    }

    public async ValueTask DeleteByFaceIdAsync(string tenantId, string faceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(faceId);
        var collectionId = _options.BuildCollectionId(tenantId);

        await _rekognition.DeleteFacesAsync(
            new DeleteFacesRequest { CollectionId = collectionId, FaceIds = [faceId] },
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Deleted indexed face by id for tenant.");
    }

    public async ValueTask DeleteCollectionAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        var collectionId = _options.BuildCollectionId(tenantId);

        try
        {
            await _rekognition.DeleteCollectionAsync(
                new DeleteCollectionRequest { CollectionId = collectionId },
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Deleted Rekognition face collection for tenant.");
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogDebug("Rekognition face collection already absent for tenant.");
        }
    }

    private async Task<List<string>> CollectFaceIdsAsync(string collectionId, string userRef, CancellationToken cancellationToken)
    {
        var faceIds = new List<string>();
        string? nextToken = null;

        do
        {
            var page = await _rekognition.ListFacesAsync(new ListFacesRequest
            {
                CollectionId = collectionId,
                NextToken = nextToken,
            }, cancellationToken).ConfigureAwait(false);

            faceIds.AddRange(page.Faces
                .Where(f => string.Equals(f.ExternalImageId, userRef, StringComparison.Ordinal))
                .Select(f => f.FaceId));

            nextToken = page.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return faceIds;
    }

    private static MemoryStream ToStream(ReadOnlyMemory<byte> imageBytes)
    {
        if (MemoryMarshalTryGetArray(imageBytes, out var segment))
        {
            return new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false);
        }

        return new MemoryStream(imageBytes.ToArray(), writable: false);
    }

    private static bool MemoryMarshalTryGetArray(ReadOnlyMemory<byte> memory, out ArraySegment<byte> segment) =>
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out segment);

    private static string DescribeUnindexed(IndexFacesResponse response) =>
        response.UnindexedFaces is { Count: > 0 }
            ? string.Join(", ", response.UnindexedFaces.Select(f => f.Reasons is { Count: > 0 } ? string.Join("|", f.Reasons) : "UNKNOWN"))
            : "NONE";
}
