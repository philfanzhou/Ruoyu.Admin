namespace Ruoyu.Admin.Common.Oss;

public class LocalFileOssService : IOssService
{
    private readonly string _rootPath;

    public LocalFileOssService(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
        foreach (OssBucket bucket in Enum.GetValues(typeof(OssBucket)))
        {
            var prefix = BucketToPrefix(bucket);
            Directory.CreateDirectory(Path.Combine(_rootPath, prefix));
        }
    }

    public Task<Stream> DownloadAsync(string objectPath)
    {
        var fullPath = GetFullPath(objectPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Object not found: {objectPath}", fullPath);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return Task.FromResult<Stream>(stream);
    }

    public Task<List<OssObjectInfo>> ListObjectsWithBucketAsync(OssBucket bucket, string? prefix = null)
    {
        var bucketPrefix = BucketToPrefix(bucket);
        var fullPrefix = string.IsNullOrWhiteSpace(prefix)
            ? bucketPrefix
            : $"{bucketPrefix}/{prefix}";
        return ListObjectsByPrefixAsync(fullPrefix);
    }

    private Task<List<OssObjectInfo>> ListObjectsByPrefixAsync(string? prefix)
    {
        var searchDir = _rootPath;

        if (!string.IsNullOrEmpty(prefix))
        {
            var currentDir = _rootPath;
            foreach (var part in prefix.Split('/'))
            {
                var nextDir = Path.Combine(currentDir, part);
                if (Directory.Exists(nextDir))
                    currentDir = nextDir;
                else
                    return Task.FromResult(new List<OssObjectInfo>());
            }
            searchDir = currentDir;
        }

        var objects = new List<OssObjectInfo>();
        if (!Directory.Exists(searchDir))
            return Task.FromResult(objects);

        foreach (var file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_rootPath, file).Replace('\\', '/');
            if (!string.IsNullOrEmpty(prefix) && !relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var fi = new FileInfo(file);
            objects.Add(new OssObjectInfo
            {
                ObjectPath = relativePath,
                Size = fi.Length,
                LastModified = fi.LastWriteTimeUtc,
                IsZombie = false
            });
        }

        return Task.FromResult(objects);
    }

    private string GetFullPath(string objectPath)
    {
        var normalized = objectPath.Replace('\\', '/').TrimStart('/');
        return Path.GetFullPath(Path.Combine(_rootPath, normalized));
    }

    private static string BucketToPrefix(OssBucket bucket) => bucket switch
    {
        OssBucket.Uploads => "uploads",
        OssBucket.Mistakes => "mistakes",
        OssBucket.Questions => "questions",
        OssBucket.Documents => "documents",
        _ => "others"
    };
}
