using Grpc.Core;
using Admin.WebApi.Data;
using Microsoft.EntityFrameworkCore;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;

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
            var studentClient = scope.ServiceProvider
                .GetRequiredService<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient>();
            var mistakeClient = scope.ServiceProvider
                .GetRequiredService<MProto.MistakeGrpcService.MistakeGrpcServiceClient>();

            HashSet<string> registeredPaths;
            try
            {
                var regResponse = await studentClient.GetRegisteredOssPathsAsync(
                    new SProto.Empty(), cancellationToken: cancellationToken);
                registeredPaths = new HashSet<string>(regResponse.Paths, StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("Got {Count} registered paths from Student service", registeredPaths.Count);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                _logger.LogError("Student service unavailable, aborting audit: {Message}", ex.Message);
                auditRun.Status = 2;
                auditRun.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                auditRun.ErrorMessage = "Student service unavailable";
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            HashSet<string> mistakePaths = new(StringComparer.OrdinalIgnoreCase);
            bool mistakeServiceAvailable = true;
            try
            {
                var mistakeResponse = await mistakeClient.GetAllReferencedImagePathsAsync(
                    new MProto.Empty(), cancellationToken: cancellationToken);
                mistakePaths = new HashSet<string>(mistakeResponse.ImagePaths, StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("Got {Count} referenced paths from Mistake service", mistakePaths.Count);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                _logger.LogWarning("Mistake service unavailable, audit results may have false positives: {Message}", ex.Message);
                mistakeServiceAvailable = false;
            }

            var buckets = new[] { SProto.OssBucket.Uploads, SProto.OssBucket.Mistakes, SProto.OssBucket.Questions };
            var bucketNames = new Dictionary<SProto.OssBucket, string>
            {
                { SProto.OssBucket.Uploads, "uploads" },
                { SProto.OssBucket.Mistakes, "mistakes" },
                { SProto.OssBucket.Questions, "questions" },
            };

            int totalNewZombies = 0;

            foreach (var bucket in buckets)
            {
                var bucketName = bucketNames[bucket];
                _logger.LogInformation("Auditing bucket: {Bucket}", bucketName);

                await foreach (var obj in ListOssObjectsPaged(studentClient, bucket, cancellationToken))
                {
                    var path = obj.ObjectPath;
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
                                LastModified = obj.LastModified,
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

    private async IAsyncEnumerable<SProto.OssObjectInfo> ListOssObjectsPaged(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient client,
        SProto.OssBucket bucket,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await client.ListOssObjectsAsync(
            new SProto.ListOssObjectsRequest { Bucket = bucket },
            cancellationToken: cancellationToken);

        foreach (var obj in response.Objects)
            yield return obj;
    }
}
