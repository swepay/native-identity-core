namespace Native.IdentityCore.Liveness;

/// <summary>
/// Face-liveness sessions backed by Amazon Rekognition (<c>CreateFaceLivenessSession</c> /
/// <c>GetFaceLivenessSessionResults</c>). <c>tenantId</c> is explicit on every call (GS-05); it
/// is not embedded in the session id itself — pass the same <c>tenantId</c> to both methods for
/// a given session, and keep the tenant-to-session mapping in the caller's own store if it needs
/// to enforce that a session is only ever queried by the tenant that created it.
/// <para>
/// This abstraction never persists reference/audit images; <see cref="LivenessSessionResult"/>
/// carries them in-memory for the caller to hand off (e.g. to <see cref="FaceIndex.IFaceIndex.IndexAsync"/>)
/// without this library writing them to disk or object storage.
/// </para>
/// </summary>
public interface ILivenessSessionService
{
    /// <summary>Starts a new liveness session for <paramref name="tenantId"/>.</summary>
    ValueTask<LivenessSessionHandle> CreateSessionAsync(string tenantId, LivenessSessionOptions options, CancellationToken cancellationToken = default);

    /// <summary>Fetches the current status/result for a session previously created for <paramref name="tenantId"/>.</summary>
    ValueTask<LivenessSessionResult> GetSessionResultAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default);
}
