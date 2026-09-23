using Admin.WebApi.Services;
using Admin.WebApi.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Xunit;

namespace Admin.WebApi.Tests.Services;

public class OssAuditWorkerTests
{
    [Theory]
    [InlineData("uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/reviews/31cd8774-7beb-4824-aab7-03294fb7454e.jpg", true)]
    [InlineData("uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/reviews/31cd8774-7beb-4824-aab7-03294fb7454e_medium.jpg", false)]
    [InlineData("uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/source.jpg", false)]
    [InlineData("uploads/homework/not-a-guid/3bf05868-ed86-4816-b022-a509b2a63965/reviews/31cd8774-7beb-4824-aab7-03294fb7454e.jpg", false)]
    [InlineData("mistakes/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/reviews/31cd8774-7beb-4824-aab7-03294fb7454e.jpg", false)]
    public void IsLegacyHomeworkReviewImagePath_OnlyMatchesObsoleteDerivedOriginals(
        string path,
        bool expected)
    {
        OssAuditWorker.IsLegacyHomeworkReviewImagePath(path).Should().Be(expected);
    }

    [Fact]
    public async Task CleanupLegacyHomeworkReviewImages_DeletesOnlyMatchingObjects()
    {
        const string legacyPath = "uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/reviews/31cd8774-7beb-4824-aab7-03294fb7454e.jpg";
        const string submissionPath = "uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/source.jpg";
        var ossService = new Mock<IOssService>();
        ossService.Setup(service => service.ListObjectsWithBucketAsync(OssBucket.Uploads, "homework"))
            .ReturnsAsync(new List<OssObjectInfo>
            {
                new() { ObjectPath = legacyPath },
                new() { ObjectPath = submissionPath }
            });
        ossService.Setup(service => service.DeleteAsync(legacyPath)).ReturnsAsync(true);

        var deleted = await OssAuditWorker.CleanupLegacyHomeworkReviewImagesAsync(
            ossService.Object,
            Mock.Of<ILogger>());

        deleted.Should().Be(1);
        ossService.Verify(service => service.DeleteAsync(legacyPath), Times.Once);
        ossService.Verify(service => service.DeleteAsync(submissionPath), Times.Never);
    }

    [Fact]
    public async Task CleanupLegacyHomeworkReviewAuditRecords_RemovesOnlyObsoletePaths()
    {
        const string legacyPath = "uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/reviews/31cd8774-7beb-4824-aab7-03294fb7454e.jpg";
        const string submissionPath = "uploads/homework/8fd72e20-e093-43a0-94be-63e29fb516ea/3bf05868-ed86-4816-b022-a509b2a63965/source.jpg";
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AuditDbContext(options);
        dbContext.OssAuditRecords.AddRange(
            new OssAuditRecord { ObjectPath = legacyPath, Bucket = "uploads" },
            new OssAuditRecord { ObjectPath = submissionPath, Bucket = "uploads" });
        await dbContext.SaveChangesAsync();

        var removed = await OssAuditWorker.CleanupLegacyHomeworkReviewAuditRecordsAsync(
            dbContext,
            Mock.Of<ILogger>());

        removed.Should().Be(1);
        (await dbContext.OssAuditRecords.SingleAsync()).ObjectPath.Should().Be(submissionPath);
    }
}
