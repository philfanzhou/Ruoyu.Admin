using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using Admin.WebApi.Models;
using GrpcStatusCode = Grpc.Core.StatusCode;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;

namespace Admin.WebApi.Controllers;

[Route("api/admin/oss-audit")]
[ApiController]
public class OssAuditController : ControllerBase
{
    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _studentClient;
    private readonly MProto.MistakeGrpcService.MistakeGrpcServiceClient _mistakeClient;
    private readonly ILogger<OssAuditController> _logger;

    public OssAuditController(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient studentClient,
        MProto.MistakeGrpcService.MistakeGrpcServiceClient mistakeClient,
        ILogger<OssAuditController> logger)
    {
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
        _logger = logger;
    }

    [HttpGet("audit-all")]
    public async Task<IActionResult> AuditAllBuckets()
    {
        var buckets = new[] { SProto.OssBucket.Mistakes, SProto.OssBucket.Uploads, SProto.OssBucket.Questions };
        var bucketResults = new List<OssBucketAuditResultDto>();
        var warnings = new List<string>();

        HashSet<string> registeredPaths;
        try
        {
            var pathsResponse = await _studentClient.GetRegisteredOssPathsAsync(new SProto.Empty());
            registeredPaths = new HashSet<string>(pathsResponse.Paths, StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("从 Student 服务获取到 {Count} 个已注册路径", registeredPaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法获取 Student 服务的已注册路径");
            return StatusCode(502, new ErrorResponse("Student 服务不可用，无法执行审核。请检查 Student 服务状态后重试。"));
        }

        HashSet<string> mistakePaths = new(StringComparer.OrdinalIgnoreCase);
        bool mistakeServiceAvailable;
        try
        {
            var mistakeResponse = await _mistakeClient.GetAllReferencedImagePathsAsync(
                new MProto.Empty(),
                deadline: DateTime.UtcNow.AddSeconds(30));
            foreach (var path in mistakeResponse.ImagePaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    mistakePaths.Add(path);
            }
            mistakeServiceAvailable = true;
            _logger.LogInformation("从 Mistake 服务获取到 {Count} 个错题引用路径", mistakePaths.Count);
        }
        catch (Exception ex)
        {
            mistakeServiceAvailable = false;
            var msg = $"Mistake 服务不可用：{ex.Message}。审核结果可能包含被错题引用的图片（误报）。";
            warnings.Add(msg);
            _logger.LogWarning(ex, "无法获取 Mistake 服务的错题引用路径");
        }

        foreach (var bucket in buckets)
        {
            try
            {
                var listResponse = await _studentClient.ListOssObjectsAsync(
                    new SProto.ListOssObjectsRequest { Bucket = bucket });

                var zombieObjects = new List<OssObjectInfoDto>();
                long zombieSize = 0;

                foreach (var obj in listResponse.Objects)
                {
                    var inRegistered = registeredPaths.Contains(obj.ObjectPath);
                    var inMistake = mistakePaths.Contains(obj.ObjectPath);
                    var isZombie = !inRegistered && !inMistake;

                    if (isZombie)
                    {
                        zombieObjects.Add(new OssObjectInfoDto(
                            obj.ObjectPath,
                            obj.Size,
                            obj.LastModified > 0 ? obj.LastModified : null,
                            true));
                        zombieSize += obj.Size;
                    }
                }

                bucketResults.Add(new OssBucketAuditResultDto(
                    (int)bucket,
                    listResponse.Objects.Count,
                    listResponse.Objects.Sum(o => o.Size),
                    zombieObjects,
                    zombieSize));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "无法列出存储桶 {Bucket} 的对象", bucket);
                warnings.Add($"无法获取存储桶 {bucket} 的对象列表：{ex.Message}");
            }
        }

        var result = new OssAuditResultDto(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            bucketResults,
            bucketResults.Sum(r => r.ZombieObjects.Count),
            bucketResults.Sum(r => r.ZombieSize),
            mistakeServiceAvailable,
            registeredPaths.Count,
            mistakePaths.Count,
            warnings);

        return Ok(result);
    }

    [HttpDelete("zombie-object")]
    public async Task<IActionResult> DeleteZombieObject([FromQuery] string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
            return BadRequest(new ErrorResponse("Object path is required."));

        try
        {
            var pathsResponse = await _studentClient.GetRegisteredOssPathsAsync(new SProto.Empty());
            var registeredPaths = new HashSet<string>(pathsResponse.Paths, StringComparer.OrdinalIgnoreCase);
            if (registeredPaths.Contains(objectPath))
                return BadRequest(new ErrorResponse("该文件仍被上传记录引用，不能删除。"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除前无法验证 Student 服务引用");
            return StatusCode(502, new ErrorResponse("Student 服务不可用，无法安全删除。请稍后重试。"));
        }

        try
        {
            var mistakeResponse = await _mistakeClient.GetAllReferencedImagePathsAsync(
                new MProto.Empty(),
                deadline: DateTime.UtcNow.AddSeconds(30));
            var mistakePaths = new HashSet<string>(mistakeResponse.ImagePaths, StringComparer.OrdinalIgnoreCase);
            if (mistakePaths.Contains(objectPath))
                return BadRequest(new ErrorResponse("该文件仍被错题记录引用，不能删除。"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除前无法验证 Mistake 服务引用");
            return StatusCode(502, new ErrorResponse("Mistake 服务不可用，无法安全删除。请稍后重试。"));
        }

        try
        {
            var response = await _studentClient.DeleteOssObjectAsync(
                new SProto.DeleteOssObjectRequest { ObjectPath = objectPath });

            if (!response.Success)
                return BadRequest(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Zombie object deleted successfully."));
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
        if (request.ObjectPaths == null || request.ObjectPaths.Count == 0)
            return BadRequest(new ErrorResponse("At least one object path is required."));

        HashSet<string> registeredPaths;
        try
        {
            var pathsResponse = await _studentClient.GetRegisteredOssPathsAsync(new SProto.Empty());
            registeredPaths = new HashSet<string>(pathsResponse.Paths, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量删除前无法验证 Student 服务引用");
            return StatusCode(502, new ErrorResponse("Student 服务不可用，无法安全删除。请稍后重试。"));
        }

        HashSet<string> mistakePaths;
        try
        {
            var mistakeResponse = await _mistakeClient.GetAllReferencedImagePathsAsync(
                new MProto.Empty(),
                deadline: DateTime.UtcNow.AddSeconds(30));
            mistakePaths = new HashSet<string>(mistakeResponse.ImagePaths, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量删除前无法验证 Mistake 服务引用");
            return StatusCode(502, new ErrorResponse("Mistake 服务不可用，无法安全删除。请稍后重试。"));
        }

        int deletedCount = 0;
        var errors = new List<string>();

        foreach (var path in request.ObjectPaths)
        {
            if (registeredPaths.Contains(path))
            {
                errors.Add($"{path}: 被上传记录引用，跳过");
                continue;
            }
            if (mistakePaths.Contains(path))
            {
                errors.Add($"{path}: 被错题记录引用，跳过");
                continue;
            }

            try
            {
                var response = await _studentClient.DeleteOssObjectAsync(
                    new SProto.DeleteOssObjectRequest { ObjectPath = path });
                if (response.Success) deletedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: 删除失败 - {ex.Message}");
            }
        }

        return Ok(new { deletedCount, errors, totalRequested = request.ObjectPaths.Count });
    }
}
