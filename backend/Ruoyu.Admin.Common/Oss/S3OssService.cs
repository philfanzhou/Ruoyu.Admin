using System.Reactive.Linq;
using Minio;
using Minio.DataModel.Args;

namespace Ruoyu.Admin.Common.Oss;

public enum OssBucket
{
    Uploads,      // uploads/
    Mistakes,     // mistakes/
    Questions,    // questions/
    Documents,    // documents/
}

/// <summary>
/// OSS 访问接口。仅保留审计所需的只读成员（下载、按 bucket 列举对象）；
/// 上传、删除、复制、预签名等写侧与辅助成员已随调用链退役删除，
/// 「审计不改对象」由接口类型约束保证。
/// </summary>
public interface IOssService
{
    Task<Stream> DownloadAsync(string objectPath);
    Task<List<OssObjectInfo>> ListObjectsWithBucketAsync(OssBucket bucket, string? prefix = null);
}

public class OssObjectInfo
{
    public string ObjectPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public bool IsZombie { get; set; }
}

public class S3OssService : IOssService
{
    private readonly IMinioClient _internalClient;
    private readonly string _bucketName;
    private readonly PathPrefixValidator? _prefixValidator;

    public S3OssService(OssOptions options, string[]? allowedPrefixes = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _bucketName = options.BucketName;
        _prefixValidator = allowedPrefixes != null ? new PathPrefixValidator(allowedPrefixes) : null;
        _internalClient = BuildClient(
            options.InternalEndpoint,
            options.AccessKey,
            options.SecretKey,
            options.InternalSecure);
    }

    private static IMinioClient BuildClient(string endpoint, string accessKey, string secretKey, bool secure)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("The S3 endpoint is required.", nameof(endpoint));
        }

        return new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(secure)
            .Build();
    }

    public async Task<Stream> DownloadAsync(string objectPath)
    {
        var memoryStream = new MemoryStream();

        var getObjectArgs = new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream =>
            {
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
            });

        await _internalClient.GetObjectAsync(getObjectArgs);

        return memoryStream;
    }

    public async Task<List<OssObjectInfo>> ListObjectsWithBucketAsync(OssBucket bucket, string? prefix = null)
    {
        var bucketPrefix = bucket switch
        {
            OssBucket.Uploads => "uploads",
            OssBucket.Mistakes => "mistakes",
            OssBucket.Questions => "questions",
            OssBucket.Documents => "documents",
            _ => "others"
        };

        var fullPrefix = string.IsNullOrWhiteSpace(prefix)
            ? bucketPrefix
            : $"{bucketPrefix}/{prefix}";

        return await ListObjectsByPrefixAsync(fullPrefix);
    }

    private async Task<List<OssObjectInfo>> ListObjectsByPrefixAsync(string? prefix)
    {
        var listArgs = new ListObjectsArgs().WithBucket(_bucketName).WithPrefix(prefix).WithRecursive(true);
        // A failed or partial enumeration is not an empty successful observation.
        var items = await _internalClient.ListObjectsAsync(listArgs).ToList();
        return items.Select(item => new OssObjectInfo
        {
            ObjectPath = item.Key,
            Size = (long)item.Size,
            LastModified = !string.IsNullOrEmpty(item.LastModified) && DateTimeOffset.TryParse(item.LastModified, out var time)
                ? time : null,
            IsZombie = false
        }).ToList();
    }
}

public class OssOptions
{
    // Structural defaults only: credentials have no code default. Local-development mock
    // credentials live in appsettings.Development.json; production values come from Consul
    // KV or environment variables, and a missing credential fails at first use.
    public string InternalEndpoint { get; set; } = "localhost:8333";
    public bool InternalSecure { get; set; }
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "ruoyu-study";
    public string? PublicBaseUrl { get; set; }
}
