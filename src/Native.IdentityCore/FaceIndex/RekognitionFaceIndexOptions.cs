namespace Native.IdentityCore.FaceIndex;

/// <summary>
/// Configuration for <see cref="RekognitionFaceIndex"/>. Registered once per product/environment
/// (not per tenant) — the tenant id is supplied per call and combined with these two segments by
/// <see cref="FaceCollectionNaming"/>.
/// </summary>
/// <param name="Product">Product segment of the collection id, e.g. <c>"kyc"</c>, <c>"biometrics"</c>. Must match <c>[a-z0-9-]+</c>.</param>
/// <param name="Environment">Deploy environment segment: <c>"hml"</c> or <c>"prd"</c> (GS-01). Must match <c>[a-z0-9-]+</c>.</param>
/// <param name="FaceMatchThreshold">Minimum similarity (0-100) Rekognition itself applies before returning a candidate from <c>SearchFacesByImage</c>. Independent from — and typically looser than — <see cref="Policy.BiometricPolicy.MinFaceSimilarity"/>, which is the business threshold applied afterwards by <see cref="Policy.BiometricDecision"/>.</param>
/// <param name="MaxFaces">Maximum number of candidates Rekognition returns per search.</param>
public sealed record RekognitionFaceIndexOptions(
    string Product,
    string Environment,
    double FaceMatchThreshold = 80d,
    int MaxFaces = 1)
{
    public string BuildCollectionId(string tenantId) => FaceCollectionNaming.Build(Product, Environment, tenantId);
}
