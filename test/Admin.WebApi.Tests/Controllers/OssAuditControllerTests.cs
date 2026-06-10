using Admin.WebApi.Controllers;
using Admin.WebApi.Data;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public class OssAuditControllerTests : IDisposable
{
    private readonly AuditDbContext _dbContext;
    private readonly Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient> _studentClient;
    private readonly Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<OssAuditWorker> _auditWorker;
    private readonly Mock<ILogger<OssAuditController>> _logger;
    private readonly OssAuditController _controller;

    public OssAuditControllerTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AuditDbContext(options);

        _studentClient = new Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient>();
        _mistakeClient = new Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _auditWorker = new Mock<OssAuditWorker>(null!, null!, null!);
        _logger = new Mock<ILogger<OssAuditController>>();

        _controller = new OssAuditController(
            _dbContext,
            _studentClient.Object,
            _mistakeClient.Object,
            _auditWorker.Object,
            _logger.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    // ============================================================
    // Helper methods
    // ============================================================

    private static AsyncUnaryCall<T> CreateAsyncCall<T>(T response) where T : class
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    private OssAuditRecord CreateRecord(
        long id = 1,
        string objectPath = "uploads/test.png",
        string bucket = "default",
        long size = 1024,
        int status = 0,
        long createdAt = 1000,
        long? resolvedAt = null,
        string? note = null)
    {
        return new OssAuditRecord
        {
            Id = id,
            ObjectPath = objectPath,
            Bucket = bucket,
            Size = size,
            LastModified = 2000,
            Status = status,
            CreatedAt = createdAt,
            ResolvedAt = resolvedAt,
            Note = note,
        };
    }

    private OssAuditRun CreateRun(
        long id = 1,
        int status = 1,
        long startedAt = 1000,
        long? completedAt = 2000,
        int newZombieCount = 5,
        string triggerType = "scheduled",
        string? errorMessage = null)
    {
        return new OssAuditRun
        {
            Id = id,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            NewZombieCount = newZombieCount,
            TriggerType = triggerType,
            ErrorMessage = errorMessage,
        };
    }

    private void SetupStudentPaths(params string[] paths)
    {
        var response = new SProto.GetRegisteredOssPathsResponse();
        response.Paths.AddRange(paths);
        _studentClient
            .Setup(c => c.GetRegisteredOssPathsAsync(
                It.IsAny<SProto.Empty>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    private void SetupMistakePaths(params string[] paths)
    {
        var response = new MProto.GetAllReferencedImagePathsResponse();
        response.ImagePaths.AddRange(paths);
        _mistakeClient
            .Setup(c => c.GetAllReferencedImagePathsAsync(
                It.IsAny<MProto.Empty>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    private void SetupDeleteOssObject(bool success = true, string? errorMessage = null)
    {
        var response = new SProto.BoolResponse { Success = success, ErrorMessage = errorMessage ?? "" };
        _studentClient
            .Setup(c => c.DeleteOssObjectAsync(
                It.IsAny<SProto.DeleteOssObjectRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    // ============================================================
    // GetRecords Tests
    // ============================================================

    [Fact]
    public async Task GetRecords_ReturnsPaginatedRecords_WithStatusAndBucketCounts()
    {
        // Arrange
        _dbContext.OssAuditRecords.AddRange(
            CreateRecord(id: 1, status: 0, bucket: "bucket-a", createdAt: 3000),
            CreateRecord(id: 2, status: 0, bucket: "bucket-b", createdAt: 2000),
            CreateRecord(id: 3, status: 1, bucket: "bucket-a", createdAt: 1000));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.GetRecords(page: 1, pageSize: 10);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.totalCount).Should().Be(3);
        ((int)data.page).Should().Be(1);
        ((int)data.pageSize).Should().Be(10);
        var items = (IEnumerable<object>)data.items;
        items.Count().Should().Be(3);
        var statusCounts = (System.Collections.IDictionary)data.statusCounts;
        statusCounts.Count.Should().BeGreaterThan(0);
        var bucketCounts = (System.Collections.IDictionary)data.bucketCounts;
        bucketCounts.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetRecords_FilterByStatus_ReturnsOnlyMatchingRecords()
    {
        // Arrange
        _dbContext.OssAuditRecords.AddRange(
            CreateRecord(id: 1, status: 0, bucket: "a"),
            CreateRecord(id: 2, status: 1, bucket: "b"),
            CreateRecord(id: 3, status: 2, bucket: "c"));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.GetRecords(status: 0);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.totalCount).Should().Be(1);
        var items = (IEnumerable<object>)data.items;
        items.Count().Should().Be(1);
    }

    [Fact]
    public async Task GetRecords_FilterByBucket_ReturnsOnlyMatchingRecords()
    {
        // Arrange
        _dbContext.OssAuditRecords.AddRange(
            CreateRecord(id: 1, bucket: "bucket-a"),
            CreateRecord(id: 2, bucket: "bucket-b"),
            CreateRecord(id: 3, bucket: "bucket-a"));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.GetRecords(bucket: "bucket-a");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.totalCount).Should().Be(2);
        var items = (IEnumerable<object>)data.items;
        items.Count().Should().Be(2);
    }

    // ============================================================
    // TriggerAudit Tests
    // ============================================================

    [Fact]
    public async Task TriggerAudit_NoRunningAudit_ReturnsOk()
    {
        // Arrange - no running audit runs in DB

        // Act
        var result = await _controller.TriggerAudit();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("审计已触发，请稍候查看结果。");
    }

    [Fact]
    public async Task TriggerAudit_AlreadyRunning_ReturnsBadRequest()
    {
        // Arrange
        _dbContext.OssAuditRuns.Add(CreateRun(id: 1, status: 0, completedAt: null));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.TriggerAudit();

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("审计正在运行中，请等待完成后再触发。");
    }

    // ============================================================
    // GetStatus Tests
    // ============================================================

    [Fact]
    public async Task GetStatus_ReturnsIsRunning_LastCompleted_LastFailed_PendingCount()
    {
        // Arrange
        _dbContext.OssAuditRuns.AddRange(
            CreateRun(id: 1, status: 0, completedAt: null),
            CreateRun(id: 2, status: 1, startedAt: 100, completedAt: 200, newZombieCount: 3, triggerType: "manual"),
            CreateRun(id: 3, status: 2, startedAt: 50, completedAt: 60, errorMessage: "timeout", triggerType: "scheduled"));
        _dbContext.OssAuditRecords.AddRange(
            CreateRecord(id: 1, status: 0),
            CreateRecord(id: 2, status: 0),
            CreateRecord(id: 3, status: 1));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.GetStatus();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((bool)data.isRunning).Should().BeTrue();
        ((int)data.pendingCount).Should().Be(2);
        Assert.NotNull(data.lastCompleted);
        Assert.NotNull(data.lastFailed);
    }

    [Fact]
    public async Task GetStatus_NoRuns_ReturnsDefaults()
    {
        // Arrange - empty DB

        // Act
        var result = await _controller.GetStatus();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((bool)data.isRunning).Should().BeFalse();
        ((int)data.pendingCount).Should().Be(0);
        Assert.Null(data.lastCompleted);
        Assert.Null(data.lastFailed);
    }

    // ============================================================
    // ResolveRecord Tests
    // ============================================================

    [Fact]
    public async Task ResolveRecord_NotFound_Returns404()
    {
        // Act
        var result = await _controller.ResolveRecord(999);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Record not found.");
    }

    [Fact]
    public async Task ResolveRecord_PendingWithNoReferences_DeletesAndRemovesFromDb()
    {
        // Arrange
        var record = CreateRecord(id: 1, status: 0, objectPath: "uploads/orphan.png");
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        SetupStudentPaths(); // no references
        SetupMistakePaths(); // no references
        SetupDeleteOssObject(success: true);

        // Act
        var result = await _controller.ResolveRecord(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();

        _dbContext.OssAuditRecords.Any(r => r.Id == 1).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRecord_ReferencedByStudentService_Returns400()
    {
        // Arrange
        var record = CreateRecord(id: 1, status: 0, objectPath: "uploads/referenced.png");
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        SetupStudentPaths("uploads/referenced.png"); // referenced by student service
        SetupMistakePaths();

        // Act
        var result = await _controller.ResolveRecord(1);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("该文件仍被上传记录引用，不能删除。");
    }

    [Fact]
    public async Task ResolveRecord_ReferencedByMistakeService_Returns400()
    {
        // Arrange
        var record = CreateRecord(id: 1, status: 0, objectPath: "uploads/mistake-ref.png");
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        SetupStudentPaths(); // not referenced by student
        SetupMistakePaths("uploads/mistake-ref.png"); // referenced by mistake service

        // Act
        var result = await _controller.ResolveRecord(1);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("该文件仍被错题记录引用，不能删除。");
    }

    [Fact]
    public async Task ResolveRecord_AlreadyResolved_SkipsReferenceCheck_DeletesDirectly()
    {
        // Arrange
        var record = CreateRecord(id: 1, status: 1, objectPath: "uploads/already-resolved.png", resolvedAt: 5000);
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        SetupDeleteOssObject(success: true);

        // Act
        var result = await _controller.ResolveRecord(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();

        // Verify that reference-check gRPC calls were NOT made
        _studentClient.Verify(
            c => c.GetRegisteredOssPathsAsync(
                It.IsAny<SProto.Empty>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _mistakeClient.Verify(
            c => c.GetAllReferencedImagePathsAsync(
                It.IsAny<MProto.Empty>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _dbContext.OssAuditRecords.Any(r => r.Id == 1).Should().BeFalse();
    }

    // ============================================================
    // IgnoreRecord Tests
    // ============================================================

    [Fact]
    public async Task IgnoreRecord_NotFound_Returns404()
    {
        // Act
        var result = await _controller.IgnoreRecord(999, null);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Record not found.");
    }

    [Fact]
    public async Task IgnoreRecord_PendingRecord_SetsStatus2AndNote()
    {
        // Arrange
        var record = CreateRecord(id: 1, status: 0);
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.IgnoreRecord(1, new IgnoreRecordRequest { Note = "not needed" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("Record ignored.");

        var updated = await _dbContext.OssAuditRecords.FindAsync(1L);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(2);
        updated.Note.Should().Be("not needed");
        updated.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IgnoreRecord_NonPendingRecord_Returns400()
    {
        // Arrange
        var record = CreateRecord(id: 1, status: 1, resolvedAt: 5000);
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _controller.IgnoreRecord(1, new IgnoreRecordRequest { Note = "try ignore" });

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Only pending records can be ignored.");
    }

    // ============================================================
    // BatchResolve Tests
    // ============================================================

    [Fact]
    public async Task BatchResolve_EmptyIds_Returns400()
    {
        // Act
        var result = await _controller.BatchResolve(new BatchResolveRequest { Ids = new List<long>() });

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("At least one record ID is required.");
    }

    [Fact]
    public async Task BatchResolve_NullIds_Returns400()
    {
        // Act
        var result = await _controller.BatchResolve(new BatchResolveRequest { Ids = null! });

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("At least one record ID is required.");
    }

    [Fact]
    public async Task BatchResolve_Success_ResolvesMultipleRecords()
    {
        // Arrange
        _dbContext.OssAuditRecords.AddRange(
            CreateRecord(id: 1, status: 0, objectPath: "uploads/a.png"),
            CreateRecord(id: 2, status: 0, objectPath: "uploads/b.png"));
        await _dbContext.SaveChangesAsync();

        SetupStudentPaths(); // no references
        SetupMistakePaths(); // no references
        SetupDeleteOssObject(success: true);

        // Act
        var result = await _controller.BatchResolve(new BatchResolveRequest { Ids = new List<long> { 1, 2 } });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.resolvedCount).Should().Be(2);
        ((System.Collections.ICollection)data.errors).Count.Should().Be(0);
        ((int)data.totalRequested).Should().Be(2);

        _dbContext.OssAuditRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchResolve_SomeReferenced_SkipsReferencedResolvesOthers()
    {
        // Arrange
        _dbContext.OssAuditRecords.AddRange(
            CreateRecord(id: 1, status: 0, objectPath: "uploads/orphan.png"),
            CreateRecord(id: 2, status: 0, objectPath: "uploads/referenced.png"));
        await _dbContext.SaveChangesAsync();

        SetupStudentPaths("uploads/referenced.png"); // only "referenced.png" is in student service
        SetupMistakePaths(); // no mistake references
        SetupDeleteOssObject(success: true);

        // Act
        var result = await _controller.BatchResolve(new BatchResolveRequest { Ids = new List<long> { 1, 2 } });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        ((int)data.resolvedCount).Should().Be(1);
        ((System.Collections.ICollection)data.errors).Count.Should().Be(1);
        ((int)data.totalRequested).Should().Be(2);

        // Orphan should be removed, referenced should remain
        _dbContext.OssAuditRecords.Any(r => r.Id == 1).Should().BeFalse();
        _dbContext.OssAuditRecords.Any(r => r.Id == 2).Should().BeTrue();
    }
}
