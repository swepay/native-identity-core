namespace Native.IdentityCore.FaceIndex;

/// <summary>Thrown by <see cref="IFaceIndex"/> implementations for domain failures that are not an AWS SDK fault — e.g. no face detected in an image submitted for indexing.</summary>
public sealed class FaceIndexException : Exception
{
    public FaceIndexException(string message) : base(message)
    {
    }

    public FaceIndexException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
