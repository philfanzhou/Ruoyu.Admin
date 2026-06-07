using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Admin.WebApi.Data;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-audit")]
[ApiController]
public class OssAuditController : ControllerBase
{
    private readonly AuditDbContext _dbContext;
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _studentClient;
    private readonly MProto.MistakeGrpcService.MistakeGrpcServiceClient _mistakeClient;
    private readonly OssAuditWorker _auditWorker;
    private readonly ILogger<OssAuditController> _logger;

    public OssAuditController(
        AuditDbContext dbContext,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient studentClient,
        MProto.MistakeGrpcService.MistakeGrpcServiceClient mistakeClient,
        OssAuditWorker auditWorker,
        ILogger<OssAuditController> logger)
    {
        _dbContext = dbContext;
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
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
        _ = Task.Run(() => _auditWorker.RunAuditAsync());
        return Ok(new OperationResponse(true, "Audit triggered. Results will be available shortly."));
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
                var pathsResponse = await _studentClient.GetRegisteredOssPathsAsync(new SProto.Empty());
                var registeredPaths = new HashSet<string>(pathsResponse.Paths, StringComparer.OrdinalIgnoreCase);
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
                var mistakeResponse = await _mistakeClient.GetAllReferencedImagePathsAsync(new MProto.Empty());
                var mistakePaths = new HashSet<string>(mistakeResponse.ImagePaths, StringComparer.OrdinalIgnoreCase);
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
            // DeleteOssObject will also delete associated fingerprint data
            var response = await _studentClient.DeleteOssObjectAsync(
                new SProto.DeleteOssObjectRequest { ObjectPath = record.ObjectPath });
            if (!response.Success)
                return BadRequest(new ErrorResponse(response.ErrorMessage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete OSS object and fingerprint");
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
                var pathsResponse = await _studentClient.GetRegisteredOssPathsAsync(new SProto.Empty());
                registeredPaths = new HashSet<string>(pathsResponse.Paths, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除前无法验证 Student 服务引用");
                return StatusCode(502, new ErrorResponse("Student 服务不可用，无法安全删除。"));
            }

            try
            {
                var mistakeResponse = await _mistakeClient.GetAllReferencedImagePathsAsync(new MProto.Empty());
                mistakePaths = new HashSet<string>(mistakeResponse.ImagePaths, StringComparer.OrdinalIgnoreCase);
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
                // DeleteOssObject will also delete associated fingerprint data (idempotent)
                var response = await _studentClient.DeleteOssObjectAsync(
                    new SProto.DeleteOssObjectRequest { ObjectPath = record.ObjectPath });
                if (response.Success)
                {
                    _dbContext.OssAuditRecords.Remove(record);
                    resolvedCount++;
                }
                else
                {
                    errors.Add($"{record.ObjectPath}: {response.ErrorMessage}");
                }
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
