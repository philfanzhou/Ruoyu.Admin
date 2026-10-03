using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Xunit;
using System.Text.Json;
using Ruoyu.Admin.ServiceClients.StorageReferences;

namespace Admin.WebApi.Tests.Services;

public sealed class OssAuditWorkerScopeTests
{
    [Fact]
    public void AuditedBuckets_ExcludeEveryBucketWithoutAReferenceSource()
    {
        Assert.Equal(new[] { OssBucket.Uploads, OssBucket.Mistakes }, OssAuditWorker.AuditedBuckets);
        Assert.Equal("uploads", OssAuditWorker.AuditedBucketNames[OssBucket.Uploads]);
        Assert.Equal("mistakes", OssAuditWorker.AuditedBucketNames[OssBucket.Mistakes]);
    }

    [Fact]
    public async Task Startup_PreservesAllHistoricalAuditRecordsAsync()
    {
        using var fixture = new Fixture();
        await using var context = fixture.Context();
        context.OssAuditRecords.AddRange(new OssAuditRecord { ObjectPath="questions/a",Bucket="questions" },
            new OssAuditRecord { ObjectPath="documents/b",Bucket="documents" },new OssAuditRecord { ObjectPath="uploads/c",Bucket="uploads" });
        await context.SaveChangesAsync();
        Assert.Equal(0, await OssAuditWorker.CleanupUnauditedBucketAuditRecordsAsync(context, Mock.Of<ILogger>()));
        Assert.Equal(0, await OssAuditWorker.CleanupLegacyHomeworkReviewAuditRecordsAsync(context, Mock.Of<ILogger>()));
        Assert.Equal(3, await context.OssAuditRecords.CountAsync());
    }

    [Fact]
    public async Task CompleteCollection_ReportsOnlyUnreferencedObjectsWithExactCaseAsync()
    {
        using var fixture = new Fixture();
        fixture.References.Setup(collector => collector.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageReferenceCollection(new HashSet<string>(["uploads/A.jpg","mistakes/pinned.jpg"],StringComparer.Ordinal),EmptyProofs()));
        fixture.Oss.Setup(oss => oss.ListObjectsWithBucketAsync(OssBucket.Uploads,It.IsAny<string?>())).ReturnsAsync([
            new() { ObjectPath="uploads/A.jpg",Size=1 },new() { ObjectPath="uploads/a.jpg",Size=2 },
            new() { ObjectPath="uploads/unused.jpg",Size=3 },new() { ObjectPath="uploads/A_small.jpg",Size=4 }]);
        fixture.Oss.Setup(oss => oss.ListObjectsWithBucketAsync(OssBucket.Mistakes,It.IsAny<string?>())).ReturnsAsync([
            new() { ObjectPath="mistakes/pinned.jpg",Size=5 }]);
        await fixture.Worker.RunAuditAsync("manual");
        await using var context = fixture.Context();
        var run = await context.OssAuditRuns.SingleAsync();
        Assert.Equal(1,run.Status); Assert.Equal(2,run.NewZombieCount);
        Assert.Equal("storage-references-v1",run.ReferenceContractVersion); using var proofs = JsonDocument.Parse(run.ReferenceSnapshots!);
        Assert.Equal(new[] { "student", "mistake", "homework" }, proofs.RootElement.EnumerateArray().Select(proof => proof.GetProperty("provider").GetString()));
        var records = await context.OssAuditRecords.OrderBy(record=>record.ObjectPath).ToListAsync();
        Assert.Equal(new[] {"uploads/a.jpg","uploads/unused.jpg"},records.Select(record=>record.ObjectPath));
        Assert.All(records,record=>Assert.Equal(3,record.Status));
        fixture.Oss.Verify(oss=>oss.ListObjectsWithBucketAsync(OssBucket.Questions,It.IsAny<string?>()),Times.Never);
        fixture.Oss.Verify(oss=>oss.ListObjectsWithBucketAsync(OssBucket.Documents,It.IsAny<string?>()),Times.Never);
        fixture.Oss.Verify(oss=>oss.DeleteAsync(It.IsAny<string>()),Times.Never);
    }

    [Theory]
    [InlineData("student")] [InlineData("mistake")] [InlineData("homework")]
    public async Task IncompleteCollection_FailsBeforeListingAnyBucketAsync(string provider)
    {
        using var fixture = new Fixture();
        fixture.References.Setup(collector=>collector.CollectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Injected "+provider+" failure."));
        await fixture.Worker.RunAuditAsync("manual");
        await using var context=fixture.Context();
        var run=await context.OssAuditRuns.SingleAsync();
        Assert.Equal(2,run.Status); Assert.Equal("references_or_scan_unavailable",run.ErrorMessage);
        Assert.Empty(await context.OssAuditRecords.ToListAsync());
        fixture.Oss.Verify(oss=>oss.ListObjectsWithBucketAsync(It.IsAny<OssBucket>(),It.IsAny<string?>()),Times.Never);
        fixture.Oss.Verify(oss=>oss.DeleteAsync(It.IsAny<string>()),Times.Never);
    }

    [Fact]
    public async Task S3Unavailable_IsFailedObservationWithoutDeleteOrCopyAsync()
    {
        using var fixture = new Fixture();
        fixture.Oss.Setup(oss => oss.ListObjectsWithBucketAsync(OssBucket.Uploads, It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("Injected S3 outage."));
        await fixture.Worker.RunAuditAsync("manual");
        await using var context = fixture.Context();
        Assert.Equal(2, (await context.OssAuditRuns.SingleAsync()).Status);
        Assert.Empty(await context.OssAuditRecords.ToListAsync());
        fixture.Oss.Verify(oss => oss.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancellationAfterCollection_PersistsTerminalFailureBeforeAnyListAsync()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.References.Setup(collector => collector.CollectAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { cancellation.Cancel(); return Task.FromResult(new StorageReferenceCollection(new HashSet<string>(), EmptyProofs())); });
        await fixture.Worker.RunAuditAsync("manual", cancellation.Token);
        await using var context = fixture.Context();
        var run = await context.OssAuditRuns.SingleAsync();
        Assert.Equal(2, run.Status); Assert.Equal("audit_cancelled", run.ErrorMessage);
        fixture.Oss.Verify(oss => oss.ListObjectsWithBucketAsync(It.IsAny<OssBucket>(), It.IsAny<string?>()), Times.Never);
        fixture.Oss.Verify(oss => oss.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    private static List<StorageReferenceMetadata> EmptyProofs() => new[] { "student", "mistake", "homework" }
        .Select(provider => new StorageReferenceMetadata(StorageReferenceContract.Version, provider, Guid.NewGuid(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), StorageReferenceContract.Coverage(provider),
            0, 500, 1, StorageReferenceContract.Digest(provider, []), false)).ToList();

    private sealed class Fixture : IDisposable
    {
        private readonly DbContextOptions<AuditDbContext> _options;
        private readonly ServiceProvider _provider;
        public Mock<IOssService> Oss { get; } = new();
        public Mock<IStorageReferenceCollector> References { get; } = new();
        public OssAuditWorker Worker { get; }
        public Fixture()
        {
            _options=new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString(),new InMemoryDatabaseRoot())
                .ConfigureWarnings(w=>w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning,InMemoryEventId.TransactionIgnoredWarning)).Options;
            Oss.Setup(oss=>oss.ListObjectsWithBucketAsync(It.IsAny<OssBucket>(),It.IsAny<string?>())).ReturnsAsync([]);
            References.Setup(collector=>collector.CollectAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StorageReferenceCollection(new HashSet<string>(StringComparer.Ordinal),EmptyProofs()));
            var services=new ServiceCollection(); services.AddLogging();services.AddScoped(_=>Context());
            services.AddSingleton(Oss.Object);services.AddSingleton(References.Object);_provider=services.BuildServiceProvider();
            Worker=new OssAuditWorker(_provider,Mock.Of<ILogger<OssAuditWorker>>(),new ConfigurationBuilder().Build());
        }
        public AuditDbContext Context()=>new(_options);
        public void Dispose()=>_provider.Dispose();
    }
}
