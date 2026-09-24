using System.Net;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// Guards the invariant that made Storage Audit destructive: a bucket may only be scanned when a
/// reference source aggregates the paths stored under it. An orphan record is not a report — it is
/// one operator click away from a real <see cref="IOssService.DeleteAsync"/> against production
/// storage, so a false positive here loses data.
/// </summary>
public class OssAuditWorkerScopeTests
{
    // ===== Audit scope =====

    [Fact]
    public void AuditedBuckets_ExcludeEveryBucketWithoutAReferenceSource()
    {
        // uploads/ is covered by the Student upload-record sweep, mistakes/ by the Mistake item
        // sweep. questions/ (QuestionBank) and documents/ (DocLibrary via StructaDoc) have no
        // reference source reachable from this service.
        OssAuditWorker.AuditedBuckets.Should().Equal(OssBucket.Uploads, OssBucket.Mistakes);
        OssAuditWorker.AuditedBuckets.Should().NotContain(OssBucket.Questions);
        OssAuditWorker.AuditedBuckets.Should().NotContain(OssBucket.Documents);
    }

    [Fact]
    public void AuditedBucketNames_CoverExactlyTheAuditedBuckets()
    {
        OssAuditWorker.AuditedBucketNames.Keys.Should().BeEquivalentTo(OssAuditWorker.AuditedBuckets);
        OssAuditWorker.AuditedBucketNames[OssBucket.Uploads].Should().Be("uploads");
        OssAuditWorker.AuditedBucketNames[OssBucket.Mistakes].Should().Be("mistakes");
    }

    // ===== Startup purge of records that should never have been created =====

    [Fact]
    public async Task CleanupUnauditedBucketAuditRecords_RemovesRecordsFromUnauditedBuckets()
    {
        await using var dbContext = await CreateDbContextAsync();
        dbContext.OssAuditRecords.AddRange(
            new OssAuditRecord { ObjectPath = "uploads/a.jpg", Bucket = "uploads" },
            new OssAuditRecord { ObjectPath = "mistakes/b.jpg", Bucket = "mistakes" },
            new OssAuditRecord { ObjectPath = "questions/c.jpg", Bucket = "questions" },
            new OssAuditRecord { ObjectPath = "documents/d.pdf", Bucket = "documents" });
        await dbContext.SaveChangesAsync();

        var removed = await OssAuditWorker.CleanupUnauditedBucketAuditRecordsAsync(
            dbContext,
            Mock.Of<ILogger>());

        removed.Should().Be(2);
        (await dbContext.OssAuditRecords.Select(r => r.ObjectPath).ToListAsync())
            .Should().BeEquivalentTo("uploads/a.jpg", "mistakes/b.jpg");
    }

    [Fact]
    public async Task CleanupUnauditedBucketAuditRecords_KeepsRecordsWhoseBucketIsUnrecognised()
    {
        await using var dbContext = await CreateDbContextAsync();
        dbContext.OssAuditRecords.AddRange(
            new OssAuditRecord { ObjectPath = "questions/c.jpg", Bucket = "questions" },
            new OssAuditRecord { ObjectPath = "unknown/e.jpg", Bucket = string.Empty });
        await dbContext.SaveChangesAsync();

        var removed = await OssAuditWorker.CleanupUnauditedBucketAuditRecordsAsync(
            dbContext,
            Mock.Of<ILogger>());

        removed.Should().Be(1);
        (await dbContext.OssAuditRecords.SingleAsync()).ObjectPath.Should().Be("unknown/e.jpg");
    }

    [Fact]
    public async Task CleanupUnauditedBucketAuditRecords_IsIdempotentWhenNothingIsStale()
    {
        await using var dbContext = await CreateDbContextAsync();
        dbContext.OssAuditRecords.Add(new OssAuditRecord { ObjectPath = "uploads/a.jpg", Bucket = "uploads" });
        await dbContext.SaveChangesAsync();

        var removed = await OssAuditWorker.CleanupUnauditedBucketAuditRecordsAsync(
            dbContext,
            Mock.Of<ILogger>());

        removed.Should().Be(0);
        (await dbContext.OssAuditRecords.CountAsync()).Should().Be(1);
    }

    // ===== RunAuditAsync end to end =====

    [Fact]
    public async Task RunAudit_NeverScansTheQuestionsBucket_AndNeverFlagsQuestionImages()
    {
        var questionImage = "questions/math/q1.jpg";
        var fixture = new AuditFixture();
        fixture.Oss.Setup(service => service.ListObjectsWithBucketAsync(OssBucket.Questions, It.IsAny<string?>()))
            .ReturnsAsync(new List<OssObjectInfo> { new() { ObjectPath = questionImage } });
        fixture.Oss.Setup(service => service.ListObjectsWithBucketAsync(OssBucket.Uploads, It.IsAny<string?>()))
            .ReturnsAsync(new List<OssObjectInfo> { new() { ObjectPath = questionImage } });

        await fixture.Worker.RunAuditAsync("manual");

        fixture.Oss.Verify(
            service => service.ListObjectsWithBucketAsync(OssBucket.Questions, It.IsAny<string?>()),
            Times.Never,
            "questions/ has no reference source, so scanning it flags every question image as deletable");
        fixture.Oss.Verify(
            service => service.ListObjectsWithBucketAsync(OssBucket.Documents, It.IsAny<string?>()),
            Times.Never);

        var records = await fixture.Records.ToListAsync();
        records.Should().NotContain(r => r.Bucket == "questions");
        // The same path arriving under an audited bucket is still legitimately flagged.
        records.Should().ContainSingle(r => r.ObjectPath == questionImage && r.Bucket == "uploads");
    }

    [Fact]
    public async Task RunAudit_AbortsWhenMistakeServiceIsUnavailable_InsteadOfFlaggingEveryMistakeImage()
    {
        var mistakeImage = "mistakes/2026/01/m1.jpg";
        var fixture = new AuditFixture();
        // mistakes/ objects were migrated out of uploads/, so the Student sweep does not cover
        // them. Degrading past an unavailable Mistake service therefore flags all of them.
        fixture.Mistake
            .Setup(client => client.GetMistakeItemListAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<MistakeReviewStatus>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        fixture.Oss.Setup(service => service.ListObjectsWithBucketAsync(OssBucket.Mistakes, It.IsAny<string?>()))
            .ReturnsAsync(new List<OssObjectInfo> { new() { ObjectPath = mistakeImage } });

        await fixture.Worker.RunAuditAsync("manual");

        var run = await fixture.Runs.SingleAsync();
        run.Status.Should().Be(2, "an incomplete reference sweep must not produce actionable findings");
        run.ErrorMessage.Should().Be("Mistake service unavailable");
        run.CompletedAt.Should().BeGreaterThan(0);

        (await fixture.Records.CountAsync()).Should().Be(0);
        fixture.Oss.Verify(
            service => service.ListObjectsWithBucketAsync(It.IsAny<OssBucket>(), It.IsAny<string?>()),
            Times.Never,
            "the audit must abort before scanning anything");
    }

    [Fact]
    public async Task RunAudit_AbortsWhenStudentServiceIsUnavailable()
    {
        var fixture = new AuditFixture();
        fixture.Student
            .Setup(client => client.GetAllUploadRecordsAsync(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await fixture.Worker.RunAuditAsync("manual");

        var run = await fixture.Runs.SingleAsync();
        run.Status.Should().Be(2);
        run.ErrorMessage.Should().Be("Student service unavailable");
        (await fixture.Records.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunAudit_AbortsWhenHomeworkServiceIsUnavailable()
    {
        var fixture = new AuditFixture(homeworkResponder: _ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await fixture.Worker.RunAuditAsync("manual");

        var run = await fixture.Runs.SingleAsync();
        run.Status.Should().Be(2);
        run.ErrorMessage.Should().Be("Homework service unavailable");
        (await fixture.Records.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunAudit_FlagsAnObjectNoReferenceSourceCovers_AndSparesReferencedOnes()
    {
        var fixture = new AuditFixture();
        fixture.StudentPaths.Add("uploads/referenced.jpg");
        fixture.MistakePaths.Add("mistakes/referenced.jpg");
        fixture.Oss.Setup(service => service.ListObjectsWithBucketAsync(OssBucket.Uploads, It.IsAny<string?>()))
            .ReturnsAsync(new List<OssObjectInfo>
            {
                new() { ObjectPath = "uploads/referenced.jpg" },
                new() { ObjectPath = "uploads/orphan.jpg", Size = 42 },
                // Derived thumbnails are skipped: deleting the original cleans them up too.
                new() { ObjectPath = "uploads/orphan_thumbnail.jpg" },
            });

        await fixture.Worker.RunAuditAsync("manual");

        var run = await fixture.Runs.SingleAsync();
        run.Status.Should().Be(1);
        run.NewZombieCount.Should().Be(1);

        var record = await fixture.Records.SingleAsync();
        record.ObjectPath.Should().Be("uploads/orphan.jpg");
        record.Bucket.Should().Be("uploads");
        record.Size.Should().Be(42);
    }

    // ===== Fixture =====

    /// <summary>
    /// Wires <see cref="OssAuditWorker"/> against an in-memory audit database and mocked
    /// downstreams. <see cref="StudentPaths"/>, <see cref="MistakePaths"/> and
    /// <see cref="HomeworkPaths"/> must be populated before <c>RunAuditAsync</c> is called.
    /// </summary>
    private sealed class AuditFixture
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();

        public AuditFixture(Func<HttpRequestMessage, HttpResponseMessage>? homeworkResponder = null)
        {
            var options = new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), _databaseRoot)
                .Options;
            Options = options;

            Oss = new Mock<IOssService>();
            Oss.Setup(service => service.ListObjectsWithBucketAsync(It.IsAny<OssBucket>(), It.IsAny<string?>()))
                .ReturnsAsync(new List<OssObjectInfo>());

            Student = new Mock<IStudentHttpClient>();
            Student.Setup(client => client.GetAllUploadRecordsAsync(
                    It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new UploadRecordsPage
                {
                    Items = StudentPaths
                        .Select(path => new UploadRecordDto
                        {
                            ImageEntries = { new ImageEntryDto { Path = path } }
                        })
                        .ToList(),
                    TotalCount = 1,
                });

            Mistake = new Mock<IMistakeHttpClient>();
            Mistake.Setup(client => client.GetMistakeItemListAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<MistakeReviewStatus>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MistakeItemPageResult
                {
                    Items = MistakePaths
                        .Select(path => new MistakeItemDto
                        {
                            SourceRegions = { new MistakeSourceRegionDto { SourceImagePath = path } }
                        })
                        .ToList(),
                    PageMeta = new MistakePageMetaDto { TotalCount = 0 },
                });

            var responder = homeworkResponder
                ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"success":true,"data":{"paths":[],"hasMore":false}}""",
                        System.Text.Encoding.UTF8,
                        "application/json"),
                });
            var homeworkClient = new HomeworkReferenceClient(
                new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("http://homework.test") });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => new AuditDbContext(options));
            services.AddSingleton(Oss.Object);
            services.AddSingleton(Student.Object);
            services.AddSingleton(Mistake.Object);
            services.AddSingleton(homeworkClient);
            Provider = services.BuildServiceProvider();

            Worker = new OssAuditWorker(
                Provider,
                Mock.Of<ILogger<OssAuditWorker>>(),
                new ConfigurationBuilder().Build());
        }

        public DbContextOptions<AuditDbContext> Options { get; }
        public ServiceProvider Provider { get; }
        public OssAuditWorker Worker { get; }
        public Mock<IOssService> Oss { get; }
        public Mock<IStudentHttpClient> Student { get; }
        public Mock<IMistakeHttpClient> Mistake { get; }

        public List<string> StudentPaths { get; } = new();
        public List<string> MistakePaths { get; } = new();

        /// <summary>Opens a context over the same in-memory store the worker writes to.</summary>
        public AuditDbContext OpenContext() => new(Options);

        public DbSet<OssAuditRecord> Records => OpenContext().OssAuditRecords;
        public DbSet<OssAuditRun> Runs => OpenContext().OssAuditRuns;

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => Task.FromResult(_responder(request));
        }
    }

    private static async Task<AuditDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        var context = new AuditDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
