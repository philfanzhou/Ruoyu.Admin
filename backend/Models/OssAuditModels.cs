namespace Admin.WebApi.Models;

public class OssAuditResultDto
{
    public long AuditTime { get; set; }
    public List<OssBucketAuditResultDto> BucketResults { get; set; } = new();
    public int TotalZombieObjects { get; set; }
    public long TotalZombieSize { get; set; }
    public bool MistakeServiceAvailable { get; set; }
    public int RegisteredPathsCount { get; set; }
    public int MistakeReferencedPathsCount { get; set; }
    public List<string> Warnings { get; set; } = new();

    public OssAuditResultDto(
        long auditTime,
        List<OssBucketAuditResultDto> bucketResults,
        int totalZombieObjects,
        long totalZombieSize,
        bool mistakeServiceAvailable,
        int registeredPathsCount,
        int mistakeReferencedPathsCount,
        List<string> warnings)
    {
        AuditTime = auditTime;
        BucketResults = bucketResults;
        TotalZombieObjects = totalZombieObjects;
        TotalZombieSize = totalZombieSize;
        MistakeServiceAvailable = mistakeServiceAvailable;
        RegisteredPathsCount = registeredPathsCount;
        MistakeReferencedPathsCount = mistakeReferencedPathsCount;
        Warnings = warnings;
    }
}

public class OssBucketAuditResultDto
{
    public int Bucket { get; set; }
    public int TotalObjects { get; set; }
    public long TotalSize { get; set; }
    public List<OssObjectInfoDto> ZombieObjects { get; set; } = new();
    public long ZombieSize { get; set; }

    public OssBucketAuditResultDto(
        int bucket,
        int totalObjects,
        long totalSize,
        List<OssObjectInfoDto> zombieObjects,
        long zombieSize)
    {
        Bucket = bucket;
        TotalObjects = totalObjects;
        TotalSize = totalSize;
        ZombieObjects = zombieObjects;
        ZombieSize = zombieSize;
    }
}

public class OssObjectInfoDto
{
    public string ObjectPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public long? LastModified { get; set; }
    public bool IsZombie { get; set; }

    public OssObjectInfoDto(string objectPath, long size, long? lastModified, bool isZombie)
    {
        ObjectPath = objectPath;
        Size = size;
        LastModified = lastModified;
        IsZombie = isZombie;
    }
}

public class DeleteZombieObjectsRequest
{
    public List<string> ObjectPaths { get; set; } = new();
}
