using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.Common.Oss;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// 分配逻辑 UT：覆盖 imageIndices 边界校验、remainingImageCount 计算、分配状态校验。
/// 分配逻辑位于 OssUploadRecordController.AssignUploadRecord。
/// </summary>
public class AssignmentControllerTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _learningClient;
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<ILogger<OssUploadRecordController>> _logger;
    private readonly Mock<IOssService> _ossService;
    private readonly OssUploadRecordController _controller;

    public AssignmentControllerTests()
    {
        _learningClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _mistakeClient = new Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _logger = new Mock<ILogger<OssUploadRecordController>>();
        _ossService = new Mock<IOssService>();

        _controller = new OssUploadRecordController(
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

    private static SProto.UploadRecordDto CreateUploadRecordDto(
        string id = "record-1",
        string studentId = "student-1",
        SProto.UploadStatus status = SProto.UploadStatus.Pending,
        string[]? imagePaths = null,
        string comments = "",
        string createdAt = "1700000000",
        string updatedAt = "1700000001")
    {
        var dto = new SProto.UploadRecordDto
        {
            Id = id,
            StudentId = studentId,
            Status = status,
            Comments = comments,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
        if (imagePaths != null)
            dto.ImagePaths.AddRange(imagePaths);
        return dto;
    }

    private void SetupGetUploadRecord(SProto.UploadRecordDto record)
    {
        _learningClient
            .Setup(c => c.GetUploadRecordAsync(
                It.IsAny<SProto.GetUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(record));
    }

    private void SetupSubmitMistakeUpload(bool success, string[]? createdItemIds = null, string? errorMessage = null)
    {
        var response = new MistakeProto.SubmitMistakeUploadResponse
        {
            Success = success,
            ErrorMessage = errorMessage ?? string.Empty
        };
        if (createdItemIds != null)
            response.CreatedItemIds.AddRange(createdItemIds);

        _mistakeClient
            .Setup(c => c.SubmitMistakeUploadAsync(
                It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    private void SetupMarkUnderReview()
    {
        _learningClient
            .Setup(c => c.MarkUploadRecordUnderReviewAsync(
                It.IsAny<SProto.MarkUploadRecordUnderReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(new SProto.BoolResponse { Success = true }));
    }

    // ============================================================
    // imageIndices 边界校验
    // ============================================================

    [Fact]
    public async Task Assign_EmptyImageIndices_SkipsAssignmentWithWarning()
    {
        // imageIndices 为空列表
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" });
        SetupGetUploadRecord(record);
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int>(), Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        var warnings = (List<string>)typed.GetType().GetProperty("warnings")!.GetValue(typed)!;
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain("No valid images");

        // SubmitMistakeUpload 不应被调用
        _mistakeClient.Verify(c => c.SubmitMistakeUploadAsync(
            It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_DuplicateImageIndices_SubmitsOnceWithDuplicatedPaths()
    {
        // 重复索引：0 出现两次
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" });
        SetupGetUploadRecord(record);
        SetupSubmitMistakeUpload(true, new[] { "item-1" });
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0, 0 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);

        // SubmitMistakeUpload 应被调用一次（包含重复路径）
        _mistakeClient.Verify(c => c.SubmitMistakeUploadAsync(
            It.Is<MistakeProto.SubmitMistakeUploadRequest>(r =>
                r.ImagePaths.Count == 2 && r.ImagePaths[0] == "uploads/img1.jpg" && r.ImagePaths[1] == "uploads/img1.jpg"),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_OutOfRangeImageIndices_SkipsWithWarning()
    {
        // 越界索引：5 和 10 超出 2 张图片的范围
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" });
        SetupGetUploadRecord(record);
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 5, 10 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        var warnings = (List<string>)typed.GetType().GetProperty("warnings")!.GetValue(typed)!;
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain("5");

        // SubmitMistakeUpload 不应被调用
        _mistakeClient.Verify(c => c.SubmitMistakeUploadAsync(
            It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_NegativeImageIndices_SkipsInvalidIndices()
    {
        // 负数索引
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" });
        SetupGetUploadRecord(record);
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { -1, -2 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        var warnings = (List<string>)typed.GetType().GetProperty("warnings")!.GetValue(typed)!;
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain("No valid images");
    }

    [Fact]
    public async Task Assign_MixedValidAndInvalidIndices_OnlySubmitsValidOnes()
    {
        // 混合有效和无效索引
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg", "uploads/img3.jpg" });
        SetupGetUploadRecord(record);
        SetupSubmitMistakeUpload(true, new[] { "item-1" });
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0, 5, 2 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;

        // SubmitMistakeUpload 应只包含有效索引对应的路径
        _mistakeClient.Verify(c => c.SubmitMistakeUploadAsync(
            It.Is<MistakeProto.SubmitMistakeUploadRequest>(r =>
                r.ImagePaths.Count == 2 &&
                r.ImagePaths.Contains("uploads/img1.jpg") &&
                r.ImagePaths.Contains("uploads/img3.jpg")),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============================================================
    // remainingImageCount 计算
    // ============================================================

    [Fact]
    public async Task Assign_AllImagesAssigned_RemainingImageCountIsZero()
    {
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" });
        SetupGetUploadRecord(record);
        SetupSubmitMistakeUpload(true, new[] { "item-1" });
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0, 1 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        ((int)typed.GetType().GetProperty("remainingImageCount")!.GetValue(typed)!).Should().Be(0);
        ((int)typed.GetType().GetProperty("totalImageCount")!.GetValue(typed)!).Should().Be(2);
    }

    [Fact]
    public async Task Assign_PartialAssignment_RemainingImageCountReflectsUnassigned()
    {
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg", "uploads/img3.jpg" });
        SetupGetUploadRecord(record);
        SetupSubmitMistakeUpload(true, new[] { "item-1" });
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        ((int)typed.GetType().GetProperty("remainingImageCount")!.GetValue(typed)!).Should().Be(2);
        ((int)typed.GetType().GetProperty("totalImageCount")!.GetValue(typed)!).Should().Be(3);
    }

    [Fact]
    public async Task Assign_MultipleAssignmentsWithOverlap_RemainingImageCountDeduplicates()
    {
        // 两个 assignment 都包含索引 0（同一张图片），remainingImageCount 应正确去重
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg", "uploads/img3.jpg" });
        SetupGetUploadRecord(record);

        var callCount = 0;
        _mistakeClient
            .Setup(c => c.SubmitMistakeUploadAsync(
                It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                var response = new MistakeProto.SubmitMistakeUploadResponse { Success = true };
                response.CreatedItemIds.Add($"item-{callCount}");
                return CreateAsyncCall(response);
            });

        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0, 1 }, Subject = 1, Grade = 3 },
                new() { ImageIndices = new List<int> { 0, 2 }, Subject = 2, Grade = 4 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        // img1 被两个 assignment 选中，但 remainingImageCount 应去重计算
        // 已分配: img1, img2, img3 → remaining = 0
        ((int)typed.GetType().GetProperty("remainingImageCount")!.GetValue(typed)!).Should().Be(0);
    }

    // ============================================================
    // 分配状态校验
    // ============================================================

    [Fact]
    public async Task Assign_SubmitFails_ReturnsAllSuccessFalseWithWarnings()
    {
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg" });
        SetupGetUploadRecord(record);
        SetupSubmitMistakeUpload(false, errorMessage: "Mistake creation failed");
        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0 }, Subject = 1, Grade = 3 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(false);
        var warnings = (List<string>)typed.GetType().GetProperty("warnings")!.GetValue(typed)!;
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain("Mistake creation failed");
    }

    [Fact]
    public async Task Assign_RecordFetchFails_Returns500()
    {
        _learningClient
            .Setup(c => c.GetUploadRecordAsync(
                It.IsAny<SProto.GetUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>()
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Assign_MultipleAssignments_PartialFailure_ContinuesWithRemaining()
    {
        var record = CreateUploadRecordDto(
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" });
        SetupGetUploadRecord(record);

        var callCount = 0;
        _mistakeClient
            .Setup(c => c.SubmitMistakeUploadAsync(
                It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateAsyncCall(new MistakeProto.SubmitMistakeUploadResponse
                    {
                        Success = false,
                        ErrorMessage = "First assignment failed"
                    });
                }
                var response = new MistakeProto.SubmitMistakeUploadResponse { Success = true };
                response.CreatedItemIds.Add("item-2");
                return CreateAsyncCall(response);
            });

        SetupMarkUnderReview();

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0 }, Subject = 1, Grade = 3 },
                new() { ImageIndices = new List<int> { 1 }, Subject = 2, Grade = 4 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(false);
        // 第二个 assignment 成功，所以 remainingImageCount 应为 1（只有 img2 被分配）
        ((int)typed.GetType().GetProperty("remainingImageCount")!.GetValue(typed)!).Should().Be(1);
    }
}
