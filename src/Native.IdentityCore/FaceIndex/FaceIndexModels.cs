namespace Native.IdentityCore.FaceIndex;

/// <summary>Result of indexing one face for a tenant user via <see cref="IFaceIndex.IndexAsync"/>.</summary>
/// <param name="FaceId">Rekognition-assigned face id, unique within the tenant collection.</param>
/// <param name="UserRef">Caller-supplied user reference the face was indexed under (<c>ExternalImageId</c>).</param>
/// <param name="Confidence">Confidence (0-100) that the indexed region is a face.</param>
public sealed record FaceIndexResult(string FaceId, string UserRef, double Confidence);

/// <summary>One candidate returned by <see cref="IFaceIndex.SearchAsync"/>, ordered by <see cref="Similarity"/> descending.</summary>
/// <param name="UserRef">The <c>ExternalImageId</c> the matched face was indexed under.</param>
/// <param name="FaceId">Rekognition face id of the matched face.</param>
/// <param name="Similarity">Similarity (0-100) between the presented image and this indexed face.</param>
public sealed record FaceSearchMatch(string UserRef, string FaceId, double Similarity);

/// <summary>Result of <see cref="IFaceIndex.SearchAsync"/> against a tenant's face collection.</summary>
/// <param name="Found"><see langword="true"/> when at least one candidate met the search threshold.</param>
/// <param name="BestMatch">Highest-similarity candidate, or <see langword="null"/> when <see cref="Found"/> is <see langword="false"/>.</param>
/// <param name="Matches">All candidates returned by Rekognition, ordered by similarity descending. Empty when <see cref="Found"/> is <see langword="false"/>.</param>
public sealed record FaceSearchResult(bool Found, FaceSearchMatch? BestMatch, IReadOnlyList<FaceSearchMatch> Matches)
{
    public static FaceSearchResult NoMatch { get; } = new(Found: false, BestMatch: null, []);
}
