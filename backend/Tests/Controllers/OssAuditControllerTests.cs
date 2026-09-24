using System.Net;
using System.Text.Json;
using Admin.WebApi.Controllers;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// Covers the pre-delete reference revalidation in <see cref="OssAuditController"/>.
///
/// This is the only runtime guard between an audit finding and a permanent
/// <see cref="IOssService.DeleteAsync"/> against production storage, so the safety
/// properties are asserted explicitly: a referenced object is never deleted, an
/// unreachable reference source blocks the delete instead of being treated as
/// "no references found", and a failed delete leaves the record in place.
/// </summary>
public class OssAuditControllerTests
{
    // ===== ResolveRecord: lookup =====

    [Fact]
    public async Task ResolveRecord_NotFound_Returns404AndDoesNotDelete()
    {
        var f = new Fixture();

        var result = await f.Controller.ResolveRecord(999);

        var error = Assert.IsType<NotFoundObjectResult>(result);
        ReadJson(error.Value).GetProperty("Message").GetString().Should().Be("Record not found.");
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    // ===== ResolveRecord: the guard must block deletion =====

    [Fact]
    public async Task ResolveRecord_ReferencedByStudentUpload_Returns400AndDoesNotDelete()
    {
        var f = new Fixture();
        f.StudentPaths.Add("uploads/a.jpg");
        var id = await f.SeedRecordAsync("uploads/a.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertBadRequest(result, "该文件仍被上传记录引用，不能删除。");
        await f.AssertNotDeletedAsync(id, "uploads/a.jpg");
    }

    [Fact]
    public async Task ResolveRecord_ReferencedByHomework_Returns400AndDoesNotDelete()
    {
        var f = new Fixture();
        f.HomeworkPaths.Add("uploads/homework/x.jpg");
        var id = await f.SeedRecordAsync("uploads/homework/x.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertBadRequest(result, "该文件仍被上传记录引用，不能删除。");
        await f.AssertNotDeletedAsync(id, "uploads/homework/x.jpg");
    }

    [Fact]
    public async Task ResolveRecord_ReferencedByMistake_Returns400AndDoesNotDelete()
    {
        var f = new Fixture();
        f.MistakePaths.Add("mistakes/m.jpg");
        var id = await f.SeedRecordAsync("mistakes/m.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertBadRequest(result, "该文件仍被错题记录引用，不能删除。");
        await f.AssertNotDeletedAsync(id, "mistakes/m.jpg");
    }

    [Fact]
    public async Task ResolveRecord_ReferenceMatchIsCaseInsensitive()
    {
        // The aggregation uses StringComparer.OrdinalIgnoreCase; a case-only difference
        // must still block the delete, or the guard leaks on case-variant paths.
        var f = new Fixture();
        f.StudentPaths.Add("uploads/ABC.JPG");
        var id = await f.SeedRecordAsync("uploads/abc.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertBadRequest(result, "该文件仍被上传记录引用，不能删除。");
        await f.AssertNotDeletedAsync(id, "uploads/abc.jpg");
    }

    [Fact]
    public async Task ResolveRecord_StudentUnavailable_Returns502AndDoesNotDelete()
    {
        var f = new Fixture();
        f.StudentThrows = new HttpRequestException("connection refused");
        var id = await f.SeedRecordAsync("uploads/a.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertStatus(result, 502, "Student 或 Homework 服务不可用，无法安全删除。");
        await f.AssertNotDeletedAsync(id, "uploads/a.jpg");
    }

    [Fact]
    public async Task ResolveRecord_HomeworkUnavailable_Returns502AndDoesNotDelete()
    {
        // HomeworkReferenceClient is aggregated inside the same try as Student, so its
        // failure must surface as the same refusal rather than a partial reference set.
        var f = new Fixture(homeworkStatus: HttpStatusCode.ServiceUnavailable);
        var id = await f.SeedRecordAsync("uploads/a.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertStatus(result, 502, "Student 或 Homework 服务不可用，无法安全删除。");
        await f.AssertNotDeletedAsync(id, "uploads/a.jpg");
    }

    [Fact]
    public async Task ResolveRecord_MistakeUnavailable_Returns502AndDoesNotDelete()
    {
        var f = new Fixture();
        f.MistakeThrows = new HttpRequestException("connection refused");
        var id = await f.SeedRecordAsync("mistakes/m.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertStatus(result, 502, "Mistake 服务不可用，无法安全删除。");
        await f.AssertNotDeletedAsync(id, "mistakes/m.jpg");
    }

    [Fact]
    public async Task ResolveRecord_NoHomeworkClientConfigured_StillValidatesStudentAndMistake()
    {
        // _homeworkClient is optional in the constructor; a null client must narrow the
        // reference set, not disable validation.
        var f = new Fixture(withHomeworkClient: false);
        f.StudentPaths.Add("uploads/a.jpg");
        var id = await f.SeedRecordAsync("uploads/a.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertBadRequest(result, "该文件仍被上传记录引用，不能删除。");
        await f.AssertNotDeletedAsync(id, "uploads/a.jpg");
    }

    // ===== ResolveRecord: the delete path =====

    [Fact]
    public async Task ResolveRecord_Unreferenced_DeletesObjectAndRemovesRecord()
    {
        var f = new Fixture();
        f.Oss.Setup(s => s.DeleteAsync("uploads/orphan.jpg")).ReturnsAsync(true);
        var id = await f.SeedRecordAsync("uploads/orphan.jpg");

        var result = await f.Controller.ResolveRecord(id);

        var ok = Assert.IsType<OkObjectResult>(result);
        ReadJson(ok.Value).GetProperty("Success").GetBoolean().Should().BeTrue();
        f.Oss.Verify(s => s.DeleteAsync("uploads/orphan.jpg"), Times.Once);
        (await f.Db.OssAuditRecords.AnyAsync(r => r.Id == id)).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRecord_NonPendingRecord_SkipsRevalidationAndDeletes()
    {
        // A record that is not Pending is treated as already-deleted and is not revalidated.
        var f = new Fixture();
        f.StudentPaths.Add("uploads/resolved.jpg");
        f.Oss.Setup(s => s.DeleteAsync("uploads/resolved.jpg")).ReturnsAsync(true);
        var id = await f.SeedRecordAsync("uploads/resolved.jpg", status: 1);

        var result = await f.Controller.ResolveRecord(id);

        Assert.IsType<OkObjectResult>(result);
        f.Oss.Verify(s => s.DeleteAsync("uploads/resolved.jpg"), Times.Once);
        f.Student.Verify(
            s => s.GetAllUploadRecordsAsync(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "non-pending records must not trigger a reference sweep");
    }

    [Fact]
    public async Task ResolveRecord_DeleteThrows_Returns500AndKeepsRecord()
    {
        var f = new Fixture();
        f.Oss.Setup(s => s.DeleteAsync("uploads/orphan.jpg"))
            .ThrowsAsync(new InvalidOperationException("s3 rejected"));
        var id = await f.SeedRecordAsync("uploads/orphan.jpg");

        var result = await f.Controller.ResolveRecord(id);

        AssertStatus(result, 500, null);
        (await f.Db.OssAuditRecords.AnyAsync(r => r.Id == id))
            .Should().BeTrue("a failed delete must not drop the audit trail");
    }

    // ===== IgnoreRecord =====

    [Fact]
    public async Task IgnoreRecord_NotFound_Returns404()
    {
        var f = new Fixture();

        var result = await f.Controller.IgnoreRecord(999, new IgnoreRecordRequest { Note = "x" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task IgnoreRecord_NonPending_Returns400()
    {
        var f = new Fixture();
        var id = await f.SeedRecordAsync("uploads/a.jpg", status: 2);

        var result = await f.Controller.IgnoreRecord(id, new IgnoreRecordRequest { Note = "x" });

        AssertBadRequest(result, "Only pending records can be ignored.");
        (await f.Db.OssAuditRecords.SingleAsync(r => r.Id == id)).Status.Should().Be(2);
    }

    [Fact]
    public async Task IgnoreRecord_Pending_SetsIgnoredStatusAndNoteWithoutDeleting()
    {
        var f = new Fixture();
        var id = await f.SeedRecordAsync("uploads/keep.jpg");

        var result = await f.Controller.IgnoreRecord(id, new IgnoreRecordRequest { Note = "still in use" });

        Assert.IsType<OkObjectResult>(result);
        var record = await f.Db.OssAuditRecords.SingleAsync(r => r.Id == id);
        record.Status.Should().Be(2);
        record.Note.Should().Be("still in use");
        record.ResolvedAt.Should().NotBeNull();
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IgnoreRecord_NullBody_ClearsNote()
    {
        var f = new Fixture();
        var id = await f.SeedRecordAsync("uploads/keep.jpg");

        var result = await f.Controller.IgnoreRecord(id, null);

        Assert.IsType<OkObjectResult>(result);
        (await f.Db.OssAuditRecords.SingleAsync(r => r.Id == id)).Note.Should().BeNull();
    }

    // ===== BatchResolve =====

    [Fact]
    public async Task BatchResolve_EmptyIds_Returns400()
    {
        var f = new Fixture();

        var result = await f.Controller.BatchResolve(new BatchResolveRequest { Ids = new List<long>() });

        AssertBadRequest(result, "At least one record ID is required.");
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BatchResolve_NullIds_Returns400()
    {
        var f = new Fixture();

        var result = await f.Controller.BatchResolve(new BatchResolveRequest { Ids = null! });

        AssertBadRequest(result, "At least one record ID is required.");
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BatchResolve_NoMatchingRecords_ReturnsZeroResolved()
    {
        var f = new Fixture();

        var result = await f.Controller.BatchResolve(new BatchResolveRequest { Ids = new List<long> { 999 } });

        var json = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
        json.GetProperty("resolvedCount").GetInt32().Should().Be(0);
        json.GetProperty("totalRequested").GetInt32().Should().Be(1);
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BatchResolve_ExcludesIgnoredRecords()
    {
        var f = new Fixture();
        f.Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        var pendingId = await f.SeedRecordAsync("uploads/orphan.jpg");
        var ignoredId = await f.SeedRecordAsync("uploads/ignored.jpg", status: 2);

        var result = await f.Controller.BatchResolve(
            new BatchResolveRequest { Ids = new List<long> { pendingId, ignoredId } });

        var json = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
        json.GetProperty("resolvedCount").GetInt32().Should().Be(1);
        f.Oss.Verify(s => s.DeleteAsync("uploads/orphan.jpg"), Times.Once);
        f.Oss.Verify(s => s.DeleteAsync("uploads/ignored.jpg"), Times.Never);
        (await f.Db.OssAuditRecords.AnyAsync(r => r.Id == ignoredId))
            .Should().BeTrue("ignored records are kept for traceability");
    }

    [Fact]
    public async Task BatchResolve_SomeReferenced_SkipsThemAndResolvesTheRest()
    {
        var f = new Fixture();
        f.StudentPaths.Add("uploads/referenced.jpg");
        f.MistakePaths.Add("mistakes/referenced.jpg");
        f.Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        var byStudent = await f.SeedRecordAsync("uploads/referenced.jpg");
        var byMistake = await f.SeedRecordAsync("mistakes/referenced.jpg");
        var orphan = await f.SeedRecordAsync("uploads/orphan.jpg");

        var result = await f.Controller.BatchResolve(
            new BatchResolveRequest { Ids = new List<long> { byStudent, byMistake, orphan } });

        var json = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
        json.GetProperty("resolvedCount").GetInt32().Should().Be(1);
        json.GetProperty("totalRequested").GetInt32().Should().Be(3);
        var errors = json.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e!.Contains("uploads/referenced.jpg") && e.Contains("被上传记录引用"));
        errors.Should().Contain(e => e!.Contains("mistakes/referenced.jpg") && e.Contains("被错题记录引用"));

        f.Oss.Verify(s => s.DeleteAsync("uploads/orphan.jpg"), Times.Once);
        f.Oss.Verify(s => s.DeleteAsync("uploads/referenced.jpg"), Times.Never);
        f.Oss.Verify(s => s.DeleteAsync("mistakes/referenced.jpg"), Times.Never);
        (await f.Db.OssAuditRecords.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BatchResolve_StudentUnavailable_Returns502AndDeletesNothing()
    {
        var f = new Fixture();
        f.StudentThrows = new HttpRequestException("connection refused");
        f.Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        var a = await f.SeedRecordAsync("uploads/a.jpg");
        var b = await f.SeedRecordAsync("uploads/b.jpg");

        var result = await f.Controller.BatchResolve(new BatchResolveRequest { Ids = new List<long> { a, b } });

        AssertStatus(result, 502, "Student 或 Homework 服务不可用，无法安全删除。");
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        (await f.Db.OssAuditRecords.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BatchResolve_MistakeUnavailable_Returns502AndDeletesNothing()
    {
        var f = new Fixture();
        f.MistakeThrows = new HttpRequestException("connection refused");
        f.Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        var a = await f.SeedRecordAsync("uploads/a.jpg");

        var result = await f.Controller.BatchResolve(new BatchResolveRequest { Ids = new List<long> { a } });

        AssertStatus(result, 502, "Mistake 服务不可用，无法安全删除。");
        f.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        (await f.Db.OssAuditRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BatchResolve_OnlyNonPendingRecords_SkipsTheReferenceSweepEntirely()
    {
        var f = new Fixture();
        f.Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        var id = await f.SeedRecordAsync("uploads/resolved.jpg", status: 1);

        var result = await f.Controller.BatchResolve(new BatchResolveRequest { Ids = new List<long> { id } });

        ReadJson(Assert.IsType<OkObjectResult>(result).Value).GetProperty("resolvedCount").GetInt32().Should().Be(1);
        f.Student.Verify(
            s => s.GetAllUploadRecordsAsync(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        f.Mistake.Verify(
            s => s.GetMistakeItemListAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<MistakeReviewStatus>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BatchResolve_OneDeleteFails_ReportsErrorAndContinuesWithTheOthers()
    {
        var f = new Fixture();
        f.Oss.Setup(s => s.DeleteAsync("uploads/bad.jpg"))
            .ThrowsAsync(new InvalidOperationException("s3 rejected"));
        f.Oss.Setup(s => s.DeleteAsync("uploads/good.jpg")).ReturnsAsync(true);
        var bad = await f.SeedRecordAsync("uploads/bad.jpg");
        var good = await f.SeedRecordAsync("uploads/good.jpg");

        var result = await f.Controller.BatchResolve(
            new BatchResolveRequest { Ids = new List<long> { bad, good } });

        var json = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
        json.GetProperty("resolvedCount").GetInt32().Should().Be(1);
        json.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain(e => e!.Contains("uploads/bad.jpg"));
        (await f.Db.OssAuditRecords.AnyAsync(r => r.Id == bad))
            .Should().BeTrue("the failed record stays for retry");
        (await f.Db.OssAuditRecords.AnyAsync(r => r.Id == good)).Should().BeFalse();
    }

    // ===== GetRecords =====

    [Fact]
    public async Task GetRecords_PaginatesFiltersAndReportsCounts()
    {
        var f = new Fixture();
        await f.SeedRecordAsync("uploads/p1.jpg", bucket: "uploads", status: 0, createdAt: 100);
        await f.SeedRecordAsync("uploads/p2.jpg", bucket: "uploads", status: 0, createdAt: 200);
        await f.SeedRecordAsync("mistakes/p3.jpg", bucket: "mistakes", status: 0, createdAt: 300);
        await f.SeedRecordAsync("uploads/i1.jpg", bucket: "uploads", status: 2, createdAt: 400);

        var all = ReadJson(Assert.IsType<OkObjectResult>(
            await f.Controller.GetRecords(page: 1, pageSize: 10)).Value);
        all.GetProperty("totalCount").GetInt32().Should().Be(4);
        // Ordered by CreatedAt descending.
        all.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("ObjectPath").GetString())
            .Should().ContainInOrder(new[]
            {
                "uploads/i1.jpg", "mistakes/p3.jpg", "uploads/p2.jpg", "uploads/p1.jpg",
            });

        var statusCounts = all.GetProperty("statusCounts");
        statusCounts.GetProperty("0").GetInt32().Should().Be(3);
        statusCounts.GetProperty("2").GetInt32().Should().Be(1);

        // bucketCounts only counts Pending records.
        var bucketCounts = all.GetProperty("bucketCounts");
        bucketCounts.GetProperty("uploads").GetInt32().Should().Be(2);
        bucketCounts.GetProperty("mistakes").GetInt32().Should().Be(1);

        var byBucket = ReadJson(Assert.IsType<OkObjectResult>(
            await f.Controller.GetRecords(bucket: "mistakes")).Value);
        byBucket.GetProperty("totalCount").GetInt32().Should().Be(1);

        var byStatus = ReadJson(Assert.IsType<OkObjectResult>(
            await f.Controller.GetRecords(status: 2)).Value);
        byStatus.GetProperty("totalCount").GetInt32().Should().Be(1);

        var page2 = ReadJson(Assert.IsType<OkObjectResult>(
            await f.Controller.GetRecords(page: 2, pageSize: 3)).Value);
        page2.GetProperty("items").GetArrayLength().Should().Be(1);
        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetRecords_MapsStatusText()
    {
        var f = new Fixture();
        await f.SeedRecordAsync("uploads/pending.jpg", status: 0);
        await f.SeedRecordAsync("uploads/resolved.jpg", status: 1);
        await f.SeedRecordAsync("uploads/ignored.jpg", status: 2);
        await f.SeedRecordAsync("uploads/odd.jpg", status: 7);

        var json = ReadJson(Assert.IsType<OkObjectResult>(
            await f.Controller.GetRecords(pageSize: 100)).Value);
        var texts = json.GetProperty("items").EnumerateArray()
            .ToDictionary(
                i => i.GetProperty("ObjectPath").GetString()!,
                i => i.GetProperty("StatusText").GetString());

        texts["uploads/pending.jpg"].Should().Be("Pending");
        texts["uploads/resolved.jpg"].Should().Be("Resolved");
        texts["uploads/ignored.jpg"].Should().Be("Ignored");
        texts["uploads/odd.jpg"].Should().Be("Unknown");
    }

    // ===== GetStatus =====

    [Fact]
    public async Task GetStatus_NoRuns_ReturnsDefaults()
    {
        var f = new Fixture();

        var json = ReadJson(Assert.IsType<OkObjectResult>(await f.Controller.GetStatus()).Value);

        json.GetProperty("isRunning").GetBoolean().Should().BeFalse();
        json.GetProperty("lastCompleted").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("lastFailed").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("pendingCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetStatus_ReportsRunningLastCompletedLastFailedAndPendingCount()
    {
        var f = new Fixture();
        f.Db.OssAuditRuns.AddRange(
            new OssAuditRun { StartedAt = 1000, CompletedAt = 1060, Status = 1, NewZombieCount = 4, TriggerType = "scheduled" },
            new OssAuditRun { StartedAt = 2000, CompletedAt = 2005, Status = 2, ErrorMessage = "Student service unavailable", TriggerType = "manual" },
            new OssAuditRun { StartedAt = 3000, Status = 0, TriggerType = "manual" });
        await f.SeedRecordAsync("uploads/p1.jpg", status: 0);
        await f.SeedRecordAsync("uploads/p2.jpg", status: 0);
        await f.SeedRecordAsync("uploads/i1.jpg", status: 2);
        await f.Db.SaveChangesAsync();

        var json = ReadJson(Assert.IsType<OkObjectResult>(await f.Controller.GetStatus()).Value);

        json.GetProperty("isRunning").GetBoolean().Should().BeTrue();
        json.GetProperty("pendingCount").GetInt32().Should().Be(2);

        var completed = json.GetProperty("lastCompleted");
        completed.GetProperty("NewZombieCount").GetInt32().Should().Be(4);
        completed.GetProperty("DurationSeconds").GetInt64().Should().Be(60);
        completed.GetProperty("TriggerType").GetString().Should().Be("scheduled");

        var failed = json.GetProperty("lastFailed");
        failed.GetProperty("ErrorMessage").GetString().Should().Be("Student service unavailable");
    }

    // ===== TriggerAudit =====

    [Fact]
    public async Task TriggerAudit_AlreadyRunning_Returns400AndDoesNotStartAnother()
    {
        var f = new Fixture();
        f.Db.OssAuditRuns.Add(new OssAuditRun { StartedAt = 1000, Status = 0, TriggerType = "scheduled" });
        await f.Db.SaveChangesAsync();

        var result = await f.Controller.TriggerAudit();

        AssertBadRequest(result, "审计正在运行中，请等待完成后再触发。");
        (await f.Db.OssAuditRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TriggerAudit_NotRunning_Returns200()
    {
        var f = new Fixture();

        var result = await f.Controller.TriggerAudit();

        var ok = Assert.IsType<OkObjectResult>(result);
        ReadJson(ok.Value).GetProperty("Success").GetBoolean().Should().BeTrue();
    }

    // ===== Helpers =====

    private static void AssertBadRequest(IActionResult result, string expectedMessage)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        ReadJson(bad.Value).GetProperty("Message").GetString().Should().Be(expectedMessage);
    }

    private static void AssertStatus(IActionResult result, int expected, string? expectedMessage)
    {
        var status = Assert.IsType<ObjectResult>(result);
        status.StatusCode.Should().Be(expected);
        if (expectedMessage is not null)
            ReadJson(status.Value).GetProperty("Message").GetString().Should().Be(expectedMessage);
    }

    private static JsonElement ReadJson(object? value) =>
        // Default options, deliberately: the controller's anonymous payloads mix literal
        // lower-case names ("items", "totalCount") with projected PascalCase ones ("ObjectPath",
        // "StatusText"). A camelCase policy would silently rename the latter and the assertions
        // would read the wrong casing.
        JsonSerializer.SerializeToElement(value);

    /// <summary>
    /// Wires <see cref="OssAuditController"/> against an in-memory audit database with mocked
    /// downstreams. Populate <see cref="StudentPaths"/>, <see cref="MistakePaths"/> and
    /// <see cref="HomeworkPaths"/> before invoking an action; set <see cref="StudentThrows"/> or
    /// <see cref="MistakeThrows"/> to simulate an unreachable reference source.
    /// </summary>
    private sealed class Fixture
    {
        public Fixture(bool withHomeworkClient = true, HttpStatusCode homeworkStatus = HttpStatusCode.OK)
        {
            var options = new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;
            Db = new AuditDbContext(options);

            Oss = new Mock<IOssService>();
            Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);

            Student = new Mock<IStudentHttpClient>();
            Student.Setup(s => s.GetAllUploadRecordsAsync(
                    It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (StudentThrows is not null) throw StudentThrows;
                    return new UploadRecordsPage
                    {
                        Items = StudentPaths
                            .Select(p => new UploadRecordDto { ImageEntries = { new ImageEntryDto { Path = p } } })
                            .ToList(),
                        TotalCount = 1,
                    };
                });

            Mistake = new Mock<IMistakeHttpClient>();
            Mistake.Setup(s => s.GetMistakeItemListAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<MistakeReviewStatus>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (MistakeThrows is not null) throw MistakeThrows;
                    return new MistakeItemPageResult
                    {
                        Items = MistakePaths
                            .Select(p => new MistakeItemDto
                            {
                                SourceRegions = { new MistakeSourceRegionDto { SourceImagePath = p } }
                            })
                            .ToList(),
                        PageMeta = new MistakePageMetaDto { TotalCount = 0 },
                    };
                });

            HomeworkReferenceClient? homework = null;
            if (withHomeworkClient)
            {
                var body = homeworkStatus == HttpStatusCode.OK
                    ? """{"success":true,"data":{"paths":["__PLACEHOLDER__"],"hasMore":false}}"""
                    : null;
                homework = new HomeworkReferenceClient(new HttpClient(new StubHandler(_ =>
                    body is null
                        ? new HttpResponseMessage(homeworkStatus)
                        : new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                body.Replace("__PLACEHOLDER__", string.Join("\",\"", HomeworkPaths)),
                                System.Text.Encoding.UTF8,
                                "application/json"),
                        }))
                { BaseAddress = new Uri("http://homework.test") });
            }

            Controller = new OssAuditController(
                Db,
                Student.Object,
                Mistake.Object,
                Oss.Object,
                BuildWorker(homework),
                Mock.Of<ILogger<OssAuditController>>(),
                homework);
        }

        public AuditDbContext Db { get; }
        public OssAuditController Controller { get; }
        public Mock<IOssService> Oss { get; }
        public Mock<IStudentHttpClient> Student { get; }
        public Mock<IMistakeHttpClient> Mistake { get; }

        public List<string> StudentPaths { get; } = new();
        public List<string> MistakePaths { get; } = new();
        public List<string> HomeworkPaths { get; } = new();
        public Exception? StudentThrows { get; set; }
        public Exception? MistakeThrows { get; set; }

        public async Task<long> SeedRecordAsync(
            string objectPath, string bucket = "uploads", int status = 0, long createdAt = 1)
        {
            var record = new OssAuditRecord
            {
                ObjectPath = objectPath,
                Bucket = bucket,
                Size = 10,
                Status = status,
                CreatedAt = createdAt,
            };
            Db.OssAuditRecords.Add(record);
            await Db.SaveChangesAsync();
            return record.Id;
        }

        public async Task AssertNotDeletedAsync(long id, string objectPath)
        {
            Oss.Verify(s => s.DeleteAsync(objectPath), Times.Never);
            (await Db.OssAuditRecords.AnyAsync(r => r.Id == id))
                .Should().BeTrue("a blocked resolve must keep the record for review");
        }

        /// <summary>
        /// TriggerAudit fires RunAuditAsync without awaiting it, so the worker needs a resolvable
        /// scope. Student throws by default in that path only if StudentThrows is set; otherwise the
        /// background audit runs against the same in-memory store and completes harmlessly.
        /// </summary>
        private OssAuditWorker BuildWorker(HomeworkReferenceClient? homework)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => Db);
            services.AddSingleton(Oss.Object);
            services.AddSingleton(Student.Object);
            services.AddSingleton(Mistake.Object);
            if (homework is not null) services.AddSingleton(homework);
            return new OssAuditWorker(
                services.BuildServiceProvider(),
                Mock.Of<ILogger<OssAuditWorker>>(),
                new ConfigurationBuilder().Build());
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_responder(request));
        }
    }
}
