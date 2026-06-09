using Microsoft.AspNetCore.Mvc;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using Admin.WebApi.Models;
using Ruoyu.Study.Common.Oss;
using SProto = Ruoyu.Study.Student.Contract.Protos;

namespace Admin.WebApi.Controllers;

[Route("api/admin/mistakes")]
[ApiController]
public class MistakeController : ControllerBase
{
    private readonly MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient _mistakeClient;
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _managementClient;
    private readonly IOssService _ossService;
    private readonly ILogger<MistakeController> _logger;

    public MistakeController(
        MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient mistakeClient,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient managementClient,
        IOssService ossService,
        ILogger<MistakeController> logger)
    {
        _mistakeClient = mistakeClient;
        _managementClient = managementClient;
        _ossService = ossService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMistakeItems(
        [FromQuery] string? studentId = null,
        [FromQuery] int? subject = null,
        [FromQuery] int? grade = null,
        [FromQuery] int? reviewStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        try
        {
            if (page <= 0) page = 1;
            if (size <= 0) size = 10;
            if (size > 100) size = 100;

            var request = new MistakeProto.GetMistakeItemListRequest
            {
                StudentId = studentId ?? string.Empty,
                Subject = subject ?? 0,
                Grade = grade ?? 0,
                Page = page,
                Size = size
            };

            if (reviewStatus.HasValue)
            {
                request.ReviewStatus = (MistakeProto.ReviewStatus)reviewStatus.Value;
            }

            var response = await _mistakeClient.GetMistakeItemListAsync(request);

            var studentIds = response.Items.Select(i => i.StudentId).Distinct().ToList();
            var studentNameMap = new Dictionary<string, string>();

            foreach (var sid in studentIds)
            {
                if (string.IsNullOrWhiteSpace(sid)) continue;
                try
                {
                    var student = await _managementClient.GetStudentAsync(new SProto.GetStudentRequest { StudentId = sid });
                    studentNameMap[sid] = student.Name;
                }
                catch
                {
                    studentNameMap[sid] = sid;
                }
            }

            var items = response.Items.Select(item => new
            {
                id = item.Id,
                studentId = item.StudentId,
                studentName = studentNameMap.GetValueOrDefault(item.StudentId, item.StudentId),
                subject = item.Subject,
                grade = item.Grade,
                sourceUploadId = item.SourceUploadId,
                reviewStatus = (int)item.ReviewStatus,
                reviewerId = item.ReviewerId,
                reviewedAt = item.ReviewedAt,
                reviewComment = item.ReviewComment,
                type = item.Type,
                questionId = item.QuestionId,
                createdAt = item.CreatedAt,
                updatedAt = item.UpdatedAt,
                imageCount = item.SourceRegions.Count,
                firstImagePath = item.SourceRegions.FirstOrDefault()?.SourceImagePath ?? string.Empty
            }).ToList();

            return Ok(new
            {
                items,
                total = response.PageMeta?.TotalCount ?? 0,
                page = response.PageMeta?.Page ?? page,
                pageSize = response.PageMeta?.Size ?? size,
                totalPages = response.PageMeta?.TotalPages ?? 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mistake items");
            return StatusCode(500, new ErrorResponse("Failed to get mistake items"));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMistakeItem(string id)
    {
        try
        {
            var request = new MistakeProto.IdRequest { Id = id };
            var item = await _mistakeClient.GetMistakeItemAsync(request);

            string studentName = item.StudentId;
            try
            {
                var student = await _managementClient.GetStudentAsync(new SProto.GetStudentRequest { StudentId = item.StudentId });
                studentName = student.Name;
            }
            catch { }

            return Ok(new
            {
                id = item.Id,
                studentId = item.StudentId,
                studentName,
                subject = item.Subject,
                grade = item.Grade,
                sourceUploadId = item.SourceUploadId,
                reviewStatus = (int)item.ReviewStatus,
                reviewerId = item.ReviewerId,
                reviewedAt = item.ReviewedAt,
                reviewComment = item.ReviewComment,
                type = item.Type,
                questionId = item.QuestionId,
                createdAt = item.CreatedAt,
                updatedAt = item.UpdatedAt,
                imageCount = item.SourceRegions.Count,
                firstImagePath = item.SourceRegions.FirstOrDefault()?.SourceImagePath ?? string.Empty,
                sourceRegions = item.SourceRegions.Select(r => new
                {
                    sourceImagePath = r.SourceImagePath,
                    boundingBox = r.BoundingBox != null ? new
                    {
                        x1 = r.BoundingBox.X1,
                        y1 = r.BoundingBox.Y1,
                        x2 = r.BoundingBox.X2,
                        y2 = r.BoundingBox.Y2
                    } : null
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mistake item: {Id}", id);
            return StatusCode(500, new ErrorResponse("Failed to get mistake item"));
        }
    }

    [HttpGet("by-upload/{uploadId}")]
    public async Task<IActionResult> GetMistakesByUploadId(string uploadId)
    {
        try
        {
            var request = new MistakeProto.GetMistakeItemsByUploadRequest
            {
                SourceUploadId = uploadId
            };
            var response = await _mistakeClient.GetMistakeItemsByUploadAsync(request);

            var studentIds = response.Items.Select(i => i.StudentId).Distinct().ToList();
            var studentNameMap = new Dictionary<string, string>();

            foreach (var sid in studentIds)
            {
                if (string.IsNullOrWhiteSpace(sid)) continue;
                try
                {
                    var student = await _managementClient.GetStudentAsync(new SProto.GetStudentRequest { StudentId = sid });
                    studentNameMap[sid] = student.Name;
                }
                catch
                {
                    studentNameMap[sid] = sid;
                }
            }

            var items = response.Items.Select(item => new
            {
                id = item.Id,
                studentId = item.StudentId,
                studentName = studentNameMap.GetValueOrDefault(item.StudentId, item.StudentId),
                subject = item.Subject,
                grade = item.Grade,
                sourceUploadId = item.SourceUploadId,
                reviewStatus = (int)item.ReviewStatus,
                reviewerId = item.ReviewerId,
                reviewedAt = item.ReviewedAt,
                reviewComment = item.ReviewComment,
                type = item.Type,
                questionId = item.QuestionId,
                createdAt = item.CreatedAt,
                updatedAt = item.UpdatedAt,
                imageCount = item.SourceRegions.Count,
                firstImagePath = item.SourceRegions.FirstOrDefault()?.SourceImagePath ?? string.Empty
            }).ToList();

            return Ok(new
            {
                items,
                total = items.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mistakes by upload id: {UploadId}", uploadId);
            return StatusCode(500, new ErrorResponse("Failed to get mistakes by upload id"));
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateMistakeItem(string id, [FromBody] UpdateMistakeRequest request)
    {
        try
        {
            var updateRequest = new MistakeProto.UpdateMistakeItemRequest
            {
                Id = id,
                StudentId = request.StudentId ?? "",
                Subject = request.Subject ?? 0,
                Grade = request.Grade ?? 0
            };

            var item = await _mistakeClient.UpdateMistakeItemAsync(updateRequest);

            string studentName = item.StudentId;
            try
            {
                var student = await _managementClient.GetStudentAsync(new SProto.GetStudentRequest { StudentId = item.StudentId });
                studentName = student.Name;
            }
            catch { }

            return Ok(new
            {
                id = item.Id,
                studentId = item.StudentId,
                studentName,
                subject = item.Subject,
                grade = item.Grade,
                sourceUploadId = item.SourceUploadId,
                reviewStatus = (int)item.ReviewStatus
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update mistake item: {Id}", id);
            return StatusCode(500, new ErrorResponse("Failed to update mistake item"));
        }
    }

    [HttpPost("migrate-images/{id}")]
    public async Task<IActionResult> MigrateImages(string id)
    {
        try
        {
            var item = await _mistakeClient.GetMistakeItemAsync(new MistakeProto.IdRequest { Id = id });

            var regionsToMigrate = item.SourceRegions
                .Where(r => !string.IsNullOrWhiteSpace(r.SourceImagePath)
                            && r.SourceImagePath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (regionsToMigrate.Count == 0)
            {
                return Ok(new { success = true, message = "所有图片路径已在 mistakes 下，无需迁移", migratedCount = 0 });
            }

            var pathMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var migratedCount = 0;

            foreach (var region in regionsToMigrate)
            {
                var oldPath = region.SourceImagePath;

                if (pathMapping.TryGetValue(oldPath, out var cachedNewPath))
                {
                    region.SourceImagePath = cachedNewPath;
                    continue;
                }

                var newPath = "mistakes" + oldPath.Substring("uploads".Length);

                try
                {
                    await _ossService.CopyObjectAsync(oldPath, newPath);
                    await _ossService.DeleteAsync(oldPath);
                    pathMapping[oldPath] = newPath;
                    region.SourceImagePath = newPath;
                    migratedCount++;
                    _logger.LogInformation("Migrated image from {OldPath} to {NewPath}", oldPath, newPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to migrate image {Path}", oldPath);
                    return StatusCode(500, new ErrorResponse($"迁移图片失败: {oldPath} - {ex.Message}"));
                }
            }

            // Build updated sourceRegions list (merge migrated + unchanged)
            var updatedRegions = item.SourceRegions.Select(r =>
            {
                var region = new MistakeProto.SourceRegion
                {
                    SourceImagePath = r.SourceImagePath
                };
                if (r.BoundingBox != null)
                {
                    region.BoundingBox = new MistakeProto.BoundingBox
                    {
                        X1 = r.BoundingBox.X1,
                        Y1 = r.BoundingBox.Y1,
                        X2 = r.BoundingBox.X2,
                        Y2 = r.BoundingBox.Y2
                    };
                }
                return region;
            }).ToList();

            var updateRequest = new MistakeProto.UpdateMistakeItemRequest
            {
                Id = id
            };
            updateRequest.SourceRegions.AddRange(updatedRegions);

            await _mistakeClient.UpdateMistakeItemAsync(updateRequest);

            return Ok(new { success = true, message = $"已迁移 {migratedCount} 张图片", migratedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate images for mistake item: {Id}", id);
            return StatusCode(500, new ErrorResponse("Failed to migrate images"));
        }
    }
}

public class UpdateMistakeRequest
{
    public string? StudentId { get; set; }
    public int? Subject { get; set; }
    public int? Grade { get; set; }
}
