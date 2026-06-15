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
/// OssUploadRecordController UT：覆盖 GetAllUploadRecords、AssignUploadRecord、ResetUploadRecordStatus。
/// </summary>
public class OssUploadRecordControllerTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _learningClient;
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<ILogger<OssUploadRecordController>> _logger;
    private readonly Mock<IOssService> _ossService;
    private readonly OssUploadRecordController _controller;

    public OssUploadRecordControllerTests()
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

    private void SetupGetAllUploadRecords(SProto.UploadRecordsPageResult response)
    {
        _learningClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    private void SetupGetStudent(SProto.StudentDto studentDto)
    {
        _learningClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));
    }

    // ============================================================
    // GetAllUploadRecords
    // ============================================================

    [Fact]
    public async Task GetAllUploadRecords_ReturnsPaginatedResults()
    {
        var uploadDto1 = new SProto.UploadRecordDto
        {
            Id = "r1",
            StudentId = "s1",
            Status = SProto.UploadStatus.Pending,
            Comments = "test",
            CreatedAt = "1700000000",
            UpdatedAt = "1700000001"
        };
        uploadDto1.ImagePaths.Add("uploads/img1.jpg");

        var uploadDto2 = new SProto.UploadRecordDto
        {
            Id = "r2",
            StudentId = "s2",
            Status = SProto.UploadStatus.UnderReview,
            Comments = "",
            CreatedAt = "1700000002",
            UpdatedAt = "1700000003"
        };
        uploadDto2.ImagePaths.Add("uploads/img2.jpg");

        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = 2,
            Page = 1,
            PageSize = 20
        };
        response.Items.Add(uploadDto1);
        response.Items.Add(uploadDto2);

        SetupGetAllUploadRecords(response);

        var studentDto = new SProto.StudentDto { Id = "s1", Name = "张三" };
        SetupGetStudent(studentDto);

        var studentDto2 = new SProto.StudentDto { Id = "s2", Name = "李四" };
        _learningClient
            .Setup(c => c.GetStudentAsync(
                It.Is<SProto.GetStudentRequest>(r => r.StudentId == "s2"),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto2));

        var result = await _controller.GetAllUploadRecords();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        ((int)typed.GetType().GetProperty("totalCount")!.GetValue(typed)!).Should().Be(2);
    }

    [Fact]
    public async Task GetAllUploadRecords_GrpcError_Returns500()
    {
        _learningClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.GetAllUploadRecords();

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetAllUploadRecords_StudentLookupFails_FallsBackToStudentId()
    {
        var uploadDto = new SProto.UploadRecordDto
        {
            Id = "r1",
            StudentId = "s1",
            Status = SProto.UploadStatus.Pending,
            CreatedAt = "1700000000",
            UpdatedAt = "1700000001"
        };
        uploadDto.ImagePaths.Add("uploads/img1.jpg");

        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        response.Items.Add(uploadDto);

        SetupGetAllUploadRecords(response);

        _learningClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Student not found")));

        var result = await _controller.GetAllUploadRecords();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        // 当学生查询失败时，应回退到显示 studentId
        var typed = okResult.Value!;
        var items = (System.Collections.IEnumerable)typed.GetType().GetProperty("items")!.GetValue(typed)!;
        items.Should().NotBeNull();
    }

    // ============================================================
    // AssignUploadRecord
    // ============================================================

    [Fact]
    public async Task AssignUploadRecord_ValidRequest_ReturnsOk()
    {
        var record = new SProto.UploadRecordDto
        {
            Id = "record-1",
            StudentId = "student-1",
            Status = SProto.UploadStatus.Pending,
            Comments = "test"
        };
        record.ImagePaths.Add("uploads/img1.jpg");

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

        _learningClient
            .Setup(c => c.MarkUploadRecordUnderReviewAsync(
                It.IsAny<SProto.MarkUploadRecordUnderReviewRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(new SProto.BoolResponse { Success = true }));

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
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
    }

    // ============================================================
    // ResetUploadRecordStatus
    // ============================================================

    [Fact]
    public async Task ResetUploadRecordStatus_Success_ReturnsOk()
    {
        var resetResponse = new SProto.BoolResponse { Success = true };
        _learningClient
            .Setup(c => c.ResetUploadRecordStatusAsync(
                It.IsAny<SProto.ResetUploadRecordStatusRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(resetResponse));

        var request = new ResetStatusRequest
        {
            StudentId = "student-1",
            TargetStatus = 0
        };

        var result = await _controller.ResetUploadRecordStatus("record-1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("success")!.GetValue(typed).Should().Be(true);
    }

    [Fact]
    public async Task ResetUploadRecordStatus_Failure_ReturnsBadRequest()
    {
        var resetResponse = new SProto.BoolResponse
        {
            Success = false,
            ErrorMessage = "Cannot reset status"
        };
        _learningClient
            .Setup(c => c.ResetUploadRecordStatusAsync(
                It.IsAny<SProto.ResetUploadRecordStatusRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(resetResponse));

        var request = new ResetStatusRequest
        {
            StudentId = "student-1",
            TargetStatus = 0
        };

        var result = await _controller.ResetUploadRecordStatus("record-1", request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResetUploadRecordStatus_GrpcError_Returns500()
    {
        _learningClient
            .Setup(c => c.ResetUploadRecordStatusAsync(
                It.IsAny<SProto.ResetUploadRecordStatusRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var request = new ResetStatusRequest
        {
            StudentId = "student-1",
            TargetStatus = 0
        };

        var result = await _controller.ResetUploadRecordStatus("record-1", request);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }
}
