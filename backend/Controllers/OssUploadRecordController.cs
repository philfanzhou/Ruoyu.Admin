using Microsoft.AspNetCore.Mvc;
using Grpc.Net.Client;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using Admin.WebApi.Models;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-upload-records")]
[ApiController]
public class OssUploadRecordController : ControllerBase
{
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _grpcClient;
    private readonly ILogger<OssUploadRecordController> _logger;

    public OssUploadRecordController(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
        ILogger<OssUploadRecordController> logger)
    {
        _grpcClient = grpcClient;
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

            var response = await _grpcClient.GetAllUploadRecordsAsync(request);
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

    [HttpPost("assign-zombie")]
    public async Task<IActionResult> AssignZombieImageToRecord([FromBody] AssignZombieImageRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ObjectPath))
                return BadRequest(new ErrorResponse("objectPath is required"));

            if (string.IsNullOrWhiteSpace(request.StudentId))
                return BadRequest(new ErrorResponse("studentId is required"));

            if (string.IsNullOrWhiteSpace(request.UploadRecordId))
                return BadRequest(new ErrorResponse("uploadRecordId is required"));

            if (string.IsNullOrWhiteSpace(request.ImageHash))
                return BadRequest(new ErrorResponse("imageHash is required"));

            var grpcRequest = new SProto.AssignZombieImageToRecordRequest
            {
                ObjectPath = request.ObjectPath,
                StudentId = request.StudentId,
                UploadRecordId = request.UploadRecordId,
                ImageHash = request.ImageHash
            };

            var response = await _grpcClient.AssignZombieImageToRecordAsync(grpcRequest);

            if (!response.Success)
                return BadRequest(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Zombie image assigned successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign zombie image");
            return StatusCode(500, new ErrorResponse("Failed to assign zombie image"));
        }
    }
}

public record AssignZombieImageRequest(string ObjectPath, string StudentId, string UploadRecordId, string ImageHash);
