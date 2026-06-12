using Admin.WebApi.Data;
using Admin.WebApi.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using MProto = Ruoyu.Study.Mistake.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Services;

public class OssAuditWorkerTests : IDisposable
{
    private readonly AuditDbContext _dbContext;
    private readonly Mock<ILogger<OssAuditWorker>> _logger;
    private readonly IConfiguration _configuration;
    private readonly Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient> _studentClient;
    private readonly Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient> _mistakeClient;
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

        _studentClient = new Mock<SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient>();
        _mistakeClient = new Mock<MProto.MistakeGrpcService.MistakeGrpcServiceClient>();

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
            .Setup(sp => sp.GetService(typeof(SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient)))
            .Returns(_studentClient.Object);

        scopedServiceProviderMock
            .Setup(sp => sp.GetService(typeof(MProto.MistakeGrpcService.MistakeGrpcServiceClient)))
            .Returns(_mistakeClient.Object);

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

    private void SetupListOssObjects(SProto.OssBucket bucket, params SProto.OssObjectInfo[] objects)
    {
        var response = new SProto.ListOssObjectsResponse();
        response.Objects.AddRange(objects);
        _studentClient
            .Setup(c => c.ListOssObjectsAsync(
                It.Is<SProto.ListOssObjectsRequest>(r => r.Bucket == bucket),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));
    }

    private static SProto.OssObjectInfo CreateOssObject(string path, long size = 1024, long lastModified = 1000)
    {
        return new SProto.OssObjectInfo { ObjectPath = path, Size = size, LastModified = lastModified };
    }

    // ============================================================
    // Tests
    // ============================================================

    [Fact]
    public async Task RunAuditAsync_Success_FindsZombieObjects()
    {
        // Arrange
        // Student service returns registered paths
        SetupStudentPaths("uploads/registered.png", "mistakes/referenced.png");

        // Mistake service returns referenced paths
        SetupMistakePaths("mistakes/referenced.png", "questions/mistake-ref.png");

        // List objects in each bucket
        SetupListOssObjects(SProto.OssBucket.Uploads,
            CreateOssObject("uploads/registered.png"),
            CreateOssObject("uploads/zombie.png"));

        SetupListOssObjects(SProto.OssBucket.Mistakes,
            CreateOssObject("mistakes/referenced.png"),
            CreateOssObject("mistakes/zombie.png"));

        SetupListOssObjects(SProto.OssBucket.Questions,
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
            .Setup(c => c.GetRegisteredOssPathsAsync(
                It.IsAny<SProto.Empty>(),
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
        SetupStudentPaths("uploads/registered.png");

        _mistakeClient
            .Setup(c => c.GetAllReferencedImagePathsAsync(
                It.IsAny<MProto.Empty>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Mistake service down")));

        // List objects — only mistakes/zombie.png is unreferenced since mistake service is down
        SetupListOssObjects(SProto.OssBucket.Uploads,
            CreateOssObject("uploads/registered.png"));

        SetupListOssObjects(SProto.OssBucket.Mistakes,
            CreateOssObject("mistakes/zombie.png"));

        SetupListOssObjects(SProto.OssBucket.Questions);

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

        SetupStudentPaths();
        SetupMistakePaths();
        SetupListOssObjects(SProto.OssBucket.Uploads);
        SetupListOssObjects(SProto.OssBucket.Mistakes);
        SetupListOssObjects(SProto.OssBucket.Questions);

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
