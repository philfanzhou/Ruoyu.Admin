using Admin.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using System.Text.Json;

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

    /// <summary>
    /// Buckets the audit is allowed to scan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only buckets covered by all required reference providers may produce observations.
    /// An observation never authorizes deletion, including historical pending records.
    /// </para>
    /// <para>
    /// <see cref="OssBucket.Questions"/> is deliberately excluded: QuestionBank owns
    /// <c>questions/</c> but publishes no reference query, so scanning it flagged every question
    /// image as unreferenced. <see cref="OssBucket.Documents"/> is excluded because DocLibrary
    /// delegates original and artifact storage to StructaDoc. Re-adding either requires landing
    /// its reference source in the same change.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyList<OssBucket> AuditedBuckets = new[]
    {
        OssBucket.Uploads,
        OssBucket.Mistakes,
    };

    /// <summary>
    /// Persisted <see cref="OssAuditRecord.Bucket"/> value for each audited bucket.
    /// </summary>
    internal static readonly IReadOnlyDictionary<OssBucket, string> AuditedBucketNames =
        new Dictionary<OssBucket, string>
        {
            { OssBucket.Uploads, "uploads" },
            { OssBucket.Mistakes, "mistakes" },
        };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

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

    internal static Task<int> CleanupLegacyHomeworkReviewImagesAsync(
        IOssService ossService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Snapshot observations never authorize deletion of objects or audit history.
        return Task.FromResult(0);
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

    internal static Task<int> CleanupLegacyHomeworkReviewAuditRecordsAsync(
        AuditDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Snapshot observations never authorize deletion of objects or audit history.
        return Task.FromResult(0);
    }

    /// <summary>
    /// Preserves historical audit records whose bucket is no longer audited.
    /// </summary>
    /// <remarks>
    /// Startup cannot infer deletion permission from a bucket or an old observation.
    /// The common collector and fixed v1 authorization gate reject all resolve requests.
    /// </remarks>
    internal static Task<int> CleanupUnauditedBucketAuditRecordsAsync(
        AuditDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Snapshot observations never authorize deletion of objects or audit history.
        return Task.FromResult(0);
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

        try
        {
            // Once the run is durable, cancellation during the concurrency check must
            // reach the same terminal-state handler as collection or object listing.
            var otherRunning = await dbContext.OssAuditRuns
                .AnyAsync(r => r.Status == 0 && r.Id != auditRun.Id, cancellationToken);
            if (otherRunning)
            {
                _logger.LogWarning("Another audit is already running, removing this record");
                dbContext.OssAuditRuns.Remove(auditRun);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var ossService = scope.ServiceProvider.GetRequiredService<IOssService>();
            var references = await scope.ServiceProvider.GetRequiredService<IStorageReferenceCollector>()
                .CollectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            auditRun.ReferenceSnapshots = JsonSerializer.Serialize(references.Snapshots, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            auditRun.ReferenceContractVersion = "storage-references-v1";
            var registeredPaths = references.Keys;
            // Persist all new observations and completion together; no partial scan is published.
            await using var scanTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            int totalNewZombies = 0;

            foreach (var bucket in AuditedBuckets)
            {
                var bucketName = AuditedBucketNames[bucket];
                _logger.LogInformation("Auditing bucket: {Bucket}", bucketName);

                var objects = await ossService.ListObjectsWithBucketAsync(bucket).WaitAsync(cancellationToken);

                foreach (var obj in objects)
                {
                    var path = obj.ObjectPath;

                    // 跳过缩略图文件：缩略图与原图关联，删原图时自动清理，不应单独标记为僵尸
                    if (ThumbnailHelper.IsThumbnailPath(path))
                        continue;

                    var isInRegistered = registeredPaths.Contains(path);

                    if (!isInRegistered)
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
                                Status = 3,
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
            await scanTransaction.CommitAsync(cancellationToken);

            _logger.LogInformation("OSS audit completed. New unreferenced observations: {Count}", totalNewZombies);
        }
        catch (Exception)
        {
            _logger.LogError("OSS audit failed before a complete observation was committed");
            dbContext.ChangeTracker.Clear();
            dbContext.Attach(auditRun);
            auditRun.Status = 2;
            auditRun.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            auditRun.ErrorMessage = cancellationToken.IsCancellationRequested ? "audit_cancelled" : "references_or_scan_unavailable";
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

}
