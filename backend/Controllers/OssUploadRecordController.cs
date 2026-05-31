using Microsoft.AspNetCore.Mvc;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using Admin.WebApi.Models;
using Ruoyu.Study.Common.Oss;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-upload-records")]
[ApiController]
public class OssUploadRecordController : ControllerBase
{
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _managementClient;
    private readonly SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient _learningClient;
    private readonly MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient _mistakeClient;
    private readonly ILogger<OssUploadRecordController> _logger;
    private readonly IOssService _ossService;

    public OssUploadRecordController(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient managementClient,
        SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient learningClient,
        MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient mistakeClient,
        ILogger<OssUploadRecordController> logger,
        IOssService ossService)
    {
        _managementClient = managementClient;
        _learningClient = learningClient;
        _mistakeClient = mistakeClient;
        _logger = logger;
        _ossService = ossService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUploadRecords([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int status = -1, [FromQuery] string? studentId = null)
    {
        try
        {
            var request = new SProto.GetAllUploadRecordsRequest
            {
                Page = page,
                PageSize = pageSize,
                Status = status >= 0 ? (SProto.UploadStatus)status : SProto.UploadStatus.Unspecified
            };

            if (!string.IsNullOrWhiteSpace(studentId))
            {
                request.StudentId = studentId;
            }

            var response = await _managementClient.GetAllUploadRecordsAsync(request);

            // 收集所有唯一的 studentId，批量查询学生姓名
            var studentIds = response.Items.Select(r => r.StudentId).Distinct().ToList();
            var studentNameMap = new Dictionary<string, string>();

            foreach (var sid in studentIds)
            {
                try
                {
                    var student = await _managementClient.GetStudentAsync(new SProto.GetStudentRequest { StudentId = sid });
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
                    status = (int)r.Status,
                    imagePaths = r.ImagePaths,
                    comments = r.Comments,
                    createdAt = r.CreatedAt,
                    updatedAt = r.UpdatedAt,
                    classification = r.Classification,
                    subject = r.Subject,
                    grade = r.Grade
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

    [HttpGet("classification-options")]
    public async Task<IActionResult> GetClassificationOptions()
    {
        try
        {
            var request = new SProto.Empty();
            var response = await _learningClient.GetAvailableClassificationsAsync(request);

            var options = response.Classifications.Select(c => new ClassificationOption(c.Value, c.Name, c.DisplayName)).ToList();
            return Ok(options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get classification options");
            return StatusCode(500, new ErrorResponse("Failed to get classification options"));
        }
    }

    [HttpPost("{id}/reset-status")]
    public async Task<IActionResult> ResetUploadRecordStatus(string id, [FromBody] ResetStatusRequest request)
    {
        try
        {
            var resetRequest = new SProto.ResetUploadRecordStatusRequest
            {
                RecordId = id,
                StudentId = request.StudentId,
                TargetStatus = (SProto.UploadStatus)request.TargetStatus
            };

            var response = await _learningClient.ResetUploadRecordStatusAsync(resetRequest);
            
            if (!response.Success)
            {
                return BadRequest(new ErrorResponse(response.ErrorMessage ?? "Failed to reset status"));
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
            var assignRequest = new SProto.UpdateUploadRecordClassificationRequest
            {
                RecordId = id,
                StudentId = request.StudentId,
                Classification = request.Classification,
                Subject = request.Subject,
                Grade = request.Grade
            };

            var response = await _learningClient.UpdateUploadRecordClassificationAsync(assignRequest);

            if (!response.Success)
            {
                return BadRequest(new ErrorResponse(response.ErrorMessage ?? "Failed to assign record"));
            }

            if (!response.MistakeDispatched && !string.IsNullOrEmpty(response.DispatchErrorMessage))
            {
                return Ok(new
                {
                    success = true,
                    message = "Assignment saved, but failed to create mistake record",
                    warning = response.DispatchErrorMessage
                });
            }

            return Ok(new { success = true, message = "Assignment successful" });
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

            var grpcRequest = new SProto.RotateUploadImageRequest
            {
                RecordId = id,
                StudentId = request.StudentId,
                ImageIndex = request.ImageIndex,
                Rotation = request.Rotation
            };

            var result = await _learningClient.RotateUploadImageAsync(grpcRequest);

            if (!result.Success)
            {
                return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Failed to rotate image"));
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate image: RecordId={RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to rotate image"));
        }
    }

    [HttpGet("legacy-check/{id}")]
    public async Task<IActionResult> LegacyCheck(string id)
    {
        try
        {
            var mistakeList = await _mistakeClient.GetMistakeItemsByUploadAsync(
                new MistakeProto.GetMistakeItemsByUploadRequest
                {
                    SourceUploadId = id
                });

            if (mistakeList.Items.Count == 0)
            {
                return Ok(new { isLegacy = false, message = "该记录没有关联错题" });
            }

            var allReviewed = mistakeList.Items.All(i => i.ReviewStatus == MistakeProto.ReviewStatus.Confirmed ||
                                                          i.ReviewStatus == MistakeProto.ReviewStatus.Rejected);
            var pendingCount = mistakeList.Items.Count(i => i.ReviewStatus == MistakeProto.ReviewStatus.PendingReview);

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

    [HttpPost("legacy-clean/{id}")]
    public async Task<IActionResult> LegacyClean(string id)
    {
        try
        {
            _logger.LogInformation("Starting legacy data cleanup for upload record: {RecordId}", id);

            var mistakeList = await _mistakeClient.GetMistakeItemsByUploadAsync(
                new MistakeProto.GetMistakeItemsByUploadRequest
                {
                    SourceUploadId = id
                });

            if (mistakeList.Items.Count == 0)
            {
                return BadRequest(new ErrorResponse("该记录没有关联错题，无法清理"));
            }

            var allReviewed = mistakeList.Items.All(i => i.ReviewStatus == MistakeProto.ReviewStatus.Confirmed ||
                                                          i.ReviewStatus == MistakeProto.ReviewStatus.Rejected);
            var hasPending = mistakeList.Items.Any(i => i.ReviewStatus == MistakeProto.ReviewStatus.PendingReview);

            if (!allReviewed || hasPending)
            {
                var pendingCount = mistakeList.Items.Count(i => i.ReviewStatus == MistakeProto.ReviewStatus.PendingReview);
                return BadRequest(new ErrorResponse($"该记录还有{pendingCount}条错题待审核，无法清理"));
            }

            var studentId = mistakeList.Items.FirstOrDefault()?.StudentId ?? "";
            if (string.IsNullOrWhiteSpace(studentId))
            {
                return BadRequest(new ErrorResponse("无法找到该上传记录的学生ID"));
            }

            _logger.LogInformation("Step 1: Calling CompleteUploadReview for {RecordId}", id);
            var completeReviewResult = await _mistakeClient.CompleteUploadReviewAsync(
                new MistakeProto.CompleteUploadReviewRequest
                {
                    SourceUploadId = id,
                    ReviewerId = "admin-legacy-cleanup"
                });

            if (!completeReviewResult.Success)
            {
                _logger.LogWarning("CompleteUploadReview failed for {RecordId}: {Message}",
                    id, completeReviewResult.ErrorMessage);
                return BadRequest(new ErrorResponse(
                    completeReviewResult.ErrorMessage ?? "Failed to complete upload review"));
            }

            _logger.LogInformation("Step 2: Calling DeleteUploadRecordAfterReview for {RecordId}", id);
            var deleteResult = await _learningClient.DeleteUploadRecordAfterReviewAsync(
                new SProto.DeleteUploadRecordAfterReviewRequest
                {
                    RecordId = id,
                    StudentId = studentId
                });

            if (!deleteResult.Success)
            {
                _logger.LogWarning("DeleteUploadRecordAfterReview failed for {RecordId}: {Message}",
                    id, deleteResult.ErrorMessage);
                return BadRequest(new ErrorResponse(
                    deleteResult.ErrorMessage ?? "Failed to delete upload record"));
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

public class AssignUploadRecordRequest
{
    public string StudentId { get; set; } = string.Empty;
    public int Classification { get; set; }
    public int Subject { get; set; }
    public int Grade { get; set; }
}
