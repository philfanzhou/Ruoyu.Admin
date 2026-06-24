using Admin.WebApi.Controllers;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// MistakeController UT：覆盖 GetMistakeItems、UpdateMistakeItem。
/// </summary>
public class MistakeControllerTests
{
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _studentLearningClient;
    private readonly Mock<ILogger<MistakeController>> _logger;
    private readonly MistakeController _controller;

    public MistakeControllerTests()
    {
        _mistakeClient = new Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _studentLearningClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _logger = new Mock<ILogger<MistakeController>>();

        _controller = new MistakeController(
            _mistakeClient.Object,
            _studentLearningClient.Object,
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

    // ============================================================
    // GetMistakeItems
    // ============================================================

    [Fact]
    public async Task GetMistakeItems_ReturnsItems()
    {
        var item1 = new MistakeProto.MistakeItemDto
        {
            Id = "m1",
            StudentId = "s1",
            Subject = 1,
            Grade = 3,
            SourceUploadId = "u1",
            ReviewStatus = MistakeProto.ReviewStatus.PendingReview,
            CreatedAt = "1700000000",
            UpdatedAt = "1700000001"
        };
        var region = new MistakeProto.SourceRegion { SourceImagePath = "uploads/img1.jpg" };
        item1.SourceRegions.Add(region);

        var pageMeta = new MistakeProto.PageMeta { TotalCount = 1, Page = 1, Size = 20, TotalPages = 1 };
        var response = new MistakeProto.MistakeItemPageResult();
        response.Items.Add(item1);
        response.PageMeta = pageMeta;

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var studentDto = new SProto.StudentDto { Id = "s1", Name = "张三" };
        _studentLearningClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));

        var result = await _controller.GetMistakeItems();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        ((int)typed.GetType().GetProperty("total")!.GetValue(typed)!).Should().Be(1);
    }

    [Fact]
    public async Task GetMistakeItems_GrpcError_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.GetMistakeItems();

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetMistakeItems_StudentLookupFallsBackToStudentId()
    {
        var item = new MistakeProto.MistakeItemDto
        {
            Id = "m1",
            StudentId = "s1",
            Subject = 1,
            Grade = 3,
            ReviewStatus = MistakeProto.ReviewStatus.PendingReview,
            CreatedAt = "1700000000",
            UpdatedAt = "1700000001"
        };
        var region = new MistakeProto.SourceRegion { SourceImagePath = "uploads/img1.jpg" };
        item.SourceRegions.Add(region);

        var pageMeta = new MistakeProto.PageMeta { TotalCount = 1, Page = 1, Size = 20, TotalPages = 1 };
        var response = new MistakeProto.MistakeItemPageResult();
        response.Items.Add(item);
        response.PageMeta = pageMeta;

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        _studentLearningClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Not found")));

        var result = await _controller.GetMistakeItems();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        // 即使学生查询失败，也应返回结果（回退显示 studentId）
        var typed = okResult.Value!;
        var items = (System.Collections.IEnumerable)typed.GetType().GetProperty("items")!.GetValue(typed)!;
        items.Should().NotBeNull();
    }

    // ============================================================
    // UpdateMistakeItem
    // ============================================================

    [Fact]
    public async Task UpdateMistakeItem_Success_ReturnsUpdatedItem()
    {
        var updatedItem = new MistakeProto.MistakeItemDto
        {
            Id = "m1",
            StudentId = "s1",
            Subject = 2,
            Grade = 4,
            SourceUploadId = "u1",
            ReviewStatus = MistakeProto.ReviewStatus.PendingReview
        };

        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(updatedItem));

        var studentDto = new SProto.StudentDto { Id = "s1", Name = "张三" };
        _studentLearningClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));

        var request = new UpdateMistakeRequest
        {
            StudentId = "s1",
            Subject = 2,
            Grade = 4
        };

        var result = await _controller.UpdateMistakeItem("m1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("id")!.GetValue(typed).Should().Be("m1");
        typed.GetType().GetProperty("subject")!.GetValue(typed).Should().Be(2);
        typed.GetType().GetProperty("grade")!.GetValue(typed).Should().Be(4);
    }

    [Fact]
    public async Task UpdateMistakeItem_GrpcError_Returns500()
    {
        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var request = new UpdateMistakeRequest
        {
            StudentId = "s1",
            Subject = 2,
            Grade = 4
        };

        var result = await _controller.UpdateMistakeItem("m1", request);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }
}
