using System.Net;
using Admin.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.Common.Oss;
using Ruoyu.Study.MistakeBff.GrpcClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public class OssUploadRecordControllerLegacyTests
{
    private readonly Mock<IStudentHttpClient> _studentClient;
    private readonly Mock<IMistakeHttpClient> _mistakeClient;
    private readonly Mock<ILogger<OssUploadRecordController>> _logger;
    private readonly Mock<IOssService> _ossService;
    private readonly OssUploadRecordController _controller;

    public OssUploadRecordControllerLegacyTests()
    {
        _studentClient = new Mock<IStudentHttpClient>();
        _mistakeClient = new Mock<IMistakeHttpClient>();
        _logger = new Mock<ILogger<OssUploadRecordController>>();
        _ossService = new Mock<IOssService>();

        _controller = new OssUploadRecordController(
            _studentClient.Object,
            _mistakeClient.Object,
            _logger.Object,
            _ossService.Object
        );
    }

    [Fact]
    public async Task LegacyCheck_NoMistakes_ReturnsNotLegacy()
    {
        var emptyResponse = new MistakeItemsByUploadResult();
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("message")!.GetValue(typed).Should().Be("该记录没有关联错题");
    }

    [Fact]
    public async Task LegacyCheck_AllReviewed_ReturnsLegacy()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        mistake1.SourceRegions.Add(new MistakeSourceRegionDto { SourceImagePath = "uploads/test.jpg" });

        var mistake2 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Rejected,
            StudentId = "student-1"
        };

        var response = new MistakeItemsByUploadResult();
        response.Items.Add(mistake1);
        response.Items.Add(mistake2);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("mistakeCount")!.GetValue(typed).Should().Be(2);
        typed.GetType().GetProperty("hasUploadPathImage")!.GetValue(typed).Should().Be(true);
    }

    [Fact]
    public async Task LegacyCheck_AllReviewed_NoUploadPath_ReturnsLegacyWithNoUploadPath()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        mistake1.SourceRegions.Add(new MistakeSourceRegionDto { SourceImagePath = "mistakes/test.jpg" });

        var response = new MistakeItemsByUploadResult();
        response.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("hasUploadPathImage")!.GetValue(typed).Should().Be(false);
    }

    [Fact]
    public async Task LegacyCheck_HasPendingReview_ReturnsNotLegacy()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        var mistake2 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.PendingReview,
            StudentId = "student-1"
        };

        var response = new MistakeItemsByUploadResult();
        response.Items.Add(mistake1);
        response.Items.Add(mistake2);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("pendingCount")!.GetValue(typed).Should().Be(1);
    }

    [Fact]
    public async Task LegacyClean_NoMistakes_ReturnsBadRequest()
    {
        var emptyResponse = new MistakeItemsByUploadResult();
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_HasPendingReview_ReturnsBadRequest()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.PendingReview,
            StudentId = "student-1"
        };

        var response = new MistakeItemsByUploadResult();
        response.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_AllReviewed_CompleteReviewFails_ReturnsBadRequest()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };

        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var completeResponse = new CompleteUploadReviewResult
        {
            Success = false,
            ErrorMessage = "Migration failed"
        };
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_AllReviewed_Success()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };

        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var completeResponse = new CompleteUploadReviewResult
        {
            Success = true
        };
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        // DeleteUploadRecordAfterReviewAsync returns Task; default Moq behavior returns completed task (success)
        _studentClient
            .Setup(c => c.DeleteUploadRecordAfterReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.LegacyClean("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("uploadRecordId")!.GetValue(typed).Should().Be("test-record-id");
    }

    [Fact]
    public async Task LegacyClean_CompleteReviewSuccess_DeleteFails_ReturnsBadRequest()
    {
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };

        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var completeResponse = new CompleteUploadReviewResult
        {
            Success = true
        };
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        // DeleteUploadRecordAfterReviewAsync throws HttpRequestException on failure; controller maps to BadRequest
        _studentClient
            .Setup(c => c.DeleteUploadRecordAfterReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Delete failed"));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyCheck_HttpException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await _controller.LegacyCheck("test-record-id");

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task LegacyClean_HttpException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task LegacyClean_WithRemovedImagePaths_CallsRemoveImagesFromRecord()
    {
        // Arrange
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var completeResponse = new CompleteUploadReviewResult { Success = true };
        completeResponse.RemovedImagePaths.Add("uploads/img1.jpg");
        completeResponse.RemovedImagePaths.Add("uploads/img2.jpg");
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        _studentClient
            .Setup(c => c.RemoveImagesFromRecordAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoveImagesResult { Success = true });

        _studentClient
            .Setup(c => c.DeleteUploadRecordAfterReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.LegacyClean("test-record-id");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);

        _studentClient.Verify(c => c.RemoveImagesFromRecordAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LegacyClean_EmptyRemovedImagePaths_SkipsRemoveImagesFromRecord()
    {
        // Arrange
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var completeResponse = new CompleteUploadReviewResult { Success = true };
        // RemovedImagePaths is empty by default
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        _studentClient
            .Setup(c => c.DeleteUploadRecordAfterReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.LegacyClean("test-record-id");

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        _studentClient.Verify(c => c.RemoveImagesFromRecordAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyClean_EmptyStudentId_ReturnsBadRequest()
    {
        // Arrange - mistake item with empty StudentId
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = ""
        };
        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        // Act
        var result = await _controller.LegacyClean("test-record-id");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_RemoveImagesThrowsException_Returns500()
    {
        // Arrange
        var mistake1 = new MistakeItemDto
        {
            ReviewStatus = MistakeReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        var listResponse = new MistakeItemsByUploadResult();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var completeResponse = new CompleteUploadReviewResult { Success = true };
        completeResponse.RemovedImagePaths.Add("uploads/img1.jpg");
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        _studentClient
            .Setup(c => c.RemoveImagesFromRecordAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        // Act
        var result = await _controller.LegacyClean("test-record-id");

        // Assert
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }
}
