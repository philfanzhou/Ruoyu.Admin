using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Admin.WebApi.Persistence;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-audit")]
[ApiController, RequireSecurityResponseHeaders]
[Authorize]
public partial class OssAuditController : ControllerBase
{
    private readonly AuditDbContext _dbContext;
    private readonly IStudentHttpClient _studentClient;
    private readonly IMistakeHttpClient _mistakeClient;
    private readonly IOssService _ossService;
    private readonly OssAuditWorker _auditWorker;
    private readonly ILogger<OssAuditController> _logger;
    private readonly HomeworkReferenceClient? _homeworkClient;
    private readonly IStorageReferenceCollector? _references;

    public OssAuditController(
        AuditDbContext dbContext,
        IStudentHttpClient studentClient,
        IMistakeHttpClient mistakeClient,
        IOssService ossService,
        OssAuditWorker auditWorker,
        ILogger<OssAuditController> logger,
        HomeworkReferenceClient? homeworkClient = null,
        IStorageReferenceCollector? references = null)
    {
        _dbContext = dbContext;
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
        _ossService = ossService;
        _auditWorker = auditWorker;
        _logger = logger;
        _homeworkClient = homeworkClient;
        _references = references;
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
            .Where(r => r.Status == 0 || r.Status == 3)
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
                StatusText = r.Status switch { 0 => "Pending", 1 => "Resolved", 2 => "Ignored", 3 => "UnreferencedObservation", _ => "Unknown" },
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
                lastCompleted.ReferenceContractVersion,
                lastCompleted.ReferenceSnapshots,
                deletionAuthorized = false,
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
            observationCount = await _dbContext.OssAuditRecords.CountAsync(r => r.Status == 3),
            deletionAuthorized = false,
        });
    }

    [HttpPost("records/{id}/resolve")]
    public async Task<IActionResult> ResolveRecord(long id)
    {
        var record = await _dbContext.OssAuditRecords.FindAsync(id);
        if (record == null)
            return NotFound(new ErrorResponse("Record not found."));

        return await CleanupGateAsync();
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

        return await CleanupGateAsync();
    }

    private async Task<IActionResult> CleanupGateAsync()
    {
        try
        {
            if (_references is null) throw new InvalidOperationException("Reference collector missing.");
            await _references.CollectAsync(HttpContext?.RequestAborted ?? CancellationToken.None);
        }
        catch (OperationCanceledException) when (HttpContext?.RequestAborted.IsCancellationRequested == true) { throw; }
        catch (Exception)
        {
            return StatusCode(502, new { success = false, errorKind = "references_unavailable" });
        }
        return StatusCode(409, new { success = false, errorKind = "cleanup_not_authorized" });
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
