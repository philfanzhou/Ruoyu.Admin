using System.Net;
using Admin.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// ImageController unit tests: covers path validation, mistakes/ path routing, Student path routing, and error handling.
/// </summary>
public class ImageControllerTests
{
    private readonly Mock<IStudentHttpClient> _studentClient;
    private readonly Mock<IMistakeHttpClient> _mistakeClient;
    private readonly Mock<ILogger<ImageController>> _logger;
    private readonly ImageController _controller;

    public ImageControllerTests()
    {
        _studentClient = new Mock<IStudentHttpClient>();
        _mistakeClient = new Mock<IMistakeHttpClient>();
        _logger = new Mock<ILogger<ImageController>>();

        _controller = new ImageController(
            _studentClient.Object,
            _mistakeClient.Object,
            _logger.Object
        );
    }

    private void SetupQuery(string? path, string? size = null)
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["path"] = path ?? string.Empty,
        });
        if (size is not null)
        {
            query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["path"] = path ?? string.Empty,
                ["size"] = size,
            });
        }

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { Request = { Query = query } }
        };
    }

    // ============================================================
    // Path validation
    // ============================================================

    [Fact]
    public async Task GetImage_EmptyPath_ReturnsBadRequest()
    {
        SetupQuery("");

        var result = await _controller.GetImage(CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value).Should().Be("缺少图片路径");
    }

    [Fact]
    public async Task GetImage_NullPath_ReturnsBadRequest()
    {
        // When the path parameter is missing, Request.Query["path"].ToString() returns an empty string
        SetupQuery(null);

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetImage_WhitespacePath_ReturnsBadRequest()
    {
        SetupQuery("   ");

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("uploads/../secret")]
    [InlineData("uploads/foo/../../bar")]
    public async Task GetImage_PathContainsDoubleDot_ReturnsBadRequest(string path)
    {
        SetupQuery(path);

        var result = await _controller.GetImage(CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value).Should().Be("非法路径");
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/uploads/img.jpg")]
    public async Task GetImage_PathStartsWithSlash_ReturnsBadRequest(string path)
    {
        SetupQuery(path);

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("\\windows\\system32")]
    [InlineData("\\uploads\\img.jpg")]
    public async Task GetImage_PathStartsWithBackslash_ReturnsBadRequest(string path)
    {
        SetupQuery(path);

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ============================================================
    // mistakes/ path routing
    // ============================================================

    [Fact]
    public async Task GetImage_MistakesPath_CallsMistakeGrpcService()
    {
        SetupQuery("mistakes/2025/06/14/abc/image.jpg");

        var httpResult = new MistakePresignedUrlResult { Url = "https://example.com/presigned" };
        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);

        var result = await _controller.GetImage(CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://example.com/presigned");

        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.Is<string>(p => p == "mistakes/2025/06/14/abc/image.jpg"),
                It.Is<int>(s => s == 3600),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The Student service must not be called
        _studentClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetImage_MistakesPathCaseInsensitive_CallsMistakeGrpcService()
    {
        // A path starting with MISTAKES/ (uppercase) must still go to the Mistake service
        SetupQuery("MISTAKES/2025/06/14/abc/image.jpg");

        var httpResult = new MistakePresignedUrlResult { Url = "https://example.com/presigned" };
        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>();
        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetImage_MistakesPath_WithSize_PassesSizeToMistakeService()
    {
        // Regression: mistakes/ branch previously hard-coded size=null, so
        // thumbnail requests (size=small) were silently ignored and the
        // original image was returned. Verify size is now forwarded.
        SetupQuery("mistakes/2025/06/14/abc/image.jpg", "small");

        var httpResult = new MistakePresignedUrlResult { Url = "https://example.com/presigned" };
        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>();
        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.Is<string>(p => p == "mistakes/2025/06/14/abc/image.jpg"),
                It.Is<int>(s => s == 3600),
                It.Is<string?>(s => s == "small"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Student path routing
    // ============================================================

    [Fact]
    public async Task GetImage_UploadsPath_CallsStudentHttpService()
    {
        SetupQuery("uploads/2025/06/14/abc/image.jpg", "thumbnail");

        var httpResult = new PresignedUrlResult { Url = "https://example.com/student-presigned" };
        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);

        var result = await _controller.GetImage(CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://example.com/student-presigned");

        _studentClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.Is<string>(p => p == "uploads/2025/06/14/abc/image.jpg"),
                It.Is<int>(s => s == 3600),
                It.Is<string?>(s => s == "thumbnail"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The Mistake service must not be called
        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetImage_QuestionsPath_CallsStudentHttpService()
    {
        // A questions/ path does not start with mistakes/ and must go to the Student service
        SetupQuery("questions/2025/01/01/xyz/q1.png");

        var httpResult = new PresignedUrlResult { Url = "https://example.com/q-presigned" };
        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>();
        _studentClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Error handling
    // ============================================================

    [Fact]
    public async Task GetImage_MistakeNotFound_Returns404()
    {
        SetupQuery("mistakes/2025/06/14/abc/notfound.jpg");

        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetImage_MistakeOtherError_Returns502()
    {
        SetupQuery("mistakes/2025/06/14/abc/image.jpg");

        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable));

        var result = await _controller.GetImage(CancellationToken.None);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task GetImage_StudentNotFound_Returns404()
    {
        SetupQuery("uploads/2025/06/14/abc/notfound.jpg");

        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetImage_StudentOtherError_Returns502()
    {
        SetupQuery("uploads/2025/06/14/abc/image.jpg");

        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable));

        var result = await _controller.GetImage(CancellationToken.None);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task GetImage_StudentError_LogsError()
    {
        SetupQuery("uploads/2025/06/14/abc/image.jpg");

        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable));

        await _controller.GetImage(CancellationToken.None);

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<HttpRequestException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
