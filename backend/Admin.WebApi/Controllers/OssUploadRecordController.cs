using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruoyu.Admin.ServiceClients;
using Admin.WebApi.Models;
using Ruoyu.Admin.Common.Oss;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-upload-records")]
[ApiController]
[Authorize]
public class OssUploadRecordController : ControllerBase
{
    private readonly IStudentHttpClient _studentClient;
    private readonly IMistakeHttpClient _mistakeClient;
    private readonly ILogger<OssUploadRecordController> _logger;
    private readonly IOssService _ossService;

    public OssUploadRecordController(
        IStudentHttpClient studentClient,
        IMistakeHttpClient mistakeClient,
        ILogger<OssUploadRecordController> logger,
        IOssService ossService)
    {
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
        _logger = logger;
        _ossService = ossService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUploadRecords([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int status = -1, [FromQuery] string? studentId = null)
    {
        try
        {
            var response = await _studentClient.GetAllUploadRecordsAsync(status >= 0 ? status : (int?)null, page, pageSize, studentId);

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
            var assignedImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assignment in request.Assignments)
            {
                // 获取指定索引的图片路径
                var selectedImagePaths = assignment.ImageIndices
                    .Where(idx => idx >= 0 && idx < record.ImageEntries.Count)
                    .Select(idx => record.ImageEntries[idx].Path)
                    .ToList();

                if (selectedImagePaths.Count == 0)
                {
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

                if (!submitResponse.Success)
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
        try
        {
            if (!Guid.TryParse(id, out var recordId))
            {
                return BadRequest(new ErrorResponse("Invalid record ID"));
            }

            if (string.IsNullOrWhiteSpace(request.StudentId))
            {
                return BadRequest(new ErrorResponse("StudentId is required"));
            }

            await _studentClient.RotateUploadImageAsync(request.StudentId, id, request.ImageIndex, request.Rotation);

            return Ok(new { success = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to rotate image: RecordId={RecordId}", id);
            return BadRequest(new ErrorResponse(ex.Message ?? "Failed to rotate image"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate image: RecordId={RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to rotate image"));
        }
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
                _logger.LogWarning("DeleteUploadRecordAfterReview failed for {RecordId}: {Message}",
                    id, ex.Message);
                return BadRequest(new ErrorResponse(ex.Message ?? "Failed to delete upload record"));
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
    public List<int> ImageIndices { get; set; } = new();
    public int Subject { get; set; }
    public int Grade { get; set; }
    public string? Comments { get; set; }
}

public class AssignUploadRecordRequest
{
    public string StudentId { get; set; } = string.Empty;
    public List<ImageAssignment> Assignments { get; set; } = new();
}
