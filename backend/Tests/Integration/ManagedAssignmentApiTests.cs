using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    private sealed class AssignmentWire : HttpMessageHandler
    {
        internal readonly List<(string Token, JsonElement Body)> Calls = [];
        internal readonly Queue<(int Status, object Body)> Replies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://mistake.example.test/api/mistakes/upload", request.RequestUri!.ToString());
            Calls.Add((request.Headers.Authorization!.Parameter!, await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken)));
            var reply = Replies.Dequeue();
            return new((HttpStatusCode)reply.Status) { Content = JsonContent.Create(reply.Body) };
        }
    }

    private WebApplicationFactory<Program> AssignmentFactory(OidcTestAuthority authority, SessionBusinessProbe probe,
        AssignmentWire wire, bool session = true, bool enabled = true) => OidcFactory(authority, configure: services =>
    {
        services.Replace(ServiceDescriptor.Singleton(new ManagedAssignmentOptions(enabled)));
        services.AddSingleton<IManagedMistakeHttpClient>(new ManagedMistakeHttpClient(new HttpClient(wire)
            { BaseAddress = new Uri("https://mistake.example.test") }));
        services.Replace(ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IOptionsMonitor<AdminPortalOptions>>(probe.Admins));
        services.Replace(ServiceDescriptor.Singleton(probe.Student.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Mistake.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Oss.Object));
    }, sessionApi: session);

    private static readonly Guid AssignmentSource = Guid.NewGuid(), AssignmentStudent = Guid.NewGuid(), AssignmentRevision = Guid.NewGuid();
    private static object AssignmentBody(Guid[] keys, int subject = 2) => new
    {
        mode = "managed-v1", studentId = AssignmentStudent, expectedContentRevision = AssignmentRevision,
        assignments = keys.Select(key => new { requestKey = key, subject, grade = 7,
            sourcePaths = new[] { "uploads/题目/A.PNG", "uploads/题目/A.PNG", "uploads/题目/a.PNG" }, comments = "fixed" })
    };
    private static string AssignmentRoute => $"/api/admin/oss-upload-records/{AssignmentSource}/assign";

    [Fact]
    public async Task ManagedAssignment_SessionCsrfAndOriginalToken_OrderedStopAndSameProposalRetry()
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var wire = new AssignmentWire();
        using var factory = AssignmentFactory(authority, probe, wire); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }; var firstId = Guid.NewGuid();
        using var refused = await Api(client, AssignmentRoute, cookie, "POST", body: JsonContent.Create(AssignmentBody(keys)));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode); Assert.Empty(wire.Calls);
        wire.Replies.Enqueue((200, new { success = true, data = new { success = true, createdItemIds = new[] { firstId } } }));
        wire.Replies.Enqueue((409, new { success = false, errorKind = "request_payload_conflict" }));
        using var response = await Api(client, AssignmentRoute, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token],
            body: JsonContent.Create(AssignmentBody(keys)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { "Completed", "Failed", "NotAttempted" }, result.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("state").GetString()));
        Assert.Equal(firstId, result.GetProperty("groups")[0].GetProperty("createdItemIds")[0].GetGuid());
        Assert.Equal(2, wire.Calls.Count); Assert.All(wire.Calls, call => Assert.Equal(authority.LastAccessToken, call.Token));
        Assert.Equal(new[] { "uploads/题目/A.PNG", "uploads/题目/a.PNG" }, wire.Calls[0].Body.GetProperty("imagePaths").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(AssignmentRevision, wire.Calls[0].Body.GetProperty("expectedContentRevision").GetGuid());
        // The existing source may now be edited or absent: retry never reads it to remap the fixed proposal.
        probe.Student.Setup(s => s.GetUploadRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("source no longer exists"));
        foreach (var id in new[] { firstId, Guid.NewGuid(), Guid.NewGuid() })
            wire.Replies.Enqueue((200, new { success = true, data = new { success = true, createdItemIds = new[] { id } } }));
        using var retry = await Api(client, AssignmentRoute, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token], body: JsonContent.Create(AssignmentBody(keys)));
        Assert.True((await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        Assert.Equal(wire.Calls[0].Body.GetRawText(), wire.Calls[2].Body.GetRawText());
        probe.Student.VerifyNoOtherCalls(); probe.Mistake.VerifyNoOtherCalls(); probe.Oss.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ManagedAssignment_SharedOriginalCropGroupsTravelAsRegions_MixedOrInvalidRejectedLocally()
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var wire = new AssignmentWire();
        using var factory = AssignmentFactory(authority, probe, wire); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        object Body(object assignment) => new
        {
            mode = "managed-v1", studentId = AssignmentStudent, expectedContentRevision = AssignmentRevision,
            assignments = new[] { assignment }
        };
        // Paths and regions are mutually exclusive on the wire; box coordinates must satisfy
        // X1 >= 0, Y1 >= 0, X1 < X2, Y1 < Y2 upstream — reject locally, zero outbound.
        object[] rejected =
        [
            Body(new { requestKey = Guid.NewGuid(), subject = 2, grade = 7, comments = "mixed",
                sourcePaths = new[] { "uploads/题目/A.PNG" },
                sourceRegions = new[] { new { sourceImagePath = "uploads/题目/A.PNG", boundingBox = (object?)null } } }),
            Body(new { requestKey = Guid.NewGuid(), subject = 2, grade = 7, comments = "inverted-box",
                sourcePaths = (string[]?)null,
                sourceRegions = new[] { new { sourceImagePath = "uploads/题目/A.PNG", boundingBox = new { x1 = 30, y1 = 20, x2 = 10, y2 = 40 } } } }),
            Body(new { requestKey = Guid.NewGuid(), subject = 2, grade = 7, comments = "negative-origin",
                sourcePaths = (string[]?)null,
                sourceRegions = new[] { new { sourceImagePath = "uploads/题目/A.PNG", boundingBox = new { x1 = -1, y1 = 0, x2 = 10, y2 = 40 } } } }),
            Body(new { requestKey = Guid.NewGuid(), subject = 2, grade = 7, comments = "empty-regions",
                sourcePaths = (string[]?)null, sourceRegions = Array.Empty<object>() }),
            Body(new { requestKey = Guid.NewGuid(), subject = 2, grade = 7, comments = "blank-region-path",
                sourcePaths = (string[]?)null,
                sourceRegions = new[] { new { sourceImagePath = "", boundingBox = (object?)null } } }),
        ];
        foreach (var bad in rejected)
        {
            using var refused = await Api(client, AssignmentRoute, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token], body: JsonContent.Create(bad));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        Assert.Empty(wire.Calls);
        // The AC1 shape: two crop groups over one shared original image.
        var cropA = Guid.NewGuid(); var cropB = Guid.NewGuid();
        wire.Replies.Enqueue((200, new { success = true, data = new { success = true, createdItemIds = new[] { Guid.NewGuid() } } }));
        wire.Replies.Enqueue((200, new { success = true, data = new { success = true, createdItemIds = new[] { Guid.NewGuid() } } }));
        using var response = await Api(client, AssignmentRoute, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token],
            body: JsonContent.Create(new
            {
                mode = "managed-v1", studentId = AssignmentStudent, expectedContentRevision = AssignmentRevision,
                assignments = new object[]
                {
                    new { requestKey = cropA, subject = 2, grade = 7, comments = "crop-a",
                        sourceRegions = new[] { new { sourceImagePath = "uploads/题目/A.PNG", boundingBox = new { x1 = 0, y1 = 0, x2 = 100, y2 = 50 } } } },
                    new { requestKey = cropB, subject = 2, grade = 7, comments = "crop-b",
                        sourceRegions = new[] { new { sourceImagePath = "uploads/题目/A.PNG", boundingBox = new { x1 = 0, y1 = 50, x2 = 100, y2 = 100 } } } },
                }
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(2, wire.Calls.Count);
        foreach (var call in wire.Calls)
        {
            Assert.Equal(0, call.Body.GetProperty("imagePaths").GetArrayLength());
            var region = call.Body.GetProperty("sourceRegions")[0];
            Assert.Equal("uploads/题目/A.PNG", region.GetProperty("sourceImagePath").GetString());
            Assert.True(region.GetProperty("boundingBox").GetProperty("x2").GetInt32() > region.GetProperty("boundingBox").GetProperty("x1").GetInt32());
        }
        Assert.Equal(cropA, wire.Calls[0].Body.GetProperty("requestKey").GetGuid());
        Assert.Equal(cropB, wire.Calls[1].Body.GetProperty("requestKey").GetGuid());
        probe.Student.VerifyNoOtherCalls(); probe.Mistake.VerifyNoOtherCalls(); probe.Oss.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedAssignment_AnyBearerEvenWithTrustedSessionAndCsrfNeverSends(bool validBearer)
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var wire = new AssignmentWire();
        using var factory = AssignmentFactory(authority, probe, wire); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        var token = validBearer ? authority.LegacyBearer() : "invalid";
        using var response = await Api(client, AssignmentRoute, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token],
            authorization: ["Bearer " + token], body: JsonContent.Create(AssignmentBody([Guid.NewGuid()])));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Empty(wire.Calls);
        probe.Student.VerifyNoOtherCalls(); probe.Mistake.VerifyNoOtherCalls(); probe.Oss.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ManagedAssignment_LegacyBearerWithoutSessionNeverAuthenticatesOrSends()
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var wire = new AssignmentWire();
        // The old UseSessionForAdminApi key is absent: session remains the only scheme regardless,
        // and a request with no session cookie plus a legacy Bearer is rejected before any outbound call.
        using var factory = AssignmentFactory(authority, probe, wire, session: false); using var client = Browser(factory);
        using var response = await Api(client, AssignmentRoute, method: "POST", authorization: ["Bearer " + authority.LegacyBearer()],
            body: JsonContent.Create(AssignmentBody([Guid.NewGuid()])));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Empty(wire.Calls); probe.Student.VerifyNoOtherCalls(); probe.Mistake.VerifyNoOtherCalls(); probe.Oss.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, 2, 503)]
    [InlineData(true, 0, 400)]
    public async Task ManagedAssignment_DisabledOrBadInputNeverFallsBackToStudentOrLegacy(bool enabled, int subject, int status)
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var wire = new AssignmentWire();
        using var factory = AssignmentFactory(authority, probe, wire, enabled: enabled); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        using var response = await Api(client, AssignmentRoute, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token], body: JsonContent.Create(AssignmentBody([Guid.NewGuid()], subject)));
        Assert.Equal(status, (int)response.StatusCode); Assert.Empty(wire.Calls);
        probe.Student.VerifyNoOtherCalls(); probe.Mistake.VerifyNoOtherCalls(); probe.Oss.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("valid", true, 200)]
    [InlineData("empty", true, 200)]
    [InlineData("missing_items", true, 502)]
    [InlineData("missing_revision", true, 502)]
    [InlineData("missing_entries", true, 502)]
    [InlineData("missing_type", true, 502)]
    [InlineData("missing_revision", false, 200)]
    public async Task ManagedAssignment_MetadataRequiresActualFields_EmptyValidPageAndLegacyRemainCompatible(string kind, bool enabled, int status)
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var wire = new AssignmentWire();
        var record = new UploadRecordDto { Id = AssignmentSource.ToString(), StudentId = AssignmentStudent.ToString(),
            ContentRevision = AssignmentRevision, ImageEntries = [new ImageEntryDto { Path = "uploads/题目/A.PNG", Type = "mistake" }] };
        if (kind == "missing_revision") record.ContentRevision = null;
        if (kind == "missing_type") record.ImageEntries[0].Type = "";
        if (kind == "missing_entries") record = new UploadRecordDto { Id = record.Id, StudentId = record.StudentId, ContentRevision = record.ContentRevision };
        var page = kind == "missing_items" ? new UploadRecordsPage() : new UploadRecordsPage { Items = kind == "empty" ? [] : [record], TotalCount = 1 };
        probe.Student.Setup(s => s.GetAllUploadRecordsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(page);
        probe.Student.Setup(s => s.GetStudentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new StudentDto { Name = "Test student" });
        using var factory = AssignmentFactory(authority, probe, wire, enabled: enabled); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        using var response = await Api(client, "/api/admin/oss-upload-records", cookie);
        Assert.Equal(status, (int)response.StatusCode); Assert.Empty(wire.Calls);
        if (status == 502) probe.Student.Verify(s => s.GetStudentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        if (status == 200 && kind != "empty")
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(); var item = result.GetProperty("items")[0];
            Assert.Equal(enabled ? "managed-v1" : "legacy", item.GetProperty("assignmentProtocol").GetString());
            Assert.True(item.TryGetProperty("imagePaths", out _)); Assert.True(item.TryGetProperty("imageEntries", out _));
            if (enabled) Assert.Equal(AssignmentRevision, item.GetProperty("contentRevision").GetGuid());
        }
    }
}
