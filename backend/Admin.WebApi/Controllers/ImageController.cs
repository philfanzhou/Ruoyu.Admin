using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruoyu.Study.MistakeBff.HttpClients;

namespace Admin.WebApi.Controllers;

[Route("api/admin/image")]
[ApiController]
[Authorize]
public class ImageController : ControllerBase
{
    private readonly IStudentHttpClient _studentClient;
    private readonly IMistakeHttpClient _mistakeClient;
    private readonly ILogger<ImageController> _logger;

    public ImageController(
        IStudentHttpClient studentClient,
        IMistakeHttpClient mistakeClient,
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
                // Pass sizeStr so mistakes/ thumbnails (small/medium) work;
                // previously this was hard-coded to null, forcing the original
                // image to be downloaded for every thumbnail render.
                var result = await _mistakeClient.GetPresignedUrlAsync(objectPath, 3600, sizeStr, cancellationToken);
                presignedUrl = result.Url;
            }
            else
            {
                var httpResult = await _studentClient.GetPresignedUrlAsync(objectPath, 3600, sizeStr, cancellationToken);
                presignedUrl = httpResult.Url;
            }

            if (string.IsNullOrEmpty(presignedUrl))
            {
                return NotFound();
            }

            return Redirect(presignedUrl);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetPresignedUrl 失败, Path: {Path}", objectPath);
            return StatusCode(502, new { message = "图片服务暂不可用" });
        }
    }
}
