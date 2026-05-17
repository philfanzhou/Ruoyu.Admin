using Microsoft.AspNetCore.Mvc;
using Ruoyu.Study.Common.Oss;

namespace Admin.WebApi.Controllers;

[Route("api/admin/image")]
[ApiController]
public class ImageController : ControllerBase
{
    private readonly IOssService _ossService;
    private readonly ILogger<ImageController> _logger;

    public ImageController(IOssService ossService, ILogger<ImageController> logger)
    {
        _ossService = ossService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetImage(CancellationToken cancellationToken)
    {
        var objectPath = Request.Query["path"].ToString();

        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return BadRequest(new { message = "缺少图片路径" });
        }

        if (objectPath.Contains("..") || objectPath.StartsWith("/") || objectPath.StartsWith("\\"))
        {
            return BadRequest(new { message = "非法路径" });
        }

        Stream? stream = null;
        try
        {
            var exists = await _ossService.ObjectExistsAsync(objectPath);
            if (!exists)
            {
                return NotFound();
            }

            stream = await _ossService.DownloadAsync(objectPath);
            if (stream.Length == 0)
            {
                await stream.DisposeAsync();
                return NotFound();
            }

            var contentType = GetContentType(objectPath);
            Response.Headers.CacheControl = "public, max-age=3600";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            if (stream != null)
            {
                await stream.DisposeAsync();
            }
            _logger.LogWarning(ex, "图片加载失败, Path: {Path}", objectPath);
            return NotFound();
        }
    }

    private static string GetContentType(string objectPath)
    {
        var ext = Path.GetExtension(objectPath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
    }
}
