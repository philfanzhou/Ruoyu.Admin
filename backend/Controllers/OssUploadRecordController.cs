using Microsoft.AspNetCore.Mvc;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using Admin.WebApi.Models;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-upload-records")]
[ApiController]
public class OssUploadRecordController : ControllerBase
{
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _managementClient;
    private readonly SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient _learningClient;
    private readonly ILogger<OssUploadRecordController> _logger;

    public OssUploadRecordController(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient managementClient,
        SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient learningClient,
        ILogger<OssUploadRecordController> logger)
    {
        _managementClient = managementClient;
        _learningClient = learningClient;
        _logger = logger;
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
            return Ok(new
            {
                items = response.Items.Select(r => new
                {
                    id = r.Id,
                    studentId = r.StudentId,
                    status = (int)r.Status,
                    imagePaths = r.ImagePaths,
                    comments = r.Comments,
                    createdAt = r.CreatedAt,
                    updatedAt = r.UpdatedAt,
                    classification = r.Classification
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

            return Ok(new { success = true, message = "Assignment successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign upload record: {RecordId}", id);
            return StatusCode(500, new ErrorResponse("Failed to assign upload record"));
        }
    }
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
