using Admin.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;

namespace Admin.WebApi.Services;

public class OssAuditWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OssAuditWorker> _logger;
    private readonly IConfiguration _configuration;

    public OssAuditWorker(
        IServiceProvider serviceProvider,
        ILogger<OssAuditWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        using (var scope = _serviceProvider.CreateScope())
        {
            var ossService = scope.ServiceProvider.GetRequiredService<IOssService>();
            await CleanupLegacyHomeworkReviewImagesAsync(ossService, _logger, stoppingToken);
            var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await CleanupLegacyHomeworkReviewAuditRecordsAsync(dbContext, _logger, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var scheduledHour = _configuration.GetValue<int>("OssAudit:ScheduledHour", 2);
            var scheduledMinute = _configuration.GetValue<int>("OssAudit:ScheduledMinute", 0);
            var nextRun = now.Date.AddHours(scheduledHour).AddMinutes(scheduledMinute);
            if (nextRun <= now)
                nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            _logger.LogInformation("Next OSS audit scheduled at {NextRun} (in {Delay})", nextRun, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunAuditAsync("scheduled", stoppingToken);
        }
    }

    internal static async Task<int> CleanupLegacyHomeworkReviewImagesAsync(
        IOssService ossService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var objects = await ossService.ListObjectsWithBucketAsync(OssBucket.Uploads, "homework");
        var deletedCount = 0;
        foreach (var obj in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsLegacyHomeworkReviewImagePath(obj.ObjectPath))
                continue;

            if (await ossService.DeleteAsync(obj.ObjectPath))
            {
                deletedCount++;
            }
            else
            {
                logger.LogWarning(
                    "Failed to delete obsolete homework review image {ObjectPath}", obj.ObjectPath);
            }
        }

        logger.LogInformation(
            "Obsolete homework review image cleanup completed. Deleted {Count} objects",
            deletedCount);
        return deletedCount;
    }

    internal static bool IsLegacyHomeworkReviewImagePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 6
            && string.Equals(segments[0], "uploads", StringComparison.Ordinal)
            && string.Equals(segments[1], "homework", StringComparison.Ordinal)
            && Guid.TryParse(segments[2], out var homeworkId)
            && homeworkId != Guid.Empty
            && Guid.TryParse(segments[3], out var studentId)
            && studentId != Guid.Empty
            && string.Equals(segments[4], "reviews", StringComparison.Ordinal)
            && string.Equals(Path.GetExtension(segments[5]), ".jpg", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(Path.GetFileNameWithoutExtension(segments[5]), out var revisionId)
            && revisionId != Guid.Empty;
    }

    internal static async Task<int> CleanupLegacyHomeworkReviewAuditRecordsAsync(
        AuditDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var records = await dbContext.OssAuditRecords.ToListAsync(cancellationToken);
        var obsoleteRecords = records
            .Where(record => IsLegacyHomeworkReviewImagePath(record.ObjectPath))
            .ToList();
        if (obsoleteRecords.Count == 0)
            return 0;

        dbContext.OssAuditRecords.RemoveRange(obsoleteRecords);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Removed {Count} obsolete homework review image audit records",
            obsoleteRecords.Count);
        return obsoleteRecords.Count;
    }

    public async Task RunAuditAsync(string triggerType = "manual", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting OSS audit (trigger: {Trigger})...", triggerType);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        // Create audit run record first (acts as a lock — only one running at a time)
        var auditRun = new OssAuditRun
        {
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = 0,
            TriggerType = triggerType,
        };
        dbContext.OssAuditRuns.Add(auditRun);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another audit may have started concurrently
            _logger.LogWarning("Failed to create audit run record, another audit may be running");
            return;
        }

        // Double-check: if there's another running record (Id != ours), abort
        var otherRunning = await dbContext.OssAuditRuns
            .AnyAsync(r => r.Status == 0 && r.Id != auditRun.Id, cancellationToken);
        if (otherRunning)
        {
            _logger.LogWarning("Another audit is already running, removing this record");
            dbContext.OssAuditRuns.Remove(auditRun);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var ossService = scope.ServiceProvider.GetRequiredService<IOssService>();
            var studentClient = scope.ServiceProvider
                .GetRequiredService<IStudentHttpClient>();
            var mistakeClient = scope.ServiceProvider
                .GetRequiredService<IMistakeHttpClient>();
            var homeworkClient = scope.ServiceProvider.GetRequiredService<HomeworkReferenceClient>();

            HashSet<string> registeredPaths;
            try
            {
                registeredPaths = await GetRegisteredOssPathsAsync(studentClient, cancellationToken);
                _logger.LogInformation("Got {Count} registered paths from Student service (aggregated from upload records)", registeredPaths.Count);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("Student service unavailable, aborting audit: {Message}", ex.Message);
                auditRun.Status = 2;
                auditRun.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                auditRun.ErrorMessage = "Student service unavailable";
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            try
            {
                var homeworkPaths = await homeworkClient.GetAllImagePathsAsync(cancellationToken);
                registeredPaths.UnionWith(homeworkPaths);
                _logger.LogInformation("Got {Count} referenced paths from Homework service", homeworkPaths.Count);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("Homework service unavailable, aborting audit: {Message}", ex.Message);
                auditRun.Status = 2;
                auditRun.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                auditRun.ErrorMessage = "Homework service unavailable";
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            HashSet<string> mistakePaths = new(StringComparer.OrdinalIgnoreCase);
            bool mistakeServiceAvailable = true;
            try
            {
                mistakePaths = await GetMistakeImagePathsAsync(mistakeClient, cancellationToken);
                _logger.LogInformation("Got {Count} referenced paths from Mistake service (aggregated from mistake items)", mistakePaths.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Mistake service unavailable, audit results may have false positives: {Message}", ex.Message);
                mistakeServiceAvailable = false;
            }

            var buckets = new[] { OssBucket.Uploads, OssBucket.Mistakes, OssBucket.Questions };
            var bucketNames = new Dictionary<OssBucket, string>
            {
                { OssBucket.Uploads, "uploads" },
                { OssBucket.Mistakes, "mistakes" },
                { OssBucket.Questions, "questions" },
            };

            int totalNewZombies = 0;

            foreach (var bucket in buckets)
            {
                var bucketName = bucketNames[bucket];
                _logger.LogInformation("Auditing bucket: {Bucket}", bucketName);

                var objects = await ossService.ListObjectsWithBucketAsync(bucket);

                foreach (var obj in objects)
                {
                    var path = obj.ObjectPath;

                    // 跳过缩略图文件：缩略图与原图关联，删原图时自动清理，不应单独标记为僵尸
                    if (ThumbnailHelper.IsThumbnailPath(path))
                        continue;

                    var isInRegistered = registeredPaths.Contains(path);
                    var isInMistake = mistakeServiceAvailable && mistakePaths.Contains(path);

                    if (!isInRegistered && !isInMistake)
                    {
                        var existing = await dbContext.OssAuditRecords
                            .AnyAsync(r => r.ObjectPath == path, cancellationToken);

                        if (!existing)
                        {
                            dbContext.OssAuditRecords.Add(new OssAuditRecord
                            {
                                ObjectPath = path,
                                Bucket = bucketName,
                                Size = obj.Size,
                                LastModified = obj.LastModified.HasValue
                                    ? obj.LastModified.Value.ToUnixTimeSeconds()
                                    : 0,
                                Status = 0,
                                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            });
                            totalNewZombies++;
                        }
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            auditRun.Status = 1;
            auditRun.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            auditRun.NewZombieCount = totalNewZombies;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("OSS audit completed. New zombie objects found: {Count}", totalNewZombies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OSS audit failed with unexpected error");
            auditRun.Status = 2;
            auditRun.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            auditRun.ErrorMessage = ex.Message;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 通过分页获取所有上传记录，本地聚合 image_paths，替代原 GetRegisteredOssPaths 专用接口。
    /// </summary>
    private static async Task<HashSet<string>> GetRegisteredOssPathsAsync(
        IStudentHttpClient client,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            var response = await client.GetAllUploadRecordsAsync(null, page, pageSize, null, cancellationToken);

            foreach (var record in response.Items)
            {
                foreach (var entry in record.ImageEntries)
                {
                    paths.Add(entry.Path);
                }
            }

            if (page * pageSize >= response.TotalCount)
                break;

            page++;
        }

        return paths;
    }

    /// <summary>
    /// 通过分页获取所有错题条目，本地聚合 source_regions.source_image_path，替代原 GetAllReferencedImagePaths 专用接口。
    /// </summary>
    private static async Task<HashSet<string>> GetMistakeImagePathsAsync(
        IMistakeHttpClient client,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            var response = await client.GetMistakeItemListAsync(
                string.Empty, 0, 0, MistakeReviewStatus.Unspecified, page, pageSize, cancellationToken);

            foreach (var item in response.Items)
            {
                foreach (var region in item.SourceRegions)
                {
                    if (!string.IsNullOrWhiteSpace(region.SourceImagePath))
                        paths.Add(region.SourceImagePath);
                }
            }

            var totalCount = response.PageMeta?.TotalCount ?? 0;
            if (page * pageSize >= totalCount)
                break;

            page++;
        }

        return paths;
    }
}
