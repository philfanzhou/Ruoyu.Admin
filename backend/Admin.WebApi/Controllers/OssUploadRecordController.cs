using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruoyu.Admin.ServiceClients;
using Admin.WebApi.Models;
using Ruoyu.Admin.Common.Oss;
using Admin.WebApi.Services;
using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-upload-records")]
[ApiController, RequireSecurityResponseHeaders]
[Authorize]
public class OssUploadRecordController : ControllerBase
{
    private readonly IStudentHttpClient _studentClient;
    private readonly IMistakeHttpClient _mistakeClient;
    private readonly ILogger<OssUploadRecordController> _logger;
    private readonly IOssService _ossService;
    private readonly ManagedAssignmentOptions _managed;
    private readonly IManagedMistakeHttpClient? _managedClient;

    public OssUploadRecordController(
        IStudentHttpClient studentClient,
        IMistakeHttpClient mistakeClient,
        ILogger<OssUploadRecordController> logger,
        IOssService ossService,
        ManagedAssignmentOptions? managed = null,
        IManagedMistakeHttpClient? managedClient = null)
    {
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
        _logger = logger;
        _ossService = ossService;
        _managed = managed ?? new(false);
        _managedClient = managedClient;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUploadRecords([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int status = -1, [FromQuery] string? studentId = null)
    {
        try
        {
            var response = await _studentClient.GetAllUploadRecordsAsync(status >= 0 ? status : (int?)null, page, pageSize, studentId);
            if (_managed.Enabled && (!response.HasItems || response.Items is null
                || response.Items.Any(r => r is null || !HasManagedMetadata(r))))
                return StatusCode(502, new { errorKind = "invalid_source_metadata", message = "Upload metadata is unavailable." });

            // 收集所有唯一的 studentId，批量查询学生姓名
            var studentIds = response.Items.Select(r => r.StudentId).Distinct().ToList();
            var studentNameMap = new Dictionary<string, string>();

            foreach (var sid in studentIds)
            {
                try
                {
                    var student = await _studentClient.GetStudentAsync(sid);
                    studentNameMap[sid] = student.Name;
                }
                catch
                {
                    studentNameMap[sid] = sid; // 如果查不到学生，回退显示 studentId
                }
            }

            return Ok(new
            {
                items = response.Items.Select(r => new
                {
                    id = r.Id,
                    studentId = r.StudentId,
                    studentName = studentNameMap.GetValueOrDefault(r.StudentId, r.StudentId),
                    status = r.Status,
                    imagePaths = r.ImageEntries.Select(e => e.Path).ToList(),
                    imageEntries = r.ImageEntries.Select(e => new { path = e.Path, type = e.Type }).ToList(),
                    contentRevision = r.ContentRevision,
                    assignmentProtocol = _managed.Enabled ? "managed-v1" : "legacy",
                    comments = r.Comments,
                    createdAt = ParseTimestampToUnixSeconds(r.CreatedAt),
                    updatedAt = ParseTimestampToUnixSeconds(r.UpdatedAt)
                }),
                totalCount = response.TotalCount,
                page = response.Page,
                pageSize = response.PageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get upload records");
            return StatusCode(500, new ErrorResponse("Failed to get upload records"));
        }
    }

    [HttpPost("{id}/reset-status")]
    public async Task<IActionResult> ResetUploadRecordStatus(string id, [FromBody] ResetStatusRequest request)
    {
        try
        {
            var response = await _studentClient.ResetUploadRecordStatusAsync(request.StudentId, id, request.TargetStatus);

            if (!response.Success)
            {
                return BadRequest(new ErrorResponse("Failed to reset status"));
            }

            return Ok(new { success = true, message = "Status reset successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset upload record status: {RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to reset upload record status"));
        }
    }

    [HttpPost("{id}/assign")]
    public async Task<IActionResult> AssignUploadRecord(string id, [FromBody] AssignUploadRecordRequest request)
    {
        if (_managed.Enabled || request.Mode is not null || request.ExpectedContentRevision.HasValue
            || request.Assignments?.Any(a => a is not null && (a.RequestKey.HasValue || a.SourcePaths is not null)) == true)
            return await AssignManagedAsync(id, request);
        if (request.Assignments is not { Count: > 0 })
            return BadRequest(new ErrorResponse("Assignments are required."));
        try
        {
            // 获取上传记录
            var record = await _studentClient.GetUploadRecordAsync(request.StudentId, id);

            if (string.IsNullOrEmpty(record.Id))
            {
                return BadRequest(new ErrorResponse("Upload record not found"));
            }

            // 按图片粒度分配 - 每个分组可以有独立的 subject 和 grade
            var warnings = new List<string>();
            var allSuccess = true;
            var createdItems = new List<object>();
            var assignedImagePaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var assignment in request.Assignments)
            {
                // 获取指定索引的图片路径
                var selectedImagePaths = assignment.ImageIndices
                    .Where(idx => idx >= 0 && idx < record.ImageEntries.Count)
                    .Select(idx => record.ImageEntries[idx].Path)
                    .ToList();

                if (selectedImagePaths.Count == 0)
                {
                    allSuccess = false;
                    warnings.Add($"No valid images found for assignment with indices: {string.Join(",", assignment.ImageIndices)}");
                    continue;
                }

                // 调用 Mistake 服务创建错题
                var submitResponse = await _mistakeClient.SubmitMistakeUploadAsync(
                    request.StudentId,
                    assignment.Subject,
                    assignment.Grade,
                    selectedImagePaths,
                    assignment.Comments ?? record.Comments ?? string.Empty,
                    id);

                if (!submitResponse.Success || submitResponse.CreatedItemIds.Count == 0)
                {
                    allSuccess = false;
                    warnings.Add($"Failed to create mistake for images {string.Join(",", assignment.ImageIndices)}: {submitResponse.ErrorMessage}");
                }
                else
                {
                    _logger.LogInformation("Successfully created mistake items {ItemIds} for upload record {RecordId}",
                        string.Join(",", submitResponse.CreatedItemIds), id);

                    foreach (var itemId in submitResponse.CreatedItemIds)
                    {
                        createdItems.Add(new
                        {
                            subject = assignment.Subject,
                            grade = assignment.Grade,
                            itemId = itemId,
                            imageCount = selectedImagePaths.Count
                        });
                    }
                    foreach (var p in selectedImagePaths)
                    {
                        assignedImagePaths.Add(p);
                    }
                }
            }

            var remainingImageCount = record.ImageEntries.Count(p => !assignedImagePaths.Contains(p.Path));
            var totalImageCount = record.ImageEntries.Count;

            object payload;
            if (warnings.Count > 0)
            {
                payload = new
                {
                    success = allSuccess,
                    message = allSuccess ? "Assignment successful" : "Assignment completed with warnings",
                    warnings,
                    createdItems,
                    remainingImageCount,
                    totalImageCount
                };
            }
            else
            {
                payload = new
                {
                    success = true,
                    message = "Assignment successful",
                    createdItems,
                    remainingImageCount,
                    totalImageCount
                };
            }

            return Ok(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign upload record: {RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to assign upload record"));
        }
    }

    private static bool HasManagedMetadata(UploadRecordDto record) => Guid.TryParse(record.Id, out var source) && source != Guid.Empty
        && Guid.TryParse(record.StudentId, out var student) && student != Guid.Empty
        && record.ContentRevision is { } revision && revision != Guid.Empty && record.HasImageEntries
        && record.ImageEntries is not null && record.ImageEntries.All(e => e is not null
            && !string.IsNullOrEmpty(e.Path) && e.Path.Length <= 4096 && !string.IsNullOrEmpty(e.Type));

    private async Task<IActionResult> AssignManagedAsync(string id, AssignUploadRecordRequest request)
    {
        if (!_managed.Enabled) return StatusCode(503, new { errorKind = "managed_assignment_disabled" });
        var caller = await ManagedCallerAsync();
        if (caller.StatusCode != 200) return StatusCode(caller.StatusCode, new { errorKind = caller.Error });
        if (request.Mode != "managed-v1" || !Guid.TryParse(id, out var source) || source == Guid.Empty
            || !Guid.TryParse(request.StudentId, out var student) || student == Guid.Empty
            || request.ExpectedContentRevision is not { } revision || revision == Guid.Empty
            || request.Assignments is not { Count: >= 1 and <= 100 }
            || request.Assignments.Any(a => a is null || a.RequestKey is null || a.RequestKey == Guid.Empty
                || a.Subject is < 1 or > 9 || a.Grade is < 1 or > 12 || a.Comments?.Length > 4096
                || a.SourcePaths is not { Count: >= 1 and <= 100 }
                || a.SourcePaths.Any(path => string.IsNullOrEmpty(path) || path.Length > 4096))
            || request.Assignments.Select(a => a.RequestKey).Distinct().Count() != request.Assignments.Count)
            return BadRequest(new { errorKind = "invalid_assignment", message = "The fixed managed assignment is invalid." });
        if (_managedClient is null) throw new InvalidOperationException("Managed client is not registered.");
        var groups = new List<object>(); var stopped = false;
        foreach (var assignment in request.Assignments)
        {
            var result = stopped ? new ManagedMistakeResult("NotAttempted", "", Array.Empty<Guid>())
                : await _managedClient.SubmitAsync(new(source, revision, assignment.RequestKey!.Value, student,
                    assignment.Subject, assignment.Grade, assignment.SourcePaths!.Distinct(StringComparer.Ordinal).ToArray(),
                    assignment.Comments ?? string.Empty), caller.AccessToken!, HttpContext.RequestAborted);
            groups.Add(new { requestKey = assignment.RequestKey, state = result.State,
                errorKind = result.ErrorKind, createdItemIds = result.CreatedItemIds, statusCode = result.HttpStatus });
            if (result.State != "Completed") stopped = true;
        }
        return Ok(new { success = !stopped, groups });
    }

    private async Task<AdminSessionResult> ManagedCallerAsync()
    {
        // The session boundary already performed the administrator and unsafe-method CSRF gate.
        var settings = HttpContext.RequestServices.GetService<AdminOidcSettings>();
        if (settings?.UseSessionForAdminApi == true)
            return HttpContext.Items.TryGetValue(AdminSessionBoundary.TrustedSessionKey, out var value)
                && value is AdminSessionResult trusted && trusted.StatusCode == 200
                ? trusted : new(401);
        var bearer = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!bearer.Succeeded || bearer.Principal is null) return new(401);
        if (!bearer.Principal.IsInRole("admin")) return new(403);
        var token = bearer.Properties?.GetTokenValue("access_token");
        if (!HttpContext.Request.Headers.TryGetValue("Authorization", out var header) || header.Count != 1
            || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(header[0], out var authorization)
            || !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(token) || authorization.Parameter != token) return new(401);
        return new(200, bearer.Principal, token);
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetImage([FromQuery] string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new ErrorResponse("Path is required"));
            }

            using var stream = await _ossService.DownloadAsync(path);
            if (stream == null)
            {
                return NotFound(new ErrorResponse("Image not found"));
            }

            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var data = memoryStream.ToArray();

            if (data.Length == 0)
            {
                return NotFound(new ErrorResponse("Image not found"));
            }

            var contentType = GetContentType(path);
            return File(data, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get image: {Path}", path);
            return StatusCode(500, new ErrorResponse("Failed to get image"));
        }
    }

    [HttpPost("{id}/rotate")]
    public async Task<IActionResult> RotateImage(string id, [FromBody] RotateImageRequest request)
    {
        if (!Guid.TryParse(id, out var recordId))
        {
            return BadRequest(new ErrorResponse("Invalid record ID"));
        }

        if (string.IsNullOrWhiteSpace(request.StudentId))
        {
            return BadRequest(new ErrorResponse("StudentId is required"));
        }

        // A downstream failure no longer relays its message as a 400: it answers this fixed
        // local result. Any other unexpected failure escapes to the Problem Details boundary.
        try
        {
            await _studentClient.RotateUploadImageAsync(request.StudentId, id, request.ImageIndex, request.Rotation);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to rotate image: RecordId={RecordId}", id);
            return BadRequest(new ErrorResponse("Failed to rotate image"));
        }

        return Ok(new { success = true });
    }

    [HttpDelete("{id}/images/{imageIndex}")]
    public async Task<IActionResult> RemoveImageFromRecord(string id, int imageIndex, [FromQuery] string studentId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(studentId))
            {
                return BadRequest(new ErrorResponse("studentId is required"));
            }

            var response = await _studentClient.RemoveImageFromRecordAsync(studentId, id, imageIndex);

            if (!response.Success)
            {
                return BadRequest(new ErrorResponse(response.Message ?? "Failed to remove image"));
            }

            _logger.LogInformation(
                "Removed image at index {Index} from record {RecordId}, record deleted: {Deleted}, remaining: {Remaining}",
                imageIndex, id, response.RecordDeleted, response.RemainingImageCount);

            return Ok(new
            {
                success = true,
                message = response.Message,
                recordDeleted = response.RecordDeleted,
                remainingImageCount = response.RemainingImageCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove image from record: {RecordId}, index: {Index}", id, imageIndex);
            return StatusCode(500, new ErrorResponse("Failed to remove image"));
        }
    }

    [HttpGet("legacy-check/{id}")]
    public async Task<IActionResult> LegacyCheck(string id)
    {
        try
        {
            var mistakeList = await _mistakeClient.GetMistakeItemsByUploadAsync(id);

            if (mistakeList.Items.Count == 0)
            {
                return Ok(new { isLegacy = false, message = "该记录没有关联错题" });
            }

            var allReviewed = mistakeList.Items.All(i => i.ReviewStatus == MistakeReviewStatus.Confirmed ||
                                                          i.ReviewStatus == MistakeReviewStatus.Rejected);
            var pendingCount = mistakeList.Items.Count(i => i.ReviewStatus == MistakeReviewStatus.PendingReview);

            if (allReviewed && pendingCount == 0)
            {
                var hasUploadPathImage = mistakeList.Items.SelectMany(i => i.SourceRegions)
                    .Any(r => !string.IsNullOrWhiteSpace(r.SourceImagePath) &&
                              r.SourceImagePath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase));

                return Ok(new
                {
                    isLegacy = true,
                    mistakeCount = mistakeList.Items.Count,
                    hasUploadPathImage,
                    message = "该记录的错题已全部审核完毕，但上传记录仍存在，属于遗留数据"
                });
            }

            return Ok(new
            {
                isLegacy = false,
                pendingCount,
                message = $"该记录还有{pendingCount}条错题待审核"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check legacy status for upload record: {RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to check legacy status"));
        }
    }

    /// <summary>
    /// 运维兜底接口：清理审核完毕但上传记录未删除的遗留数据。
    /// 在目标流程中，每次审核后 gRPC 层会自动编排状态检查和清理，
    /// 正常流程不应遗留需要此接口处理的场景。
    /// </summary>
    [HttpPost("legacy-clean/{id}")]
    public async Task<IActionResult> LegacyClean(string id)
    {
        try
        {
            _logger.LogInformation("Starting legacy data cleanup for upload record: {RecordId}", id);

            var mistakeList = await _mistakeClient.GetMistakeItemsByUploadAsync(id);

            if (mistakeList.Items.Count == 0)
            {
                return BadRequest(new ErrorResponse("该记录没有关联错题，无法清理"));
            }

            var allReviewed = mistakeList.Items.All(i => i.ReviewStatus == MistakeReviewStatus.Confirmed ||
                                                          i.ReviewStatus == MistakeReviewStatus.Rejected);
            var hasPending = mistakeList.Items.Any(i => i.ReviewStatus == MistakeReviewStatus.PendingReview);

            if (!allReviewed || hasPending)
            {
                var pendingCount = mistakeList.Items.Count(i => i.ReviewStatus == MistakeReviewStatus.PendingReview);
                return BadRequest(new ErrorResponse($"该记录还有{pendingCount}条错题待审核，无法清理"));
            }

            var studentId = mistakeList.Items.FirstOrDefault()?.StudentId ?? "";
            if (string.IsNullOrWhiteSpace(studentId))
            {
                return BadRequest(new ErrorResponse("无法找到该上传记录的学生ID"));
            }

            _logger.LogInformation("Step 1: Calling CompleteUploadReview for {RecordId}", id);
            var completeReviewResult = await _mistakeClient.CompleteUploadReviewAsync(
                id,
                "admin-legacy-cleanup");

            if (!completeReviewResult.Success)
            {
                _logger.LogWarning("CompleteUploadReview failed for {RecordId}: {Message}",
                    id, completeReviewResult.ErrorMessage);
                return BadRequest(new ErrorResponse(
                    completeReviewResult.ErrorMessage ?? "Failed to complete upload review"));
            }

            if (completeReviewResult.RemovedImagePaths.Count > 0)
            {
                _logger.LogInformation("Step 2: Removing {Count} image paths from upload record {RecordId}",
                    completeReviewResult.RemovedImagePaths.Count, id);
                await _studentClient.RemoveImagesFromRecordAsync(studentId, id, completeReviewResult.RemovedImagePaths.ToList());
            }

            _logger.LogInformation("Step 3: Calling DeleteUploadRecordAfterReview for {RecordId}", id);
            try
            {
                await _studentClient.DeleteUploadRecordAfterReviewAsync(studentId, id);
            }
            catch (HttpRequestException ex)
            {
                // No longer relays the downstream message: fixed local result only.
                _logger.LogWarning(ex, "DeleteUploadRecordAfterReview failed for {RecordId}", id);
                return BadRequest(new ErrorResponse("Failed to delete upload record"));
            }

            _logger.LogInformation("Legacy data cleanup completed successfully for {RecordId}", id);

            return Ok(new
            {
                success = true,
                message = "清理完成",
                uploadRecordId = id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean legacy data for upload record: {RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to clean legacy data"));
        }
    }

    [HttpPost("{id}/analyze")]
    public async Task<IActionResult> AnalyzeUploadRecord(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new ErrorResponse("Invalid record ID format"));
            }

            // TODO: VL analysis has moved to Mistake service. Redirect this call or implement via Mistake gRPC.
            return StatusCode(501, new ErrorResponse("VL analysis has been moved to Mistake service. Use the Mistake polling mechanism instead."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze upload record: {RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to analyze upload record"));
        }
    }

    private string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private static long? ParseTimestampToUnixSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (long.TryParse(raw, out var asNumber)) return asNumber;
        if (DateTimeOffset.TryParse(raw, out var dto)) return dto.ToUnixTimeSeconds();
        return null;
    }
}

public class RotateImageRequest
{
    public string StudentId { get; set; } = string.Empty;
    public int ImageIndex { get; set; }
    public int Rotation { get; set; }
}

public class ResetStatusRequest
{
    public string StudentId { get; set; } = string.Empty;
    public int TargetStatus { get; set; }
}

public class ImageAssignment
{
    public Guid? RequestKey { get; set; }
    public List<string>? SourcePaths { get; set; }
    public List<int> ImageIndices { get; set; } = new();
    public int Subject { get; set; }
    public int Grade { get; set; }
    public string? Comments { get; set; }
}

public class AssignUploadRecordRequest
{
    public string? Mode { get; set; }
    public Guid? ExpectedContentRevision { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public List<ImageAssignment> Assignments { get; set; } = new();
}
