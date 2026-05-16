using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using Admin.WebApi.Models;
using GrpcStatusCode = Grpc.Core.StatusCode;
using SProto = Ruoyu.Study.Student.Contract.Protos;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-audit")]
[ApiController]
public class OssAuditController : ControllerBase
{
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _grpcClient;
    private readonly ILogger<OssAuditController> _logger;

    public OssAuditController(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
        ILogger<OssAuditController> logger)
    {
        _grpcClient = grpcClient;
        _logger = logger;
    }

    [HttpGet("audit-all")]
    public async Task<IActionResult> AuditAllBuckets()
    {
        try
        {
            var request = new SProto.Empty();
            var response = await _grpcClient.AuditAllBucketsAsync(request);
            var dto = ToDto(response);
            return Ok(dto);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to audit all buckets");
            return StatusCode(500, new ErrorResponse($"Failed to audit: {ex.Status.Detail}"));
        }
    }

    [HttpGet("audit-bucket/{bucket}")]
    public async Task<IActionResult> AuditBucket(int bucket)
    {
        try
        {
            if (bucket < 1 || bucket > 3)
                return BadRequest(new ErrorResponse("Invalid bucket value. Must be 1 (Mistakes), 2 (Uploads), or 3 (Questions)."));

            var request = new SProto.AuditBucketRequest
            {
                Bucket = (SProto.OssBucket)bucket
            };
            var response = await _grpcClient.AuditBucketAsync(request);
            var dto = ToDto(response.Result);
            return Ok(dto);
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to audit bucket");
            return StatusCode(500, new ErrorResponse($"Failed to audit bucket: {ex.Status.Detail}"));
        }
    }

    [HttpDelete("zombie-object")]
    public async Task<IActionResult> DeleteZombieObject([FromQuery] string objectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(objectPath))
                return BadRequest(new ErrorResponse("Object path is required."));

            var request = new SProto.DeleteZombieObjectRequest { ObjectPath = objectPath };
            var response = await _grpcClient.DeleteZombieObjectAsync(request);

            if (!response.Success)
                return BadRequest(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Zombie object deleted successfully."));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to delete zombie object");
            return StatusCode(500, new ErrorResponse($"Failed to delete object: {ex.Status.Detail}"));
        }
    }

    [HttpDelete("zombie-objects")]
    public async Task<IActionResult> DeleteZombieObjects([FromBody] DeleteZombieObjectsRequest request)
    {
        try
        {
            if (request.ObjectPaths == null || request.ObjectPaths.Count == 0)
                return BadRequest(new ErrorResponse("At least one object path is required."));

            var grpcRequest = new SProto.DeleteZombieObjectsRequest();
            grpcRequest.ObjectPaths.AddRange(request.ObjectPaths);

            var response = await _grpcClient.DeleteZombieObjectsAsync(grpcRequest);
            return Ok(new DeleteZombieObjectsResponse(response.DeletedCount));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to delete zombie objects");
            return StatusCode(500, new ErrorResponse($"Failed to delete objects: {ex.Status.Detail}"));
        }
    }

    private static OssAuditResultDto ToDto(SProto.OssAuditResultResponse model) => new(
        model.AuditTime,
        model.BucketResults.Select(ToDto).ToList(),
        model.TotalZombieObjects,
        model.TotalZombieSize);

    private static OssBucketAuditResultDto ToDto(SProto.OssBucketAuditResult model) => new(
        (int)model.Bucket,
        model.TotalObjects,
        model.TotalSize,
        model.ZombieObjects.Select(ToDto).ToList(),
        model.ZombieSize);

    private static OssObjectInfoDto ToDto(SProto.OssObjectInfo model) => new(
        model.ObjectPath,
        model.Size,
        model.LastModified > 0 ? model.LastModified : null,
        model.IsZombie);
}
