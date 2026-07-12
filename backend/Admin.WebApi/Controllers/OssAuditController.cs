using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Admin.WebApi.Persistence;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using Ruoyu.Study.Common.Oss;
using Ruoyu.Study.MistakeBff.GrpcClients;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-audit")]
[ApiController]
public partial class OssAuditController : ControllerBase
{
    private readonly AuditDbContext _dbContext;
    private readonly IStudentHttpClient _studentClient;
    private readonly IMistakeHttpClient _mistakeClient;
    private readonly IOssService _ossService;
    private readonly OssAuditWorker _auditWorker;
    private readonly ILogger<OssAuditController> _logger;

    public OssAuditController(
        AuditDbContext dbContext,
        IStudentHttpClient studentClient,
        IMistakeHttpClient mistakeClient,
        IOssService ossService,
        OssAuditWorker auditWorker,
        ILogger<OssAuditController> logger)
    {
        _dbContext = dbContext;
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
        _ossService = ossService;
        _auditWorker = auditWorker;
        _logger = logger;
    }

    [HttpGet("records")]
    public async Task<IActionResult> GetRecords(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? status = null,
        [FromQuery] string? bucket = null)
    {
        var query = _dbContext.OssAuditRecords.AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(bucket))
            query = query.Where(r => r.Bucket == bucket);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var statusCounts = await _dbContext.OssAuditRecords
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        var bucketCounts = await _dbContext.OssAuditRecords
            .Where(r => r.Status == 0)
            .GroupBy(r => r.Bucket)
            .Select(g => new { Bucket = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Bucket, x => x.Count);

        return Ok(new
        {
            items = items.Select(r => new
            {
                r.Id,
                r.ObjectPath,
                r.Bucket,
                r.Size,
                r.LastModified,
                Status = r.Status,
                StatusText = r.Status switch { 0 => "Pending", 1 => "Resolved", 2 => "Ignored", _ => "Unknown" },
                r.CreatedAt,
                r.ResolvedAt,
                r.Note,
            }),
            totalCount,
            page,
            pageSize,
            statusCounts,
            bucketCounts,
        });
    }

    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerAudit()
    {
        var isRunning = await _dbContext.OssAuditRuns.AnyAsync(r => r.Status == 0);
        if (isRunning)
            return BadRequest(new ErrorResponse("审计正在运行中，请等待完成后再触发。"));

        _ = Task.Run(() => _auditWorker.RunAuditAsync("manual"));
        return Ok(new OperationResponse(true, "审计已触发，请稍候查看结果。"));
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var isRunning = await _dbContext.OssAuditRuns.AnyAsync(r => r.Status == 0);

        var lastCompleted = await _dbContext.OssAuditRuns
            .Where(r => r.Status == 1)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync();

        var lastFailed = await _dbContext.OssAuditRuns
            .Where(r => r.Status == 2)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync();

        var pendingCount = await _dbContext.OssAuditRecords.CountAsync(r => r.Status == 0);

        return Ok(new
        {
            isRunning,
            lastCompleted = lastCompleted == null ? null : new
            {
                lastCompleted.Id,
                StartedAt = lastCompleted.StartedAt,
                CompletedAt = lastCompleted.CompletedAt,
                DurationSeconds = lastCompleted.CompletedAt.HasValue
                    ? lastCompleted.CompletedAt.Value - lastCompleted.StartedAt
                    : (long?)null,
                lastCompleted.NewZombieCount,
                lastCompleted.TriggerType,
            },
            lastFailed = lastFailed == null ? null : new
            {
                lastFailed.Id,
                StartedAt = lastFailed.StartedAt,
                CompletedAt = lastFailed.CompletedAt,
                lastFailed.ErrorMessage,
                lastFailed.TriggerType,
            },
            pendingCount,
        });
    }

    [HttpPost("records/{id}/resolve")]
    public async Task<IActionResult> ResolveRecord(long id)
    {
        var record = await _dbContext.OssAuditRecords.FindAsync(id);
        if (record == null)
            return NotFound(new ErrorResponse("Record not found."));

        // Only validate references for Pending records;
        // Resolved records already had their OSS files deleted, skip re-validation
        if (record.Status == 0)
        {
            try
            {
                var registeredPaths = await GetRegisteredOssPathsAsync();
                if (registeredPaths.Contains(record.ObjectPath))
                    return BadRequest(new ErrorResponse("该文件仍被上传记录引用，不能删除。"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除前无法验证 Student 服务引用");
                return StatusCode(502, new ErrorResponse("Student 服务不可用，无法安全删除。"));
            }

            try
            {
                var mistakePaths = await GetMistakeImagePathsAsync();
                if (mistakePaths.Contains(record.ObjectPath))
                    return BadRequest(new ErrorResponse("该文件仍被错题记录引用，不能删除。"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除前无法验证 Mistake 服务引用");
                return StatusCode(502, new ErrorResponse("Mistake 服务不可用，无法安全删除。"));
            }
        }

        try
        {
            await _ossService.DeleteAsync(record.ObjectPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete OSS object");
            return StatusCode(500, new ErrorResponse($"Failed to delete object: {ex.Message}"));
        }

        _dbContext.OssAuditRecords.Remove(record);
        await _dbContext.SaveChangesAsync();

        return Ok(new OperationResponse(true, "Record, OSS object and fingerprint deleted."));
    }

    [HttpPost("records/{id}/ignore")]
    public async Task<IActionResult> IgnoreRecord(long id, [FromBody] IgnoreRecordRequest? body)
    {
        var record = await _dbContext.OssAuditRecords.FindAsync(id);
        if (record == null)
            return NotFound(new ErrorResponse("Record not found."));

        if (record.Status != 0)
            return BadRequest(new ErrorResponse("Only pending records can be ignored."));

        record.Status = 2;
        record.ResolvedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        record.Note = body?.Note;
        await _dbContext.SaveChangesAsync();

        return Ok(new OperationResponse(true, "Record ignored."));
    }

    [HttpPost("records/batch-resolve")]
    public async Task<IActionResult> BatchResolve([FromBody] BatchResolveRequest request)
    {
        if (request.Ids == null || request.Ids.Count == 0)
            return BadRequest(new ErrorResponse("At least one record ID is required."));

        var records = await _dbContext.OssAuditRecords
            .Where(r => request.Ids.Contains(r.Id) && r.Status != 2)
            .ToListAsync();

        if (records.Count == 0)
            return Ok(new { resolvedCount = 0, errors = new List<string>() });

        // Only fetch reference paths for Pending records that need validation
        var pendingRecords = records.Where(r => r.Status == 0).ToList();
        HashSet<string> registeredPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> mistakePaths = new(StringComparer.OrdinalIgnoreCase);

        if (pendingRecords.Count > 0)
        {
            try
            {
                registeredPaths = await GetRegisteredOssPathsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除前无法验证 Student 服务引用");
                return StatusCode(502, new ErrorResponse("Student 服务不可用，无法安全删除。"));
            }

            try
            {
                mistakePaths = await GetMistakeImagePathsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除前无法验证 Mistake 服务引用");
                return StatusCode(502, new ErrorResponse("Mistake 服务不可用，无法安全删除。"));
            }
        }

        int resolvedCount = 0;
        var errors = new List<string>();

        foreach (var record in records)
        {
            // Only check references for Pending records; Resolved records already had files deleted
            if (record.Status == 0)
            {
                if (registeredPaths.Contains(record.ObjectPath))
                {
                    errors.Add($"{record.ObjectPath}: 被上传记录引用，跳过");
                    continue;
                }
                if (mistakePaths.Contains(record.ObjectPath))
                {
                    errors.Add($"{record.ObjectPath}: 被错题记录引用，跳过");
                    continue;
                }
            }

            try
            {
                await _ossService.DeleteAsync(record.ObjectPath);
                _dbContext.OssAuditRecords.Remove(record);
                resolvedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"{record.ObjectPath}: {ex.Message}");
            }
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { resolvedCount, errors, totalRequested = request.Ids.Count });
    }
}

public class IgnoreRecordRequest
{
    public string? Note { get; set; }
}

public class BatchResolveRequest
{
    public List<long> Ids { get; set; } = new();
}

// Partial class extension for OssAuditController helper methods
public partial class OssAuditController
{
    /// <summary>
    /// 通过分页获取所有上传记录，本地聚合 image_paths，替代原 GetRegisteredOssPaths 专用接口。
    /// </summary>
    private async Task<HashSet<string>> GetRegisteredOssPathsAsync()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            var response = await _studentClient.GetAllUploadRecordsAsync(null, page, pageSize, null);

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
    private async Task<HashSet<string>> GetMistakeImagePathsAsync()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            var response = await _mistakeClient.GetMistakeItemListAsync(
                string.Empty, 0, 0, MistakeReviewStatus.Unspecified, page, pageSize);

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
