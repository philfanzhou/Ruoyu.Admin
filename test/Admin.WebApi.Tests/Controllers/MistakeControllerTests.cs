using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public class MistakeControllerTests
{
    private readonly Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient> _managementClient;
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _studentLearningClient;
    private readonly Mock<ILogger<MistakeController>> _logger;
    private readonly MistakeController _controller;

    public MistakeControllerTests()
    {
        _mistakeClient = new Mock<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _managementClient = new Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient>();
        _studentLearningClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _logger = new Mock<ILogger<MistakeController>>();
        _controller = new MistakeController(
            _mistakeClient.Object,
            _managementClient.Object,
            _studentLearningClient.Object,
            _logger.Object);
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

    private static MistakeProto.MistakeItemDto CreateMistakeItemDto(
        string id = "mistake-1",
        string studentId = "student-1",
        int subject = 1,
        int grade = 3,
        string sourceUploadId = "upload-1",
        MistakeProto.ReviewStatus reviewStatus = MistakeProto.ReviewStatus.PendingReview,
        string reviewerId = "",
        string reviewedAt = "",
        string reviewComment = "",
        int type = 0,
        string questionId = "",
        string createdAt = "2024-01-01T00:00:00Z",
        string updatedAt = "2024-01-02T00:00:00Z")
    {
        var dto = new MistakeProto.MistakeItemDto
        {
            Id = id,
            StudentId = studentId,
            Subject = subject,
            Grade = grade,
            SourceUploadId = sourceUploadId,
            ReviewStatus = reviewStatus,
            ReviewerId = reviewerId,
            ReviewedAt = reviewedAt,
            ReviewComment = reviewComment,
            Type = type,
            QuestionId = questionId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
        return dto;
    }

    private static MistakeProto.SourceRegion CreateSourceRegion(
        string sourceImagePath = "mistakes/img1.jpg",
        int x1 = 0, int y1 = 0, int x2 = 100, int y2 = 100,
        bool withBoundingBox = true)
    {
        var region = new MistakeProto.SourceRegion
        {
            SourceImagePath = sourceImagePath
        };
        if (withBoundingBox)
        {
            region.BoundingBox = new MistakeProto.BoundingBox
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2
            };
        }
        return region;
    }

    private void SetupGetStudentAsync(string studentId, string name)
    {
        var studentDto = new SProto.StudentDto
        {
            Id = studentId,
            Name = name,
            Grade = SProto.Grade.Primary1,
            CreatedAt = 1000,
            UpdatedAt = 2000
        };

        _managementClient
            .Setup(c => c.GetStudentAsync(
                It.Is<SProto.GetStudentRequest>(r => r.StudentId == studentId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));
    }

    private void SetupGetStudentAsyncThrows(string studentId)
    {
        _managementClient
            .Setup(c => c.GetStudentAsync(
                It.Is<SProto.GetStudentRequest>(r => r.StudentId == studentId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Student not found")));
    }

    // ============================================================
    // GetMistakeItems Tests
    // ============================================================

    [Fact]
    public async Task GetMistakeItems_Success_ReturnsPaginatedItemsWithStudentNames()
    {
        var item = CreateMistakeItemDto(studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("mistakes/img1.jpg"));

        var response = new MistakeProto.MistakeItemPageResult();
        response.Items.Add(item);
        response.PageMeta = new MistakeProto.PageMeta
        {
            TotalCount = 1, Page = 1, Size = 20, TotalPages = 1
        };

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        SetupGetStudentAsync("s1", "Alice");

        var result = await _controller.GetMistakeItems("s1", 1, 3, null, 1, 20);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.total).Should().Be(1);
        ((int)data.page).Should().Be(1);
        ((int)data.pageSize).Should().Be(20);
        ((int)data.totalPages).Should().Be(1);
    }

    [Fact]
    public async Task GetMistakeItems_StudentQueryFails_FallsBackToStudentId()
    {
        var item = CreateMistakeItemDto(studentId: "s1");
        var response = new MistakeProto.MistakeItemPageResult();
        response.Items.Add(item);
        response.PageMeta = new MistakeProto.PageMeta
        {
            TotalCount = 1, Page = 1, Size = 20, TotalPages = 1
        };

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        SetupGetStudentAsyncThrows("s1");

        var result = await _controller.GetMistakeItems("s1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMistakeItems_Page0_ResetsTo1()
    {
        var response = new MistakeProto.MistakeItemPageResult();
        response.PageMeta = new MistakeProto.PageMeta { TotalCount = 0, Page = 1, Size = 10, TotalPages = 0 };

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.GetMistakeItems(page: 0, size: 10);

        _mistakeClient.Verify(c => c.GetMistakeItemListAsync(
            It.Is<MistakeProto.GetMistakeItemListRequest>(r => r.Page == 1),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMistakeItems_Size0_ResetsTo10()
    {
        var response = new MistakeProto.MistakeItemPageResult();
        response.PageMeta = new MistakeProto.PageMeta { TotalCount = 0, Page = 1, Size = 10, TotalPages = 0 };

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.GetMistakeItems(page: 1, size: 0);

        _mistakeClient.Verify(c => c.GetMistakeItemListAsync(
            It.Is<MistakeProto.GetMistakeItemListRequest>(r => r.Size == 10),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMistakeItems_Size200_ClampedTo100()
    {
        var response = new MistakeProto.MistakeItemPageResult();
        response.PageMeta = new MistakeProto.PageMeta { TotalCount = 0, Page = 1, Size = 100, TotalPages = 0 };

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.GetMistakeItems(page: 1, size: 200);

        _mistakeClient.Verify(c => c.GetMistakeItemListAsync(
            It.Is<MistakeProto.GetMistakeItemListRequest>(r => r.Size == 100),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMistakeItems_ReviewStatus1_SendsConfirmedInGrpcRequest()
    {
        var response = new MistakeProto.MistakeItemPageResult();
        response.PageMeta = new MistakeProto.PageMeta { TotalCount = 0, Page = 1, Size = 20, TotalPages = 0 };

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        // reviewStatus=1 maps to ReviewStatusPendingReview in proto enum (value 1)
        await _controller.GetMistakeItems(reviewStatus: 1);

        _mistakeClient.Verify(c => c.GetMistakeItemListAsync(
            It.Is<MistakeProto.GetMistakeItemListRequest>(r =>
                r.ReviewStatus == MistakeProto.ReviewStatus.PendingReview),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMistakeItems_GrpcException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MistakeProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var result = await _controller.GetMistakeItems();

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to get mistake items");
    }

    // ============================================================
    // GetMistakeItem Tests
    // ============================================================

    [Fact]
    public async Task GetMistakeItem_Success_ReturnsItemWithSourceRegionsAndBoundingBox()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("mistakes/img1.jpg", 10, 20, 110, 120));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        SetupGetStudentAsync("s1", "Bob");

        var result = await _controller.GetMistakeItem("m1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMistakeItem_BoundingBoxNull_InResponseStillNull()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("mistakes/img1.jpg", withBoundingBox: false));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        SetupGetStudentAsync("s1", "Bob");

        var result = await _controller.GetMistakeItem("m1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMistakeItem_StudentQueryFails_FallsBackToStudentId()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        SetupGetStudentAsyncThrows("s1");

        var result = await _controller.GetMistakeItem("m1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMistakeItem_GrpcException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var result = await _controller.GetMistakeItem("m1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to get mistake item");
    }

    // ============================================================
    // GetMistakesByUploadId Tests
    // ============================================================

    [Fact]
    public async Task GetMistakesByUploadId_Success_ReturnsItemsWithStudentNames()
    {
        var item1 = CreateMistakeItemDto(id: "m1", studentId: "s1");
        var item2 = CreateMistakeItemDto(id: "m2", studentId: "s2");

        var response = new MistakeProto.MistakeItemListResponse();
        response.Items.Add(item1);
        response.Items.Add(item2);

        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        SetupGetStudentAsync("s1", "Alice");
        SetupGetStudentAsync("s2", "Bob");

        var result = await _controller.GetMistakesByUploadId("upload-1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMistakesByUploadId_GrpcException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemsByUploadAsync(
                It.IsAny<MistakeProto.GetMistakeItemsByUploadRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var result = await _controller.GetMistakesByUploadId("upload-1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to get mistakes by upload id");
    }

    // ============================================================
    // UpdateMistakeItem Tests
    // ============================================================

    [Fact]
    public async Task UpdateMistakeItem_Success_ReturnsItemWithStudentName()
    {
        var updatedItem = CreateMistakeItemDto(id: "m1", studentId: "s1", subject: 2, grade: 5);

        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(updatedItem));

        SetupGetStudentAsync("s1", "Alice");

        var request = new UpdateMistakeRequest { StudentId = "s1", Subject = 2, Grade = 5 };
        var result = await _controller.UpdateMistakeItem("m1", request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateMistakeItem_NullFields_SendsEmptyStringAndZeroInGrpcRequest()
    {
        var updatedItem = CreateMistakeItemDto(id: "m1", studentId: "", subject: 0, grade: 0);

        MistakeProto.UpdateMistakeItemRequest? capturedRequest = null;
        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<MistakeProto.UpdateMistakeItemRequest, Metadata, DateTime?, CancellationToken>(
                (r, _, _, _) => capturedRequest = r)
            .Returns(CreateAsyncCall(updatedItem));

        var request = new UpdateMistakeRequest { StudentId = null, Subject = null, Grade = null };
        await _controller.UpdateMistakeItem("m1", request);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.StudentId.Should().BeEmpty();
        capturedRequest.Subject.Should().Be(0);
        capturedRequest.Grade.Should().Be(0);
    }

    [Fact]
    public async Task UpdateMistakeItem_GrpcException_Returns500()
    {
        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var request = new UpdateMistakeRequest { StudentId = "s1", Subject = 1, Grade = 3 };
        var result = await _controller.UpdateMistakeItem("m1", request);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to update mistake item");
    }

    // ============================================================
    // MigrateImages Tests
    // ============================================================

    [Fact]
    public async Task MigrateImages_AllPathsAlreadyInMistakes_ReturnsMigratedCount0()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("mistakes/img1.jpg"));
        item.SourceRegions.Add(CreateSourceRegion("mistakes/img2.jpg"));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        var result = await _controller.MigrateImages("m1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.migratedCount).Should().Be(0);

        _studentLearningClient.Verify(c => c.MigrateImagesToMistakeAsync(
            It.IsAny<SProto.MigrateImagesToMistakeRequest>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MigrateImages_PathsInUploads_CallsMigrateGrpc_ReturnsMigratedCount()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("uploads/2024/01/img1.jpg"));
        item.SourceRegions.Add(CreateSourceRegion("mistakes/img2.jpg"));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        var updateResponse = CreateMistakeItemDto(id: "m1", studentId: "s1");
        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(updateResponse));

        var migrateResponse = new SProto.MigrateImagesToMistakeResponse();
        migrateResponse.Results.Add(new SProto.MigrateImageResult
        {
            SourcePath = "uploads/2024/01/img1.jpg",
            NewPath = "mistakes/2024/01/img1.jpg",
            Success = true
        });

        _studentLearningClient
            .Setup(c => c.MigrateImagesToMistakeAsync(
                It.IsAny<SProto.MigrateImagesToMistakeRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(migrateResponse));

        var result = await _controller.MigrateImages("m1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.migratedCount).Should().Be(1);

        _studentLearningClient.Verify(c => c.MigrateImagesToMistakeAsync(
            It.IsAny<SProto.MigrateImagesToMistakeRequest>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MigrateImages_SameOldPathMultipleTimes_MigratedOnlyOnce()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("uploads/2024/01/img1.jpg"));
        item.SourceRegions.Add(CreateSourceRegion("uploads/2024/01/img1.jpg"));
        item.SourceRegions.Add(CreateSourceRegion("uploads/2024/01/img1.jpg"));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        var updateResponse = CreateMistakeItemDto(id: "m1", studentId: "s1");
        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(updateResponse));

        var migrateResponse = new SProto.MigrateImagesToMistakeResponse();
        migrateResponse.Results.Add(new SProto.MigrateImageResult
        {
            SourcePath = "uploads/2024/01/img1.jpg",
            NewPath = "mistakes/2024/01/img1.jpg",
            Success = true
        });

        _studentLearningClient
            .Setup(c => c.MigrateImagesToMistakeAsync(
                It.IsAny<SProto.MigrateImagesToMistakeRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(migrateResponse));

        var result = await _controller.MigrateImages("m1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.migratedCount).Should().Be(1);
    }

    [Fact]
    public async Task MigrateImages_MigrateGrpcThrows_Returns500()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("uploads/2024/01/img1.jpg"));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        _studentLearningClient
            .Setup(c => c.MigrateImagesToMistakeAsync(
                It.IsAny<SProto.MigrateImagesToMistakeRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var result = await _controller.MigrateImages("m1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task MigrateImages_GetMistakeItemThrows_Returns500()
    {
        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var result = await _controller.MigrateImages("m1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to migrate images");
    }

    [Fact]
    public async Task MigrateImages_UpdateMistakeItemThrows_Returns500()
    {
        var item = CreateMistakeItemDto(id: "m1", studentId: "s1");
        item.SourceRegions.Add(CreateSourceRegion("uploads/2024/01/img1.jpg"));

        _mistakeClient
            .Setup(c => c.GetMistakeItemAsync(
                It.IsAny<MistakeProto.IdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(item));

        var migrateResponse = new SProto.MigrateImagesToMistakeResponse();
        migrateResponse.Results.Add(new SProto.MigrateImageResult
        {
            SourcePath = "uploads/2024/01/img1.jpg",
            NewPath = "mistakes/2024/01/img1.jpg",
            Success = true
        });

        _studentLearningClient
            .Setup(c => c.MigrateImagesToMistakeAsync(
                It.IsAny<SProto.MigrateImagesToMistakeRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(migrateResponse));

        _mistakeClient
            .Setup(c => c.UpdateMistakeItemAsync(
                It.IsAny<MistakeProto.UpdateMistakeItemRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Internal, "gRPC error")));

        var result = await _controller.MigrateImages("m1");

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var error = statusResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Failed to migrate images");
    }
}
