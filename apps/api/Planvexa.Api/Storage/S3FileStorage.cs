namespace Planvexa.Api.Storage;

using Amazon.S3;
using Amazon.S3.Model;
using Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// S3-compatible object storage: works against MinIO for local dev (see
/// docker-compose.yml/AppHost.cs) and any real S3-compatible provider (AWS S3, R2, etc.) in production.
/// Selected via <c>FileStorage:Provider = "S3"</c> (default remains "LocalDisk" so existing dev workflows
/// without MinIO configured keep working — see Program.cs's DI registration). Configuration:
/// <c>FileStorage:S3:ServiceUrl</c> (set for MinIO/non-AWS endpoints; omit for real AWS S3),
/// <c>FileStorage:S3:BucketName</c>, <c>FileStorage:S3:AccessKey</c>/<c>SecretKey</c> (or omit both to use
/// the default AWS credential chain in production), <c>FileStorage:S3:Region</c>,
/// <c>FileStorage:S3:ForcePathStyle</c> (true for MinIO, which does not support virtual-hosted-style
/// bucket URLs). No secrets are hardcoded (AGENTS.md rule 14) — access/secret keys, when set, come only
/// from configuration/environment/user-secrets, never from source.
/// </summary>
public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3FileStorage(IConfiguration configuration)
    {
        var section = configuration.GetSection("FileStorage:S3");
        _bucket = section["BucketName"] ?? throw new InvalidOperationException("FileStorage:S3:BucketName is required when FileStorage:Provider is 'S3'.");

        var config = new AmazonS3Config
        {
            ForcePathStyle = section.GetValue("ForcePathStyle", true),
        };

        var serviceUrl = section["ServiceUrl"];
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
        }
        else if (!string.IsNullOrWhiteSpace(section["Region"]))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(section["Region"]);
        }

        var accessKey = section["AccessKey"];
        var secretKey = section["SecretKey"];
        _client = !string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonS3Client(accessKey, secretKey, config)
            // Falls back to the default AWS credential chain (instance role, env vars, etc.) — the right
            // choice in production where static keys should not be configured at all.
            : new AmazonS3Client(config);
    }

    private int _bucketEnsured;

    /// <summary>Creates the bucket on first use if it doesn't already exist — real AWS S3 buckets are
    /// normally provisioned out-of-band (this call is then a harmless no-op, since the bucket already
    /// exists), but MinIO's dev bucket has nothing else to create it, and requiring a separate manual
    /// setup step for local dev is exactly the kind of friction object storage should not add. Guarded so
    /// it runs at most once per process (this class is registered as a singleton).</summary>
    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _bucketEnsured, 1) == 1)
        {
            return;
        }

        if (!await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_client, _bucket))
        {
            try
            {
                await _client.PutBucketAsync(_bucket, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // Benign race: another concurrent request already created it.
            }
        }
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        await EnsureBucketAsync(cancellationToken);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = path,
            InputStream = content,
            AutoCloseStream = false,
        }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(_bucket, path, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        await _client.DeleteObjectAsync(_bucket, path, cancellationToken);
    }

    public Task<string> GetSignedDownloadUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default)
        => Task.FromResult(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = path,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry),
        }));

    public Task<string> GetSignedUploadUrlAsync(string path, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default)
        => Task.FromResult(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = path,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(expiry),
        }));
}
