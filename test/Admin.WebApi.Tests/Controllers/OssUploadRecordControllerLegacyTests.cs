using Admin.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.Common.Oss;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using Grpc.Core;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public class OssUploadRecordControllerLegacyTests
{
    private readonly Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient> _managementClient;
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _learningClient;
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<ILogger<OssUploadRecordController>> _logger;
    private readonly Mock<IOssService> _ossService;
    private readonly OssUploadRecordController _controller;

    public OssUploadRecordControllerLegacyTests()
    {
        _managementClient = new Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient>();
        _learningClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _mistakeClient = new Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _logger = new Mock<ILogger<OssUploadRecordController>>();
        _ossService = new Mock<IOssService>();

        _controller = new OssUploadRecordController(
            _managementClient.Object,
            _learningClient.Object,
            _mistakeClient.Object,
            _logger.Object,
            _ossService.Object
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

    [Fact]
    public async Task LegacyCheck_NoMistakes_ReturnsNotLegacy()
    {
        var emptyResponse = new MistakeProto.MistakeItemListResponse();
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(emptyResponse));

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("message")!.GetValue(typed).Should().Be("该记录没有关联错题");
    }

    [Fact]
    public async Task LegacyCheck_AllReviewed_ReturnsLegacy()
    {
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        mistake1.SourceRegions.Add(new MistakeProto.SourceRegion { SourceImagePath = "uploads/test.jpg" });

        var mistake2 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Rejected,
            StudentId = "student-1"
        };

        var response = new MistakeProto.MistakeItemListResponse();
        response.Items.Add(mistake1);
        response.Items.Add(mistake2);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

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
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        mistake1.SourceRegions.Add(new MistakeProto.SourceRegion { SourceImagePath = "mistakes/test.jpg" });

        var response = new MistakeProto.MistakeItemListResponse();
        response.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("hasUploadPathImage")!.GetValue(typed).Should().Be(false);
    }

    [Fact]
    public async Task LegacyCheck_HasPendingReview_ReturnsNotLegacy()
    {
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Confirmed,
            StudentId = "student-1"
        };
        var mistake2 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.PendingReview,
            StudentId = "student-1"
        };

        var response = new MistakeProto.MistakeItemListResponse();
        response.Items.Add(mistake1);
        response.Items.Add(mistake2);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.LegacyCheck("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isLegacy")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("pendingCount")!.GetValue(typed).Should().Be(1);
    }

    [Fact]
    public async Task LegacyClean_NoMistakes_ReturnsBadRequest()
    {
        var emptyResponse = new MistakeProto.MistakeItemListResponse();
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(emptyResponse));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_HasPendingReview_ReturnsBadRequest()
    {
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.PendingReview,
            StudentId = "student-1"
        };

        var response = new MistakeProto.MistakeItemListResponse();
        response.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_AllReviewed_CompleteReviewFails_ReturnsBadRequest()
    {
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Confirmed,
            StudentId = "student-1"
        };

        var listResponse = new MistakeProto.MistakeItemListResponse();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(listResponse));

        var completeResponse = new MistakeProto.CompleteUploadReviewResponse
        {
            Success = false,
            ErrorMessage = "Migration failed"
        };
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<MistakeProto.CompleteUploadReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(completeResponse));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyClean_AllReviewed_Success()
    {
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Confirmed,
            StudentId = "student-1"
        };

        var listResponse = new MistakeProto.MistakeItemListResponse();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(listResponse));

        var completeResponse = new MistakeProto.CompleteUploadReviewResponse
        {
            Success = true
        };
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<MistakeProto.CompleteUploadReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(completeResponse));

        var deleteResponse = new SProto.BoolResponse
        {
            Success = true
        };
        _learningClient
            .Setup(c => c.DeleteUploadRecordAfterReviewAsync(
                It.IsAny<SProto.DeleteUploadRecordAfterReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(deleteResponse));

        var result = await _controller.LegacyClean("test-record-id");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("uploadRecordId")!.GetValue(typed).Should().Be("test-record-id");
    }

    [Fact]
    public async Task LegacyClean_CompleteReviewSuccess_DeleteFails_ReturnsBadRequest()
    {
        var mistake1 = new MistakeProto.MistakeItemDto
        {
            ReviewStatus = MistakeProto.ReviewStatus.Confirmed,
            StudentId = "student-1"
        };

        var listResponse = new MistakeProto.MistakeItemListResponse();
        listResponse.Items.Add(mistake1);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(listResponse));

        var completeResponse = new MistakeProto.CompleteUploadReviewResponse
        {
            Success = true
        };
        _mistakeClient
            .Setup(c => c.CompleteUploadReviewAsync(
                It.IsAny<MistakeProto.CompleteUploadReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(completeResponse));

        var deleteResponse = new SProto.BoolResponse
        {
            Success = false,
            ErrorMessage = "Delete failed"
        };
        _learningClient
            .Setup(c => c.DeleteUploadRecordAfterReviewAsync(
                It.IsAny<SProto.DeleteUploadRecordAfterReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(deleteResponse));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LegacyCheck_GrpcException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.LegacyCheck("test-record-id");

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task LegacyClean_GrpcException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.LegacyClean("test-record-id");

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }
}
