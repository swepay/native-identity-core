using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging;

namespace Native.IdentityCore.Liveness;

/// <summary>
/// <see cref="ILivenessSessionService"/> implementation over <see cref="IAmazonRekognition"/>.
/// Logs carry <c>tenantId</c> and <c>sessionId</c> for correlation (GS-11) but never a
/// confidence score, image bytes, or any other biometric signal (GS-09).
/// </summary>
public sealed class RekognitionLivenessSessionService(IAmazonRekognition rekognition, ILogger<RekognitionLivenessSessionService> logger) : ILivenessSessionService
{
    private readonly IAmazonRekognition _rekognition = rekognition ?? throw new ArgumentNullException(nameof(rekognition));
    private readonly ILogger<RekognitionLivenessSessionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<LivenessSessionHandle> CreateSessionAsync(string tenantId, LivenessSessionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentNullException.ThrowIfNull(options);

        var request = new CreateFaceLivenessSessionRequest
        {
            KmsKeyId = options.KmsKeyId,
            ClientRequestToken = options.ClientRequestToken,
        };

        if (options.AuditImagesLimit is not null || options.OutputS3Bucket is not null)
        {
            request.Settings = new CreateFaceLivenessSessionRequestSettings
            {
                AuditImagesLimit = options.AuditImagesLimit,
                OutputConfig = options.OutputS3Bucket is null
                    ? null
                    : new LivenessOutputConfig { S3Bucket = options.OutputS3Bucket, S3KeyPrefix = options.OutputS3KeyPrefix },
            };
        }

        var response = await _rekognition.CreateFaceLivenessSessionAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created liveness session {SessionId} for tenant {TenantId}.", response.SessionId, tenantId);
        return new LivenessSessionHandle(response.SessionId);
    }

    public async ValueTask<LivenessSessionResult> GetSessionResultAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var response = await _rekognition.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = sessionId },
            cancellationToken).ConfigureAwait(false);

        var status = MapStatus(response.Status);
        _logger.LogInformation("Liveness session {SessionId} for tenant {TenantId} is {Status}.", sessionId, tenantId, status);

        // NOTE: deliberately an if/else, not `cond ? null : new ReadOnlyMemory<byte>(bytes)`.
        // byte[] has an implicit conversion to ReadOnlyMemory<byte> that accepts a null array
        // (producing a zero-length, non-null memory); the C# ternary's "common type" inference
        // routes the `null` branch through that very conversion, so the conditional expression
        // ends up *always* non-null (HasValue: true) even when bytes is null — an if/else avoids
        // that pitfall entirely.
        var referenceBytes = response.ReferenceImage?.Bytes?.ToArray();
        ReadOnlyMemory<byte>? referenceImage;
        if (referenceBytes is null)
        {
            referenceImage = null;
        }
        else
        {
            referenceImage = new ReadOnlyMemory<byte>(referenceBytes);
        }

        var auditImages = response.AuditImages is { Count: > 0 }
            ? response.AuditImages.Where(a => a.Bytes is not null).Select(a => new ReadOnlyMemory<byte>(a.Bytes!.ToArray())).ToList()
            : [];

        return new LivenessSessionResult(response.SessionId, status, response.Confidence, referenceImage, auditImages);
    }

    private static LivenessSessionStatus MapStatus(string status) => status switch
    {
        var s when s == Amazon.Rekognition.LivenessSessionStatus.CREATED => LivenessSessionStatus.Created,
        var s when s == Amazon.Rekognition.LivenessSessionStatus.IN_PROGRESS => LivenessSessionStatus.InProgress,
        var s when s == Amazon.Rekognition.LivenessSessionStatus.SUCCEEDED => LivenessSessionStatus.Succeeded,
        var s when s == Amazon.Rekognition.LivenessSessionStatus.FAILED => LivenessSessionStatus.Failed,
        var s when s == Amazon.Rekognition.LivenessSessionStatus.EXPIRED => LivenessSessionStatus.Expired,
        _ => throw new NotSupportedException($"Unknown Rekognition liveness session status '{status}'."),
    };
}
