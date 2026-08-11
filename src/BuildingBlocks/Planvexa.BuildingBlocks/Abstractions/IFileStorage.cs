namespace Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Binary blob storage for user-uploaded content. Paths are logical and forward-slash separated;
/// callers prefix them with the owning tenant. The implementation decides where the bytes actually
/// live — local disk in development, object storage in a real deployment.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// A time-limited URL a client can GET directly to download the blob at <paramref name="path"/>
    /// — for object storage this is a native pre-signed GET that bypasses the API for
    /// the byte transfer entirely; for local disk (which has no notion of a direct-to-storage URL) it is
    /// an API-relative URL carrying a signed, time-limited token that is still proxied through this API.
    /// See each implementation's doc comment for exactly what "signed" means for that backend.
    /// </summary>
    Task<string> GetSignedDownloadUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>
    /// A time-limited URL a client can PUT the raw file bytes to at <paramref name="path"/>
    /// item 6) — a pre-signed PUT for object storage; a signed, API-proxied PUT token for local disk. See
    /// <see cref="GetSignedDownloadUrlAsync"/>'s doc comment for the local-disk-vs-object-storage distinction.
    /// </summary>
    Task<string> GetSignedUploadUrlAsync(string path, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default);
}
