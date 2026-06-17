using Admin.WebApi.Data;
using Admin.WebApi.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.Common.Oss;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Services;

public class OssAuditWorkerTests : IDisposable
{
    private readonly AuditDbContext _dbContext;
    private readonly Mock<ILogger<OssAuditWorker>> _logger;
    private readonly IConfiguration _configuration;
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _studentClient;
    private readonly Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
    private readonly Mock<IOssService> _ossService;
    private readonly Mock<IServiceProvider> _serviceProvider;
    private readonly OssAuditWorker _worker;

    public OssAuditWorkerTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AuditDbContext(options);

        _logger = new Mock<ILogger<OssAuditWorker>>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "OssAudit:ScheduledHour", "2" },
                { "OssAudit:ScheduledMinute", "0" },
            })
            .Build();

        _studentClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _mistakeClient = new Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient>();
        _ossService = new Mock<IOssService>();

        _serviceProvider = BuildServiceProvider();

        _worker = new OssAuditWorker(_serviceProvider.Object, _logger.Object, _configuration);
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

    private Mock<IServiceProvider> BuildServiceProvider()
    {
        var scopedServiceProviderMock = new Mock<IServiceProvider>();

        scopedServiceProviderMock
            .Setup(sp => sp.GetService(typeof(AuditDbContext)))
            .Returns(_dbContext);

        scopedServiceProviderMock
            .Setup(sp => sp.GetService(typeof(SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient)))
            .Returns(_studentClient.Object);

        scopedServiceProviderMock
            .Setup(sp => sp.GetService(typeof(MProto.MistakeGrpcService.MistakeGrpcServiceClient)))
            .Returns(_mistakeClient.Object);

        scopedServiceProviderMock
            .Setup(sp => sp.GetService(typeof(IOssService)))
            .Returns(_ossService.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(scopedServiceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);

        return serviceProviderMock;
    }

    /// <summary>
    /// 模拟 GetAllUploadRecords 分页返回包含指定 image_paths 的上传记录。
    /// </summary>
    private void SetupStudentUploadRecords(params string[] imagePaths)
    {
        var record = new SProto.UploadRecordDto();
        record.ImagePaths.AddRange(imagePaths);

        var response = new SProto.UploadRecordsPageResult
        {
            TotalCount = imagePaths.Length > 0 ? 1 : 0,
            Page = 1,
            PageSize = 100,
        };
        if (imagePaths.Length > 0)
            response.Items.Add(record);

        _studentClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    /// <summary>
    /// 模拟 GetMistakeItemList 分页返回包含指定 source_image_path 的错题条目。
    /// </summary>
    private void SetupMistakeItems(params string[] imagePaths)
    {
        var response = new MProto.MistakeItemPageResult
        {
            PageMeta = new MProto.PageMeta
            {
                Page = 1,
                Size = 100,
                TotalCount = imagePaths.Length,
                TotalPages = imagePaths.Length > 0 ? 1 : 0,
            }
        };

        foreach (var path in imagePaths)
        {
            var item = new MProto.MistakeItemDto();
            item.SourceRegions.Add(new MProto.SourceRegion { SourceImagePath = path });
            response.Items.Add(item);
        }

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    /// <summary>
    /// 模拟 IOssService.ListObjectsWithBucketAsync 返回指定 bucket 的对象列表。
    /// </summary>
    private void SetupOssListObjects(OssBucket bucket, params OssObjectInfo[] objects)
    {
        _ossService
            .Setup(s => s.ListObjectsWithBucketAsync(bucket, It.IsAny<string?>()))
            .ReturnsAsync(objects.ToList());
    }

    private static OssObjectInfo CreateOssObject(string path, long size = 1024, DateTimeOffset? lastModified = null)
    {
        return new OssObjectInfo
        {
            ObjectPath = path,
            Size = size,
            LastModified = lastModified ?? DateTimeOffset.FromUnixTimeSeconds(1000),
        };
    }

    // ============================================================
    // Tests
    // ============================================================

    [Fact]
    public async Task RunAuditAsync_Success_FindsZombieObjects()
    {
        // Arrange
        // Student service returns registered paths via upload records
        SetupStudentUploadRecords("uploads/registered.png", "mistakes/referenced.png");

        // Mistake service returns referenced paths via mistake items
        SetupMistakeItems("mistakes/referenced.png", "questions/mistake-ref.png");

        // List objects in each bucket via IOssService
        SetupOssListObjects(OssBucket.Uploads,
            CreateOssObject("uploads/registered.png"),
            CreateOssObject("uploads/zombie.png"));

        SetupOssListObjects(OssBucket.Mistakes,
            CreateOssObject("mistakes/referenced.png"),
            CreateOssObject("mistakes/zombie.png"));

        SetupOssListObjects(OssBucket.Questions,
            CreateOssObject("questions/mistake-ref.png"),
            CreateOssObject("questions/zombie.png"));

        // Act
        await _worker.RunAuditAsync("manual");

        // Assert
        var auditRun = await _dbContext.OssAuditRuns.FirstOrDefaultAsync();
        auditRun.Should().NotBeNull();
        auditRun!.Status.Should().Be(1); // Completed
        auditRun.NewZombieCount.Should().Be(3); // uploads/zombie, mistakes/zombie, questions/zombie
        auditRun.CompletedAt.Should().NotBeNull();

        var records = await _dbContext.OssAuditRecords.ToListAsync();
        records.Should().HaveCount(3);
        records.Select(r => r.ObjectPath).Should().BeEquivalentTo(
            "uploads/zombie.png", "mistakes/zombie.png", "questions/zombie.png");
        records.All(r => r.Status == 0).Should().BeTrue();
    }

    [Fact]
    public async Task RunAuditAsync_StudentServiceUnavailable_MarksFailed()
    {
        // Arrange
        _studentClient
            .Setup(c => c.GetAllUploadRecordsAsync(
                It.IsAny<SProto.GetAllUploadRecordsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        // Act
        await _worker.RunAuditAsync("manual");

        // Assert
        var auditRun = await _dbContext.OssAuditRuns.FirstOrDefaultAsync();
        auditRun.Should().NotBeNull();
        auditRun!.Status.Should().Be(2); // Failed
        auditRun.ErrorMessage.Should().Be("Student service unavailable");
        auditRun.CompletedAt.Should().NotBeNull();

        var records = await _dbContext.OssAuditRecords.ToListAsync();
        records.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAuditAsync_MistakeServiceUnavailable_ContinuesWithWarning()
    {
        // Arrange
        SetupStudentUploadRecords("uploads/registered.png");

        _mistakeClient
            .Setup(c => c.GetMistakeItemListAsync(
                It.IsAny<MProto.GetMistakeItemListRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Mistake service down")));

        // List objects — only mistakes/zombie.png is unreferenced since mistake service is down
        SetupOssListObjects(OssBucket.Uploads,
            CreateOssObject("uploads/registered.png"));

        SetupOssListObjects(OssBucket.Mistakes,
            CreateOssObject("mistakes/zombie.png"));

        SetupOssListObjects(OssBucket.Questions);

        // Act
        await _worker.RunAuditAsync("manual");

        // Assert — audit should complete successfully
        var auditRun = await _dbContext.OssAuditRuns.FirstOrDefaultAsync();
        auditRun.Should().NotBeNull();
        auditRun!.Status.Should().Be(1); // Completed
        auditRun.NewZombieCount.Should().Be(1); // mistakes/zombie.png

        var records = await _dbContext.OssAuditRecords.ToListAsync();
        records.Should().HaveCount(1);
        records[0].ObjectPath.Should().Be("mistakes/zombie.png");
    }

    [Fact]
    public async Task RunAuditAsync_ConcurrentExecution_SecondRunAborts()
    {
        // Arrange — insert a running audit run to simulate concurrent execution
        _dbContext.OssAuditRuns.Add(new OssAuditRun
        {
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = 0, // Running
            TriggerType = "scheduled",
        });
        await _dbContext.SaveChangesAsync();

        SetupStudentUploadRecords();
        SetupMistakeItems();
        SetupOssListObjects(OssBucket.Uploads);
        SetupOssListObjects(OssBucket.Mistakes);
        SetupOssListObjects(OssBucket.Questions);

        // Act
        await _worker.RunAuditAsync("manual");

        // Assert — the new audit should have detected the other running audit and aborted
        var runs = await _dbContext.OssAuditRuns.ToListAsync();
        // The original running run should still be there
        runs.Should().HaveCount(1);
        runs[0].Status.Should().Be(0); // Still running (the original one)

        // No audit records should have been created
        var records = await _dbContext.OssAuditRecords.ToListAsync();
        records.Should().BeEmpty();
    }
}
