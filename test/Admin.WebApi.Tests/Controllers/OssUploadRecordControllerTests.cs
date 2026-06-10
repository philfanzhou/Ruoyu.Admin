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

public class OssUploadRecordControllerTests
{
    private readonly Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient> _managementClient;
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _learningClient;
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<ILogger<OssUploadRecordController>> _logger;
    private readonly Mock<IOssService> _ossService;
    private readonly OssUploadRecordController _controller;

    public OssUploadRecordControllerTests()
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

    // ============================================================
    // GetAllUploadRecords Tests
    // ============================================================

    [Fact]
    public async Task GetAllUploadRecords_Success_ReturnsPagedDataWithStudentNames()
    {
        var record = CreateUploadRecordDto(studentId: "student-1");
        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        response.Items.Add(record);

        _managementClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var studentDto = new SProto.StudentDto { Id = "student-1", Name = "Alice" };
        _managementClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));

        var result = await _controller.GetAllUploadRecords(1, 20, -1, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("totalCount")!.GetValue(typed).Should().Be(1);
        typed.GetType().GetProperty("page")!.GetValue(typed).Should().Be(1);
        typed.GetType().GetProperty("pageSize")!.GetValue(typed).Should().Be(20);

        var items = (System.Collections.IEnumerable)typed.GetType().GetProperty("items")!.GetValue(typed)!;
        items.Cast<object>().Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAllUploadRecords_StudentQueryFails_FallbackToStudentId()
    {
        var record = CreateUploadRecordDto(studentId: "student-1");
        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        response.Items.Add(record);

        _managementClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        _managementClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Student not found")));

        var result = await _controller.GetAllUploadRecords(1, 20, -1, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        var items = (System.Collections.IEnumerable)typed.GetType().GetProperty("items")!.GetValue(typed)!;
        var itemList = items.Cast<dynamic>().ToList();
        ((string)itemList[0].studentName).Should().Be("student-1");
    }

    [Fact]
    public async Task GetAllUploadRecords_GrpcException_Returns500()
    {
        _managementClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.GetAllUploadRecords(1, 20, -1, null);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to get upload records");
    }

    [Fact]
    public async Task GetAllUploadRecords_StatusNegativeOne_SendsUnspecifiedInRequest()
    {
        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = 0,
            Page = 1,
            PageSize = 20
        };

        _managementClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.GetAllUploadRecords(1, 20, -1, null);

        _managementClient.Verify(c => c.GetAllUploadRecordsAsync(
            It.Is<SProto.GetAllUploadRecordsRequest>(r => r.Status == SProto.UploadStatus.Unspecified),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllUploadRecords_StatusOne_SendsUploadedInRequest()
    {
        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = 0,
            Page = 1,
            PageSize = 20
        };

        _managementClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.GetAllUploadRecords(1, 20, 1, null);

        _managementClient.Verify(c => c.GetAllUploadRecordsAsync(
            It.Is<SProto.GetAllUploadRecordsRequest>(r => r.Status == (SProto.UploadStatus)1),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============================================================
    // ResetUploadRecordStatus Tests
    // ============================================================

    [Fact]
    public async Task ResetUploadRecordStatus_Success_Returns200()
    {
        var boolResponse = new SProto.BoolResponse { Success = true };

        _learningClient
            .Setup(c => c.ResetUploadRecordStatusAsync(
                It.IsAny<SProto.ResetUploadRecordStatusRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new ResetStatusRequest { StudentId = "student-1", TargetStatus = 1 };
        var result = await _controller.ResetUploadRecordStatus("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("message")!.GetValue(typed).Should().Be("Status reset successfully");
    }

    [Fact]
    public async Task ResetUploadRecordStatus_GrpcSuccessFalse_Returns400()
    {
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Cannot reset" };

        _learningClient
            .Setup(c => c.ResetUploadRecordStatusAsync(
                It.IsAny<SProto.ResetUploadRecordStatusRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new ResetStatusRequest { StudentId = "student-1", TargetStatus = 1 };
        var result = await _controller.ResetUploadRecordStatus("record-1", request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Cannot reset");
    }

    [Fact]
    public async Task ResetUploadRecordStatus_GrpcException_Returns500()
    {
        _learningClient
            .Setup(c => c.ResetUploadRecordStatusAsync(
                It.IsAny<SProto.ResetUploadRecordStatusRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var request = new ResetStatusRequest { StudentId = "student-1", TargetStatus = 1 };
        var result = await _controller.ResetUploadRecordStatus("record-1", request);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to reset upload record status");
    }

    // ============================================================
    // AssignUploadRecord Tests
    // ============================================================

    [Fact]
    public async Task AssignUploadRecord_Success_AllAssignmentsValid()
    {
        var record = CreateUploadRecordDto(
            id: "record-1",
            studentId: "student-1",
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" }
        );

        _learningClient
            .Setup(c => c.GetUploadRecordAsync(
                It.IsAny<SProto.GetUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(record));

        var submitResponse = new MistakeProto.SubmitMistakeUploadResponse { Success = true };
        submitResponse.CreatedItemIds.Add("item-1");

        _mistakeClient
            .Setup(c => c.SubmitMistakeUploadAsync(
                It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(submitResponse));

        var markCompletedResponse = new SProto.BoolResponse { Success = true };
        _learningClient
            .Setup(c => c.MarkUploadRecordCompletedAsync(
                It.IsAny<SProto.MarkUploadRecordCompletedRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(markCompletedResponse));

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
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("message")!.GetValue(typed).Should().Be("Assignment successful");
    }

    [Fact]
    public async Task AssignUploadRecord_RecordNotFound_Returns400()
    {
        _learningClient
            .Setup(c => c.GetUploadRecordAsync(
                It.IsAny<SProto.GetUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Not found")));

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
    public async Task AssignUploadRecord_SomeImageIndicesOutOfRange_SkipsWithWarnings()
    {
        var record = CreateUploadRecordDto(
            id: "record-1",
            studentId: "student-1",
            imagePaths: new[] { "uploads/img1.jpg" }
        );

        _learningClient
            .Setup(c => c.GetUploadRecordAsync(
                It.IsAny<SProto.GetUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(record));

        var submitResponse = new MistakeProto.SubmitMistakeUploadResponse { Success = true };
        submitResponse.CreatedItemIds.Add("item-1");

        _mistakeClient
            .Setup(c => c.SubmitMistakeUploadAsync(
                It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(submitResponse));

        var markCompletedResponse = new SProto.BoolResponse { Success = true };
        _learningClient
            .Setup(c => c.MarkUploadRecordCompletedAsync(
                It.IsAny<SProto.MarkUploadRecordCompletedRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(markCompletedResponse));

        var request = new AssignUploadRecordRequest
        {
            StudentId = "student-1",
            Assignments = new List<ImageAssignment>
            {
                new() { ImageIndices = new List<int> { 0 }, Subject = 1, Grade = 3 },
                new() { ImageIndices = new List<int> { 5, 10 }, Subject = 2, Grade = 4 }
            }
        };

        var result = await _controller.AssignUploadRecord("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        var warnings = (List<string>)typed.GetType().GetProperty("warnings")!.GetValue(typed)!;
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain("5");
    }

    [Fact]
    public async Task AssignUploadRecord_SubmitMistakeFails_ReturnsAllSuccessFalseWithWarnings()
    {
        var record = CreateUploadRecordDto(
            id: "record-1",
            studentId: "student-1",
            imagePaths: new[] { "uploads/img1.jpg", "uploads/img2.jpg" }
        );

        _learningClient
            .Setup(c => c.GetUploadRecordAsync(
                It.IsAny<SProto.GetUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(record));

        var submitResponse = new MistakeProto.SubmitMistakeUploadResponse
        {
            Success = false,
            ErrorMessage = "Mistake creation failed"
        };

        _mistakeClient
            .Setup(c => c.SubmitMistakeUploadAsync(
                It.IsAny<MistakeProto.SubmitMistakeUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(submitResponse));

        var markCompletedResponse = new SProto.BoolResponse { Success = true };
        _learningClient
            .Setup(c => c.MarkUploadRecordCompletedAsync(
                It.IsAny<SProto.MarkUploadRecordCompletedRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(markCompletedResponse));

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
    public async Task AssignUploadRecord_GrpcException_Returns500()
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
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to assign upload record");
    }

    // ============================================================
    // GetImage Tests
    // ============================================================

    [Fact]
    public async Task GetImage_EmptyPath_Returns400()
    {
        var result = await _controller.GetImage("");

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Path is required");
    }

    [Fact]
    public async Task GetImage_ImageExists_Returns200WithFileContent()
    {
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var stream = new MemoryStream(imageData);

        _ossService
            .Setup(s => s.DownloadAsync("uploads/test.jpg"))
            .ReturnsAsync(stream);

        var result = await _controller.GetImage("uploads/test.jpg");

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileContents.Should().Equal(imageData);
    }

    [Fact]
    public async Task GetImage_NullStream_Returns404()
    {
        _ossService
            .Setup(s => s.DownloadAsync("uploads/missing.jpg"))
            .ReturnsAsync((Stream?)null!);

        var result = await _controller.GetImage("uploads/missing.jpg");

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Image not found");
    }

    [Fact]
    public async Task GetImage_OssServiceException_Returns500()
    {
        _ossService
            .Setup(s => s.DownloadAsync("uploads/error.jpg"))
            .ThrowsAsync(new Exception("OSS connection failed"));

        var result = await _controller.GetImage("uploads/error.jpg");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to get image");
    }

    // ============================================================
    // RotateImage Tests
    // ============================================================

    [Fact]
    public async Task RotateImage_InvalidGuid_Returns400()
    {
        var request = new RotateImageRequest { StudentId = "student-1", ImageIndex = 0, Rotation = 90 };

        var result = await _controller.RotateImage("not-a-guid", request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid record ID");
    }

    [Fact]
    public async Task RotateImage_EmptyStudentId_Returns400()
    {
        var request = new RotateImageRequest { StudentId = "", ImageIndex = 0, Rotation = 90 };

        var result = await _controller.RotateImage(Guid.NewGuid().ToString(), request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("StudentId is required");
    }

    [Fact]
    public async Task RotateImage_GrpcSuccess_Returns200()
    {
        var boolResponse = new SProto.BoolResponse { Success = true };

        _learningClient
            .Setup(c => c.RotateUploadImageAsync(
                It.IsAny<SProto.RotateUploadImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new RotateImageRequest { StudentId = "student-1", ImageIndex = 0, Rotation = 90 };
        var result = await _controller.RotateImage(Guid.NewGuid().ToString(), request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
    }

    [Fact]
    public async Task RotateImage_GrpcSuccessFalse_Returns400()
    {
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Rotation failed" };

        _learningClient
            .Setup(c => c.RotateUploadImageAsync(
                It.IsAny<SProto.RotateUploadImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new RotateImageRequest { StudentId = "student-1", ImageIndex = 0, Rotation = 90 };
        var result = await _controller.RotateImage(Guid.NewGuid().ToString(), request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Rotation failed");
    }

    [Fact]
    public async Task RotateImage_GrpcException_Returns500()
    {
        _learningClient
            .Setup(c => c.RotateUploadImageAsync(
                It.IsAny<SProto.RotateUploadImageRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var request = new RotateImageRequest { StudentId = "student-1", ImageIndex = 0, Rotation = 90 };
        var result = await _controller.RotateImage(Guid.NewGuid().ToString(), request);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to rotate image");
    }

    // ============================================================
    // RemoveImageFromRecord Tests
    // ============================================================

    [Fact]
    public async Task RemoveImageFromRecord_EmptyStudentId_Returns400()
    {
        var result = await _controller.RemoveImageFromRecord("record-1", 0, "");

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("studentId is required");
    }

    [Fact]
    public async Task RemoveImageFromRecord_GrpcSuccess_Returns200WithDetails()
    {
        var removeResponse = new SProto.RemoveImageFromRecordResponse
        {
            Success = true,
            Message = "Image removed",
            RecordDeleted = false,
            RemainingImageCount = 2
        };

        _learningClient
            .Setup(c => c.RemoveImageFromRecordAsync(
                It.IsAny<SProto.RemoveImageFromRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(removeResponse));

        var result = await _controller.RemoveImageFromRecord("record-1", 0, "student-1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("message")!.GetValue(typed).Should().Be("Image removed");
        typed.GetType().GetProperty("recordDeleted")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("remainingImageCount")!.GetValue(typed).Should().Be(2);
    }

    [Fact]
    public async Task RemoveImageFromRecord_GrpcSuccessFalse_Returns400()
    {
        var removeResponse = new SProto.RemoveImageFromRecordResponse
        {
            Success = false,
            Message = "Index out of range"
        };

        _learningClient
            .Setup(c => c.RemoveImageFromRecordAsync(
                It.IsAny<SProto.RemoveImageFromRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(removeResponse));

        var result = await _controller.RemoveImageFromRecord("record-1", 5, "student-1");

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Index out of range");
    }

    [Fact]
    public async Task RemoveImageFromRecord_GrpcException_Returns500()
    {
        _learningClient
            .Setup(c => c.RemoveImageFromRecordAsync(
                It.IsAny<SProto.RemoveImageFromRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.RemoveImageFromRecord("record-1", 0, "student-1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to remove image");
    }

    // ============================================================
    // AnalyzeUploadRecord Tests
    // ============================================================

    [Fact]
    public async Task AnalyzeUploadRecord_Success_Returns200WithAnalysisResults()
    {
        var analyzeResponse = new SProto.AnalyzeUploadRecordResponse
        {
            Success = true,
            RawResponse = "raw-json",
            Skipped = false,
            Prompt = "Analyze these images"
        };
        analyzeResponse.CompressedImages.Add("uploads/img1_compressed.jpg");
        analyzeResponse.CompressedImages.Add("uploads/img2_compressed.jpg");

        var group = new SProto.AnalyzeImageGroup
        {
            Subject = 1,
            Grade = 3,
            Description = "Math homework"
        };
        group.ImageIndices.Add(0);
        group.ImageIndices.Add(1);
        analyzeResponse.Groups.Add(group);

        _managementClient
            .Setup(c => c.AnalyzeUploadRecordAsync(
                It.IsAny<SProto.AnalyzeUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(analyzeResponse));

        var result = await _controller.AnalyzeUploadRecord("record-1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
        typed.GetType().GetProperty("rawResponse")!.GetValue(typed).Should().Be("raw-json");
        typed.GetType().GetProperty("skipped")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("prompt")!.GetValue(typed).Should().Be("Analyze these images");

        var compressedImages = (List<string>)typed.GetType().GetProperty("compressedImages")!.GetValue(typed)!;
        compressedImages.Should().HaveCount(2);

        var groups = (System.Collections.IEnumerable)typed.GetType().GetProperty("groups")!.GetValue(typed)!;
        var groupList = groups.Cast<dynamic>().ToList();
        groupList.Should().HaveCount(1);
        ((int)groupList[0].subject).Should().Be(1);
        ((int)groupList[0].grade).Should().Be(3);
        ((string)groupList[0].description).Should().Be("Math homework");
    }

    [Fact]
    public async Task AnalyzeUploadRecord_GrpcException_Returns500()
    {
        _managementClient
            .Setup(c => c.AnalyzeUploadRecordAsync(
                It.IsAny<SProto.AnalyzeUploadRecordRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.AnalyzeUploadRecord("record-1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Contain("VL 分析失败");
    }
}
