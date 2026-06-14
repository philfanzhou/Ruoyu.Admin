using Microsoft.AspNetCore.Mvc;
using Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;

namespace Admin.WebApi.Controllers;

[Route("api/admin/image")]
[ApiController]
public class ImageController : ControllerBase
{
    private readonly StudentLearningGrpcService.StudentLearningGrpcServiceClient _studentClient;
    private readonly MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient _mistakeClient;
    private readonly ILogger<ImageController> _logger;

    public ImageController(
        StudentLearningGrpcService.StudentLearningGrpcServiceClient studentClient,
        MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient mistakeClient,
        ILogger<ImageController> logger)
    {
        _studentClient = studentClient;
        _mistakeClient = mistakeClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetImage(CancellationToken cancellationToken)
    {
        var objectPath = Request.Query["path"].ToString();
        var sizeStr = Request.Query["size"].ToString();

        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return BadRequest(new { message = "缺少图片路径" });
        }

        if (objectPath.Contains("..") || objectPath.StartsWith("/") || objectPath.StartsWith("\\"))
        {
            return BadRequest(new { message = "非法路径" });
        }

        try
        {
            string presignedUrl;

            if (objectPath.StartsWith("mistakes/", StringComparison.OrdinalIgnoreCase))
            {
                var grpcResponse = await _mistakeClient.GetPresignedUrlAsync(
                    new MistakeProto.GetPresignedUrlRequest
                    { ObjectPath = objectPath, ExpirySeconds = 3600 }, cancellationToken: cancellationToken);
                presignedUrl = grpcResponse.Url;
            }
            else
            {
                var grpcResponse = await _studentClient.GetPresignedUrlAsync(
                    new GetPresignedUrlRequest
                    { ObjectPath = objectPath, ExpirySeconds = 3600, Size = sizeStr }, cancellationToken: cancellationToken);
                presignedUrl = grpcResponse.Url;
            }

            return Redirect(presignedUrl);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound();
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError(ex, "GetPresignedUrl 失败, Path: {Path}, StatusCode: {StatusCode}", objectPath, ex.StatusCode);
            return StatusCode(502, new { message = "图片服务暂不可用" });
        }
    }
}
