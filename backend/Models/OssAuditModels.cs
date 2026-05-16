namespace Admin.WebApi.Models;

public record OssObjectInfoDto(
    string ObjectPath,
    long Size,
    long? LastModified,
    bool IsZombie);

public record OssBucketAuditResultDto(
    int Bucket,
    int TotalObjects,
    long TotalSize,
    List<OssObjectInfoDto> ZombieObjects,
    long ZombieSize);

public record OssAuditResultDto(
    long AuditTime,
    List<OssBucketAuditResultDto> BucketResults,
    int TotalZombieObjects,
    long TotalZombieSize);

public record DeleteZombieObjectsRequest(
    List<string> ObjectPaths);

public record DeleteZombieObjectsResponse(
    int DeletedCount);
