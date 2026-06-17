using Admin.WebApi.Controllers;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// ImageController UT：覆盖路径校验、mistakes/ 路径分流、Student 路径分流、gRPC 异常处理。
/// </summary>
public class ImageControllerTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _studentClient;
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<ILogger<ImageController>> _logger;
    private readonly ImageController _controller;

    public ImageControllerTests()
    {
        _studentClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _mistakeClient = new Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _logger = new Mock<ILogger<ImageController>>();

        _controller = new ImageController(
            _studentClient.Object,
            _mistakeClient.Object,
            _logger.Object
        );
    }

    private static AsyncUnaryCall<T> CreateAsyncCall<T>(T response) where T : class
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
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
    // 路径校验
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
        // path 参数缺失时 Request.Query["path"].ToString() 返回空字符串
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
    // mistakes/ 路径分流
    // ============================================================

    [Fact]
    public async Task GetImage_MistakesPath_CallsMistakeGrpcService()
    {
        SetupQuery("mistakes/2025/06/14/abc/image.jpg");

        var grpcResponse = new MistakeProto.PresignedUrlResponse { Url = "https://example.com/presigned" };
        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<MistakeProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(grpcResponse));

        var result = await _controller.GetImage(CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://example.com/presigned");

        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.Is<MistakeProto.GetPresignedUrlRequest>(r => r.ObjectPath == "mistakes/2025/06/14/abc/image.jpg" && r.ExpirySeconds == 3600),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // 不应调用 Student 服务
        _studentClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetImage_MistakesPathCaseInsensitive_CallsMistakeGrpcService()
    {
        // 路径以 MISTAKES/ 开头（大写），应仍走 Mistake 服务
        SetupQuery("MISTAKES/2025/06/14/abc/image.jpg");

        var grpcResponse = new MistakeProto.PresignedUrlResponse { Url = "https://example.com/presigned" };
        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<MistakeProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(grpcResponse));

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>();
        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<MistakeProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Student 路径分流
    // ============================================================

    [Fact]
    public async Task GetImage_UploadsPath_CallsStudentGrpcService()
    {
        SetupQuery("uploads/2025/06/14/abc/image.jpg", "thumbnail");

        var grpcResponse = new SProto.PresignedUrlResponse { Url = "https://example.com/student-presigned" };
        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(grpcResponse));

        var result = await _controller.GetImage(CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://example.com/student-presigned");

        _studentClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.Is<SProto.GetPresignedUrlRequest>(r =>
                    r.ObjectPath == "uploads/2025/06/14/abc/image.jpg" &&
                    r.ExpirySeconds == 3600 &&
                    r.Size == "thumbnail"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // 不应调用 Mistake 服务
        _mistakeClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<MistakeProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetImage_QuestionsPath_CallsStudentGrpcService()
    {
        // questions/ 路径不以 mistakes/ 开头，应走 Student 服务
        SetupQuery("questions/2025/01/01/xyz/q1.png");

        var grpcResponse = new SProto.PresignedUrlResponse { Url = "https://example.com/q-presigned" };
        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(grpcResponse));

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>();
        _studentClient.Verify(
            c => c.GetPresignedUrlAsync(
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // gRPC 异常处理
    // ============================================================

    [Fact]
    public async Task GetImage_MistakeNotFound_Returns404()
    {
        SetupQuery("mistakes/2025/06/14/abc/notfound.jpg");

        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<MistakeProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Not found")));

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetImage_MistakeOtherError_Returns502()
    {
        SetupQuery("mistakes/2025/06/14/abc/image.jpg");

        _mistakeClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<MistakeProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

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
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Not found")));

        var result = await _controller.GetImage(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetImage_StudentOtherError_Returns502()
    {
        SetupQuery("uploads/2025/06/14/abc/image.jpg");

        _studentClient
            .Setup(c => c.GetPresignedUrlAsync(
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.PermissionDenied, "Denied")));

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
                It.IsAny<SProto.GetPresignedUrlRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        await _controller.GetImage(CancellationToken.None);

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<RpcException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
