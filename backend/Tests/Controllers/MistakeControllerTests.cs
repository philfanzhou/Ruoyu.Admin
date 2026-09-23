using System.Net;
using Admin.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// MistakeController UT：覆盖 GetMistakeItems、UpdateMistakeItem。
/// </summary>
public class MistakeControllerTests
{
    private readonly Mock<IMistakeHttpClient> _mistakeClient;
    private readonly Mock<IStudentHttpClient> _studentClient;
    private readonly Mock<ILogger<MistakeController>> _logger;
    private readonly MistakeController _controller;

    public MistakeControllerTests()
    {
        _mistakeClient = new Mock<IMistakeHttpClient>();
        _studentClient = new Mock<IStudentHttpClient>();
        _logger = new Mock<ILogger<MistakeController>>();

        _controller = new MistakeController(
            _mistakeClient.Object,
            _studentClient.Object,
            _logger.Object
        );
    }

    // ============================================================
    // GetMistakeItems
    // ============================================================

    [Fact]
    public async Task GetMistakeItems_ReturnsItems()
    {
        var item1 = new MistakeItemDto
        {
            Id = "m1",
            StudentId = "s1",
            Subject = 1,
            Grade = 3,
            SourceUploadId = "u1",
            ReviewStatus = MistakeReviewStatus.PendingReview,
            CreatedAt = "1700000000",
            UpdatedAt = "1700000001"
        };
        item1.SourceRegions.Add(new MistakeSourceRegionDto { SourceImagePath = "uploads/img1.jpg" });

        var pageMeta = new MistakePageMetaDto { TotalCount = 1, Page = 1, Size = 20, TotalPages = 1 };
        var response = new MistakeItemPageResult();
        response.Items.Add(item1);
        response.PageMeta = pageMeta;

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<MistakeReviewStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var studentDto = new StudentDto { Id = "s1", Name = "张三" };
        _studentClient
            .Setup(c => c.GetStudentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(studentDto);

        var result = await _controller.GetMistakeItems();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        ((int)typed.GetType().GetProperty("total")!.GetValue(typed)!).Should().Be(1);
    }

    [Fact]
    public async Task GetMistakeItems_HttpError_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<MistakeReviewStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await _controller.GetMistakeItems();

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetMistakeItems_StudentLookupFallsBackToStudentId()
    {
        var item = new MistakeItemDto
        {
            Id = "m1",
            StudentId = "s1",
            Subject = 1,
            Grade = 3,
            ReviewStatus = MistakeReviewStatus.PendingReview,
            CreatedAt = "1700000000",
            UpdatedAt = "1700000001"
        };
        item.SourceRegions.Add(new MistakeSourceRegionDto { SourceImagePath = "uploads/img1.jpg" });

        var pageMeta = new MistakePageMetaDto { TotalCount = 1, Page = 1, Size = 20, TotalPages = 1 };
        var response = new MistakeItemPageResult();
        response.Items.Add(item);
        response.PageMeta = pageMeta;

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<MistakeReviewStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _studentClient
            .Setup(c => c.GetStudentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

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
        var updatedItem = new UpdateMistakeItemResult
        {
            Id = "m1",
            StudentId = "s1",
            Subject = 2,
            Grade = 4,
            SourceUploadId = "u1",
            ReviewStatus = MistakeReviewStatus.PendingReview
        };

        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<MistakeSourceRegionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedItem);

        var studentDto = new StudentDto { Id = "s1", Name = "张三" };
        _studentClient
            .Setup(c => c.GetStudentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(studentDto);

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
    public async Task UpdateMistakeItem_HttpError_Returns500()
    {
        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<MistakeSourceRegionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

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
