using Admin.WebApi.Controllers;
using Admin.WebApi.Data;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.Common.Oss;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// OssAuditController UT：覆盖 TriggerAudit、GetStatus、ResolveRecord、IgnoreRecord。
/// </summary>
public class OssAuditControllerTests
{
    private readonly AuditDbContext _dbContext;
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _studentClient;
    private readonly Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<IOssService> _ossService;
    private readonly Mock<OssAuditWorker> _auditWorker;
    private readonly Mock<ILogger<OssAuditController>> _logger;
    private readonly OssAuditController _controller;

    public OssAuditControllerTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AuditDbContext(options);

        _studentClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _mistakeClient = new Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _ossService = new Mock<IOssService>();
        _auditWorker = new Mock<OssAuditWorker>(
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<OssAuditWorker>>(),
            Mock.Of<IConfiguration>());
        _logger = new Mock<ILogger<OssAuditController>>();

        _controller = new OssAuditController(
            _dbContext,
            _studentClient.Object,
            _mistakeClient.Object,
            _ossService.Object,
            _auditWorker.Object,
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
    // TriggerAudit
    // ============================================================

    [Fact]
    public async Task TriggerAudit_NoRunningAudit_ReturnsOk()
    {
        // 没有正在运行的审计
        var result = await _controller.TriggerAudit();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        typed.Success.Should().BeTrue();
    }

    [Fact]
    public async Task TriggerAudit_AlreadyRunning_ReturnsBadRequest()
    {
        // 插入一条正在运行的审计记录
        _dbContext.OssAuditRuns.Add(new OssAuditRun
        {
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = 0,
            TriggerType = "manual"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.TriggerAudit();

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
    }

    // ============================================================
    // GetStatus
    // ============================================================

    [Fact]
    public async Task GetStatus_NoRuns_ReturnsNotRunning()
    {
        var result = await _controller.GetStatus();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isRunning")!.GetValue(typed).Should().Be(false);
        typed.GetType().GetProperty("pendingCount")!.GetValue(typed).Should().Be(0);
    }

    [Fact]
    public async Task GetStatus_RunningAudit_ReturnsIsRunning()
    {
        _dbContext.OssAuditRuns.Add(new OssAuditRun
        {
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = 0,
            TriggerType = "manual"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetStatus();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isRunning")!.GetValue(typed).Should().Be(true);
    }

    [Fact]
    public async Task GetStatus_CompletedRun_ReturnsLastCompleted()
    {
        _dbContext.OssAuditRuns.Add(new OssAuditRun
        {
            StartedAt = 1700000000,
            CompletedAt = 1700000060,
            Status = 1,
            NewZombieCount = 5,
            TriggerType = "scheduled"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetStatus();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.GetType().GetProperty("isRunning")!.GetValue(typed).Should().Be(false);
        var lastCompleted = typed.GetType().GetProperty("lastCompleted")!.GetValue(typed);
        lastCompleted.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatus_PendingRecords_ReturnsCorrectPendingCount()
    {
        _dbContext.OssAuditRecords.AddRange(
            new OssAuditRecord { ObjectPath = "a.jpg", Bucket = "uploads", Status = 0, CreatedAt = 1 },
            new OssAuditRecord { ObjectPath = "b.jpg", Bucket = "uploads", Status = 0, CreatedAt = 2 },
            new OssAuditRecord { ObjectPath = "c.jpg", Bucket = "uploads", Status = 1, CreatedAt = 3 }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetStatus();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        ((int)typed.GetType().GetProperty("pendingCount")!.GetValue(typed)!).Should().Be(2);
    }

    // ============================================================
    // ResolveRecord
    // ============================================================

    [Fact]
    public async Task ResolveRecord_NotFound_Returns404()
    {
        var result = await _controller.ResolveRecord(9999);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ResolveRecord_PendingRecordNotReferenced_DeletesAndReturnsOk()
    {
        var record = new OssAuditRecord
        {
            ObjectPath = "uploads/orphan.jpg",
            Bucket = "uploads",
            Size = 1024,
            Status = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        // 设置 gRPC 返回空路径集合（无引用）
        var uploadResponse = new SProto.UploadRecordsPageResult { TotalCount = 0 };
        _studentClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(uploadResponse));

        var mistakeResponse = new MProto.MistakeItemPageResult();
        mistakeResponse.PageMeta = new MProto.PageMeta { TotalCount = 0 };
        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(mistakeResponse));

        _ossService.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);

        var result = await _controller.ResolveRecord(record.Id);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        typed.Success.Should().BeTrue();

        // 记录应被从数据库删除
        _dbContext.OssAuditRecords.Any(r => r.Id == record.Id).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRecord_ReferencedByUploadRecord_ReturnsBadRequest()
    {
        var record = new OssAuditRecord
        {
            ObjectPath = "uploads/referenced.jpg",
            Bucket = "uploads",
            Status = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        // 设置 gRPC 返回包含该路径的上传记录
        var uploadDto = new SProto.UploadRecordDto { Id = "r1" };
        uploadDto.ImagePaths.Add("uploads/referenced.jpg");
        var uploadResponse = new SProto.UploadRecordsPageResult { TotalCount = 1 };
        uploadResponse.Items.Add(uploadDto);
        _studentClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(uploadResponse));

        var result = await _controller.ResolveRecord(record.Id);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResolveRecord_StudentServiceUnavailable_Returns502()
    {
        var record = new OssAuditRecord
        {
            ObjectPath = "uploads/orphan.jpg",
            Bucket = "uploads",
            Status = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        _studentClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _controller.ResolveRecord(record.Id);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(502);
    }

    // ============================================================
    // IgnoreRecord
    // ============================================================

    [Fact]
    public async Task IgnoreRecord_NotFound_Returns404()
    {
        var result = await _controller.IgnoreRecord(9999, null);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task IgnoreRecord_PendingRecord_SetsStatusToIgnored()
    {
        var record = new OssAuditRecord
        {
            ObjectPath = "uploads/zombie.jpg",
            Bucket = "uploads",
            Status = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.IgnoreRecord(record.Id, new IgnoreRecordRequest { Note = "test ignore" });

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        typed.Success.Should().BeTrue();

        // 验证数据库中记录状态已更新
        var updated = await _dbContext.OssAuditRecords.FindAsync(record.Id);
        updated!.Status.Should().Be(2);
        updated.Note.Should().Be("test ignore");
        updated.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IgnoreRecord_AlreadyResolvedRecord_ReturnsBadRequest()
    {
        var record = new OssAuditRecord
        {
            ObjectPath = "uploads/resolved.jpg",
            Bucket = "uploads",
            Status = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ResolvedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _dbContext.OssAuditRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.IgnoreRecord(record.Id, null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
