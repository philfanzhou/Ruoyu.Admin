using Microsoft.AspNetCore.Mvc;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using Admin.WebApi.Models;

namespace Admin.WebApi.Controllers;

[Route("api/admin/legacy-data")]
[ApiController]
public class LegacyDataController : ControllerBase
{
    private readonly MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient _mistakeClient;
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _managementClient;
    private readonly SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient _learningClient;
    private readonly ILogger<LegacyDataController> _logger;

    public LegacyDataController(
        MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient mistakeClient,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient managementClient,
        SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient learningClient,
        ILogger<LegacyDataController> logger)
    {
        _mistakeClient = mistakeClient;
        _managementClient = managementClient;
        _learningClient = learningClient;
        _logger = logger;
    }

    /// <summary>
    /// 扫描历史遗留数据 - 查找所有"错题已审核但上传记录还在"的情况
    /// </summary>
    [HttpGet("scan")]
    public async Task<IActionResult> ScanLegacyData([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var allResults = new List<LegacyDataItem>();
            var studentNameMap = new Dictionary<string, string>();

            int scanPage = 1;
            const int scanPageSize = 100;
            bool hasMore = true;

            while (hasMore)
            {
                var uploadRecordsRequest = new SProto.GetAllUploadRecordsRequest
                {
                    Page = scanPage,
                    PageSize = scanPageSize
                };
                var uploadRecordsResponse = await _managementClient.GetAllUploadRecordsAsync(uploadRecordsRequest);

                if (uploadRecordsResponse.Items.Count == 0)
                {
                    hasMore = false;
                    break;
                }

                foreach (var record in uploadRecordsResponse.Items)
                {
                    var mistakeListRequest = new MistakeProto.GetMistakeItemsByUploadRequest
                    {
                        SourceUploadId = record.Id
                    };
                    var mistakeList = await _mistakeClient.GetMistakeItemsByUploadAsync(mistakeListRequest);

                    if (mistakeList.Items.Count == 0)
                    {
                        continue;
                    }

                    var allReviewed = mistakeList.Items.All(i => i.ReviewStatus == MistakeProto.ReviewStatus.Confirmed ||
                                                                  i.ReviewStatus == MistakeProto.ReviewStatus.Rejected);
                    var hasPending = mistakeList.Items.Any(i => i.ReviewStatus == MistakeProto.ReviewStatus.PendingReview);

                    var hasUploadPathImage = mistakeList.Items.SelectMany(i => i.SourceRegions)
                        .Any(r => !string.IsNullOrWhiteSpace(r.SourceImagePath) &&
                                  r.SourceImagePath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase));

                    if (!studentNameMap.TryGetValue(record.StudentId, out var studentName))
                    {
                        try
                        {
                            var student = await _managementClient.GetStudentAsync(new SProto.GetStudentRequest { StudentId = record.StudentId });
                            studentName = student.Name;
                            studentNameMap[record.StudentId] = studentName;
                        }
                        catch
                        {
                            studentNameMap[record.StudentId] = record.StudentId;
                        }
                    }

                    if (allReviewed && !hasPending)
                    {
                        long createdAtTimestamp = 0;
                        if (!string.IsNullOrWhiteSpace(record.CreatedAt))
                        {
                            if (DateTimeOffset.TryParse(record.CreatedAt, out var dto))
                            {
                                createdAtTimestamp = dto.ToUnixTimeSeconds();
                            }
                            else if (long.TryParse(record.CreatedAt, out var ts))
                            {
                                createdAtTimestamp = ts;
                            }
                        }

                        allResults.Add(new LegacyDataItem
                        {
                            UploadRecordId = record.Id,
                            StudentId = record.StudentId,
                            StudentName = studentName,
                            MistakeCount = mistakeList.Items.Count,
                            HasUploadPathImage = hasUploadPathImage,
                            CreatedAt = createdAtTimestamp
                        });
                    }
                }

                if (scanPage * scanPageSize >= uploadRecordsResponse.TotalCount)
                {
                    hasMore = false;
                }
                else
                {
                    scanPage++;
                }
            }

            var totalCount = allResults.Count;
            var pagedResults = allResults
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new
            {
                items = pagedResults,
                totalCount,
                page = page,
                pageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan legacy data");
            return StatusCode(500, new ErrorResponse("Failed to scan legacy data"));
        }
    }

    /// <summary>
    /// 清理单个遗留数据 - 通过业务接口安全处理
    /// </summary>
    [HttpPost("clean")]
    public async Task<IActionResult> CleanLegacyData([FromBody] CleanLegacyDataRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UploadRecordId))
            {
                return BadRequest(new ErrorResponse("UploadRecordId is required"));
            }

            _logger.LogInformation("Starting legacy data cleanup for upload record: {UploadRecordId}", request.UploadRecordId);

            // 验证：先检查该记录下的错题状态
            var mistakeList = await _mistakeClient.GetMistakeItemsByUploadAsync(
                new MistakeProto.GetMistakeItemsByUploadRequest
                {
                    SourceUploadId = request.UploadRecordId
                });

            if (mistakeList.Items.Count == 0)
            {
                return BadRequest(new ErrorResponse("No mistakes found for this upload record"));
            }

            var hasPending = mistakeList.Items.Any(i => i.ReviewStatus == MistakeProto.ReviewStatus.PendingReview);
            if (hasPending)
            {
                return BadRequest(new ErrorResponse("Cannot clean: some mistakes are still pending review"));
            }

            // 获取学生 ID（从第一个错题获取）
            var studentId = mistakeList.Items.FirstOrDefault()?.StudentId ?? "";
            if (string.IsNullOrWhiteSpace(studentId))
            {
                return BadRequest(new ErrorResponse("Cannot find studentId for this upload record"));
            }

            // 步骤 1：调用 CompleteUploadReview - 迁移图片并更新路径
            _logger.LogInformation("Step 1: Calling CompleteUploadReview for {UploadRecordId}", request.UploadRecordId);
            var completeReviewRequest = new MistakeProto.CompleteUploadReviewRequest
            {
                SourceUploadId = request.UploadRecordId,
                ReviewerId = "system-legacy-cleanup"
            };

            var completeReviewResult = await _mistakeClient.CompleteUploadReviewAsync(completeReviewRequest);

            if (!completeReviewResult.Success)
            {
                _logger.LogWarning("CompleteUploadReview failed for {UploadRecordId}: {Message}",
                    request.UploadRecordId, completeReviewResult.ErrorMessage);
                return BadRequest(new ErrorResponse(
                    completeReviewResult.ErrorMessage ?? "Failed to complete upload review"));
            }

            _logger.LogInformation("CompleteUploadReview succeeded for {UploadRecordId}", request.UploadRecordId);

            // 步骤 2：调用 DeleteUploadRecordAfterReview - 删除上传记录
            _logger.LogInformation("Step 2: Calling DeleteUploadRecordAfterReview for {UploadRecordId}", request.UploadRecordId);
            var deleteRequest = new SProto.DeleteUploadRecordAfterReviewRequest
            {
                RecordId = request.UploadRecordId,
                StudentId = studentId
            };

            var deleteResult = await _learningClient.DeleteUploadRecordAfterReviewAsync(deleteRequest);

            if (!deleteResult.Success)
            {
                _logger.LogWarning("DeleteUploadRecordAfterReview failed for {UploadRecordId}: {Message}",
                    request.UploadRecordId, deleteResult.ErrorMessage);
                return BadRequest(new ErrorResponse(
                    deleteResult.ErrorMessage ?? "Failed to delete upload record"));
            }

            _logger.LogInformation("Legacy data cleanup completed successfully for {UploadRecordId}", request.UploadRecordId);

            return Ok(new
            {
                success = true,
                message = "Cleanup completed successfully",
                uploadRecordId = request.UploadRecordId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean legacy data for upload record: {UploadRecordId}", request.UploadRecordId);
            return StatusCode(500, new ErrorResponse("Failed to clean legacy data"));
        }
    }
}

public class LegacyDataItem
{
    public string UploadRecordId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public int MistakeCount { get; set; }
    public bool HasUploadPathImage { get; set; }
    public long CreatedAt { get; set; }
}

public class CleanLegacyDataRequest
{
    public string UploadRecordId { get; set; } = string.Empty;
}
