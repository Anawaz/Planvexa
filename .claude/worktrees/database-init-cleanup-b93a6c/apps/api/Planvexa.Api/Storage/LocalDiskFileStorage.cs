namespace Planvexa.Api.Storage;

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Stores blobs on the local filesystem under <c>FileStorage:RootPath</c> (default
/// <c>{ContentRoot}/App_Data/files</c>). Every logical path is resolved and verified to stay inside
/// that root, so a crafted name cannot escape it. Dev/single-node fallback; see
/// <see cref="S3FileStorage"/> for the object-storage implementation swapped in via
/// <c>FileStorage:Provider</c>.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly IDataProtector _protector;

    public LocalDiskFileStorage(IConfiguration configuration, IHostEnvironment environment, IDataProtectionProvider dataProtection)
    {
        var configured = configuration["FileStorage:RootPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "files")
            : configured);

        // Same ASP.NET Core Data Protection API already used for encrypted-at-rest secrets elsewhere in
        // this host (DataProtectionAiSecretProtector, DataProtectionIntegrationSecretProtector) — reused
        // here instead of hand-rolling HMAC signing, per AGENTS.md rule 16 (prefer existing framework
        // capabilities). Keys are managed by the framework's default key ring, so tokens minted by one
        // process instance are only valid on hosts sharing that key ring (fine for the local-disk dev
        // fallback this backs).
        _protector = dataProtection.CreateProtector("Planvexa.Storage.SignedUrl.v1");
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(File.OpenRead(Resolve(path)));

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        // File.Delete is a no-op when the file is already gone, which is what callers want.
        File.Delete(Resolve(path));
        return Task.CompletedTask;
    }

    public Task<string> GetSignedDownloadUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildSignedUrl("download", path, contentType: null, expiry));

    public Task<string> GetSignedUploadUrlAsync(string path, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildSignedUrl("upload", path, contentType, expiry));

    private string BuildSignedUrl(string mode, string path, string? contentType, TimeSpan expiry)
    {
        var payload = JsonSerializer.Serialize(new SignedFileToken(path, contentType, mode, DateTimeOffset.UtcNow.Add(expiry)));
        var token = _protector.Protect(payload);
        return $"/api/v1/files/signed/{mode}?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Validates a token minted by <see cref="GetSignedDownloadUrlAsync"/>/<see
    /// cref="GetSignedUploadUrlAsync"/> for the given <paramref name="expectedMode"/> ("download" or
    /// "upload"): unprotects it (tamper/forgery detection comes from Data Protection's built-in
    /// authenticated encryption), rejects it if expired, rejects it if it was minted for the other mode
    /// (an upload token cannot be replayed as a download token or vice-versa). Called only by
    /// <c>SignedFileEndpoints</c> — nothing else needs this, since S3 presigned URLs are verified by S3
    /// itself and never reach this API at all.
    /// </summary>
    public SignedFileToken? ValidateSignedToken(string token, string expectedMode)
    {
        string json;
        try
        {
            json = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null;
        }

        SignedFileToken? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SignedFileToken>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null || parsed.Mode != expectedMode || parsed.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            return null;
        }

        return parsed;
    }

    private string Resolve(string path)
    {
        // Path.Combine drops the root for rooted inputs and GetFullPath collapses "..", so the
        // prefix check below is what actually contains the path — not the caller's sanitisation.
        var full = Path.GetFullPath(Path.Combine(_root, path));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The resolved storage path escapes the storage root.");
        }

        return full;
    }
}

/// <summary>Payload of a local-disk signed file URL token (see <see cref="LocalDiskFileStorage"/>).</summary>
public sealed record SignedFileToken(string Path, string? ContentType, string Mode, DateTimeOffset ExpiresAtUtc);
