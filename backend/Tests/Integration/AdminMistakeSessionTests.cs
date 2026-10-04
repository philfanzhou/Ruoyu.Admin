using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Models;
using Admin.WebApi.Tests.Authentication;
using Admin.WebApi.Tests.ServiceClients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    private WebApplicationFactory<Program> MistakeFactory(OidcTestAuthority authority, MistakeSessionCapture? capture,
        MutableAdmins admins, Mock<IStudentHttpClient> student, Mock<IOssService> oss,
        ManualOidcTime? time = null, OidcLogCapture? logs = null, Uri? origin = null)
        => CreateFactory(configureTestServices: services =>
        {
            services.Configure<OpenIdConnectOptions>(AdminOidcSettings.OidcScheme, options =>
                options.Backchannel = new HttpClient(authority, false) { Timeout = TimeSpan.FromSeconds(2) });
            if (capture is not null) services.AddHttpClient<IMistakeHttpClient, MistakeHttpClient>().ConfigurePrimaryHttpMessageHandler(() => capture);
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
            services.Replace(ServiceDescriptor.Singleton(student.Object));
            services.Replace(ServiceDescriptor.Singleton(oss.Object));
            if (time is not null) services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
            if (logs is not null)
            {
                services.RemoveAll<ILoggerFactory>();
                services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace);
                services.AddSingleton<ILoggerFactory>(provider => new LoggerFactory([logs], provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>()));
            }
        }, settings: new Dictionary<string, string?>
        {
            ["MistakeService:UseSessionToken"] = "true", ["MistakeService:Url"] = origin?.GetLeftPart(UriPartial.Authority) ?? "https://mistake.example.test",
            ["AdminOidc:RedirectUri"] = OidcTestAuthority.RedirectUri,
            ["AdminOidc:PostLogoutRedirectUri"] = "https://admin.example.test/api/auth/oidc/logout-callback",
            ["IdentityService:Authority"] = OidcTestAuthority.Issuer,
            ["IdentityService:AppId"] = OidcTestAuthority.ClientId, ["IdentityService:AppSecret"] = OidcTestAuthority.Secret
        });

    private static Mock<IStudentHttpClient> MistakeStudent()
    {
        var student = new Mock<IStudentHttpClient>();
        student.Setup(s => s.GetStudentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Ruoyu.Admin.ServiceClients.StudentDto { Id = "student", Name = "Synthetic" });
        student.Setup(s => s.GetUploadRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadRecordDto { Id = "upload", StudentId = "student", ImageEntries = [new ImageEntryDto { Path = "uploads/a.png" }] });
        return student;
    }

    [Theory]
    [InlineData("anonymous")][InlineData("missing")][InlineData("stamp")][InlineData("issuer")]
    [InlineData("no-token")][InlineData("bad-expiry")][InlineData("expired")][InlineData("allowlist")]
    [InlineData("authorization")][InlineData("old-cookie")]
    public async Task MistakeSession_RealCodeHandshakeRejectsInvalidCurrentTicketBeforeAllBusinessIo(string defect)
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture();
        var admins = new MutableAdmins(); var student = MistakeStudent(); var oss = new Mock<IOssService>(); var time = new ManualOidcTime();
        using var factory = MistakeFactory(authority, capture, admins, student, oss, time); using var browser = Browser(factory);
        var cookie = await LoginSession(browser, authority); var (key, ticket) = await Stored(factory, cookie);
        var store = factory.Services.GetRequiredService<MemoryTicketStore>();
        if (defect == "anonymous") cookie = "";
        if (defect == "old-cookie") cookie = "adminAuthToken=" + authority.LegacyBearer();
        if (defect == "missing") await store.RemoveAsync(key);
        if (defect == "allowlist") admins.CurrentValue.AdminUserIds.Clear();
        if (defect == "stamp") ticket.Properties.Items["oidc.subject"] = "wrong";
        if (defect == "issuer") ticket.Properties.Items["oidc.issuer"] = "https://wrong.example.test";
        if (defect == "no-token") ticket.Properties.StoreTokens(ticket.Properties.GetTokens().Where(t => t.Name != "access_token"));
        if (defect == "bad-expiry") ticket.Properties.UpdateTokenValue("expires_at", "tomorrow");
        if (defect == "expired") ticket.Properties.UpdateTokenValue("expires_at", time.GetUtcNow().ToString("o"));
        if (defect is "stamp" or "issuer" or "no-token" or "bad-expiry" or "expired") await store.RenewAsync(key, ticket);
        foreach (var path in new[] { "/api/admin/mistakes", "/api/admin/mistakes/item", "/api/admin/mistakes/by-upload/upload", "/api/admin/image?path=mistakes/a.png", "/api/admin/oss-upload-records/legacy-check/upload" })
        {
            using var response = await Api(browser, path, cookie, authorization: defect == "authorization" ? ["Bearer " + authority.LegacyBearer()] : null);
            Assert.Equal(defect == "allowlist" ? 403 : 401, (int)response.StatusCode);
        }
        Assert.Empty(capture.Requests); Assert.Empty(student.Invocations); Assert.Empty(oss.Invocations);
    }

    [Fact]
    public async Task MistakeSession_All14UseCurrentScopedTicketEvenWithForgedPrincipalAndCachedAuthentication()
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture();
        var admins = new MutableAdmins(); var student = MistakeStudent(); var oss = new Mock<IOssService>();
        using var factory = MistakeFactory(authority, capture, admins, student, oss); using var browser = Browser(factory);
        var cookie = await LoginSession(browser, authority); var (key, ticket) = await Stored(factory, cookie);
        var store = factory.Services.GetRequiredService<MemoryTicketStore>();
        var accessor = factory.Services.GetRequiredService<IHttpContextAccessor>();
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "forged")], "forged")) };
        context.Request.Headers.Cookie = cookie; accessor.HttpContext = context;
        try
        {
            // Populate the framework's per-request cache, then update/revoke the SERVER ticket.
            Assert.True((await context.AuthenticateAsync(AdminOidcSettings.SessionScheme)).Succeeded);
            context.Items[AdminSessionBoundary.TrustedSessionKey] = new AdminSessionResult(200, context.User, "forged-items-token");
            var client = scope.ServiceProvider.GetRequiredService<IMistakeHttpClient>();
            for (var method = 0; method < 14; method++)
            {
                var token = "fictitious-current-server-token-" + method;
                ticket.Properties.UpdateTokenValue("access_token", token); await store.RenewAsync(key, ticket);
                capture.MethodData = method;
                Assert.NotNull(await MistakeSessionClientTests.Call(client, method));
                var sent = capture.Requests.Last(); Assert.Equal("Bearer " + token, sent.Authorization);
                Assert.False(sent.Cookie); Assert.False(sent.Csrf); Assert.Equal("mistake.example.test", sent.Host);
            }
            var validSnapshot = Microsoft.AspNetCore.Authentication.TicketSerializer.Default.Serialize(ticket);
            foreach (var defect in new[] { "stamp", "issuer", "token", "token-control", "expiry", "allowlist" })
            {
                ticket = Microsoft.AspNetCore.Authentication.TicketSerializer.Default.Deserialize(validSnapshot)!;
                if (defect == "stamp") ticket.Properties.Items["oidc.subject"] = "wrong";
                if (defect == "issuer") ticket.Properties.Items["oidc.issuer"] = "https://wrong.example.test";
                if (defect == "token") ticket.Properties.UpdateTokenValue("access_token", "");
                if (defect == "token-control") ticket.Properties.UpdateTokenValue("access_token", "invalid\r\ntoken");
                if (defect == "expiry") ticket.Properties.UpdateTokenValue("expires_at", "tomorrow");
                if (defect == "allowlist") admins.CurrentValue.AdminUserIds.Clear();
                await store.RenewAsync(key, ticket);
                var unchanged = capture.Requests.Count;
                Assert.Equal(defect == "allowlist" ? 403 : 401, (await Assert.ThrowsAsync<MistakeSessionException>(() => client.GetMistakeItemAsync("item"))).StatusCode);
                Assert.Equal(unchanged, capture.Requests.Count);
                admins.CurrentValue.AdminUserIds.Add("FAKE-SUBJECT");
            }
            await store.RemoveAsync(key);
            var before = capture.Requests.Count;
            Assert.Equal(401, (await Assert.ThrowsAsync<MistakeSessionException>(() => client.GetMistakeItemAsync("item"))).StatusCode);
            Assert.Equal(before, capture.Requests.Count);
            accessor.HttpContext = null;
            await Assert.ThrowsAsync<MistakeDownstreamException>(() => client.GetMistakeItemAsync("item"));
            Assert.Equal(before, capture.Requests.Count);
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task MistakeSession_TwoRealSubjectsConcurrentScopesAndHandlerReuseNeverMixTokens()
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture();
        var admins = new MutableAdmins(); admins.CurrentValue.AdminUserIds.Add("second-subject");
        using var factory = MistakeFactory(authority, capture, admins, MistakeStudent(), new Mock<IOssService>()); using var browser = Browser(factory);
        var first = await Start(browser); using var firstCallback = await Callback(browser, first, authority.Code(first.Query, accessToken: "fictitious-first-token"));
        var second = await Start(browser); using var secondCallback = await Callback(browser, second, authority.Code(second.Query, subject: "second-subject", accessToken: "fictitious-second-token"));
        var cookies = new[] { SessionCookie(firstCallback), SessionCookie(secondCallback) };
        await Task.WhenAll(Enumerable.Range(0, 24).Select(async i =>
        {
            using var response = await Api(browser, "/api/admin/mistakes?studentId=" + i % 2, cookies[i % 2]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }));
        Assert.Equal(24, capture.Requests.Count);
        foreach (var sent in capture.Requests)
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(sent.Query);
            Assert.Equal(query["studentId"] == "0" ? "Bearer fictitious-first-token" : "Bearer fictitious-second-token", sent.Authorization);
        }
    }

    [Theory]
    [InlineData("/api/admin/mistakes", "GET")]
    [InlineData("/api/admin/mistakes/item", "GET")]
    [InlineData("/api/admin/mistakes/by-upload/upload", "GET")]
    [InlineData("/api/admin/mistakes/item", "PUT")]
    [InlineData("/api/admin/oss-upload-records/upload/assign", "POST")]
    [InlineData("/api/admin/oss-upload-records/legacy-clean/upload", "POST")]
    [InlineData("/api/admin/image?path=mistakes/a.png", "GET")]
    public async Task MistakeSession_AllSevenControllerChainsFailSafelyAndStopLaterWrites(string path, string method)
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture(); var logs = new OidcLogCapture();
        var student = MistakeStudent(); var oss = new Mock<IOssService>();
        using var factory = MistakeFactory(authority, capture, new MutableAdmins(), student, oss, logs: logs); using var browser = Browser(factory);
        var cookie = await LoginSession(browser, authority); var csrf = await Csrf(browser, cookie);
        foreach (var status in new[] { 401, 403, 302, 307, 500 })
        {
            capture.Status = status; capture.Body = "access-token-canary signed-url-canary exception-canary";
            using var response = await Api(browser, path, cookie + "; " + csrf.Cookie, method, csrf: [csrf.Token],
                body: method == "GET" ? null : JsonContent.Create(new { studentId = "student", subject = 1, grade = 2, assignments = new[] { new { subject = 1, grade = 2, imageIndices = new[] { 0 } } } }));
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.DoesNotContain("canary", await response.Content.ReadAsStringAsync());
            Assert.Null(response.Headers.Location);
        }
        student.Verify(s => s.RemoveImagesFromRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        student.Verify(s => s.DeleteUploadRecordAfterReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never); oss.Verify(s => s.CopyObjectAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.DoesNotContain("access-token-canary", string.Join("\n", logs.Messages));
        Assert.DoesNotContain("signed-url-canary", string.Join("\n", logs.Messages));
        Assert.DoesNotContain(OidcTestAuthority.AccessToken, string.Join("\n", logs.Messages));
    }

    [Fact]
    public async Task MistakeSession_LegacyReviewerFailureAfterSuccessfulReadStopsStudentAndS3Writes()
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture { ReviewerReject = true };
        var student = MistakeStudent(); var oss = new Mock<IOssService>();
        using var factory = MistakeFactory(authority, capture, new MutableAdmins(), student, oss); using var browser = Browser(factory);
        var cookie = await LoginSession(browser, authority); var csrf = await Csrf(browser, cookie);
        using var response = await Api(browser, "/api/admin/oss-upload-records/legacy-clean/upload", cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token]);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, capture.Requests.Count);
        Assert.Contains("admin-legacy-cleanup", capture.LastBody);
        Assert.Empty(student.Invocations); Assert.Empty(oss.Invocations);
    }

    [Fact]
    public async Task MistakeSession_CsrfAdmissionAndRealImageRedirectHaveNoBrowserToken()
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture();
        using var factory = MistakeFactory(authority, capture, new MutableAdmins(), MistakeStudent(), new Mock<IOssService>()); using var browser = Browser(factory);
        var cookie = await LoginSession(browser, authority); var csrf = await Csrf(browser, cookie);
        foreach (var headers in new string[]?[] { null, ["wrong"], [csrf.Token, csrf.Token] })
        {
            using var failed = await Api(browser, "/api/admin/mistakes/item", cookie + "; " + csrf.Cookie, "PUT", csrf: headers, body: JsonContent.Create(new { studentId = "student" }));
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode); Assert.Empty(capture.Requests);
        }
        using var image = await Api(browser, "/api/admin/image?path=mistakes/a.png", cookie);
        Assert.Equal(HttpStatusCode.Redirect, image.StatusCode);
        Assert.Equal("https://s3.example.test", image.Headers.Location!.GetLeftPart(UriPartial.Authority));
        Assert.DoesNotContain(OidcTestAuthority.AccessToken, image.Headers.Location.OriginalString);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task MistakeSession_All14RequestDisconnectsPropagateWithoutFallbackOrReplay()
    {
        using var authority = new OidcTestAuthority(); var capture = new MistakeSessionCapture { Delay = true };
        using var factory = MistakeFactory(authority, capture, new MutableAdmins(), MistakeStudent(), new Mock<IOssService>()); using var browser = Browser(factory);
        var cookie = await LoginSession(browser, authority);
        var accessor = factory.Services.GetRequiredService<IHttpContextAccessor>();
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Cookie = cookie; accessor.HttpContext = context;
        try
        {
            var client = scope.ServiceProvider.GetRequiredService<IMistakeHttpClient>();
            for (var method = 0; method < 14; method++)
            {
                using var before = new CancellationTokenSource(); before.Cancel(); context.RequestAborted = before.Token;
                var count = capture.Requests.Count;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MistakeSessionClientTests.Call(client, method));
                Assert.Equal(count, capture.Requests.Count);
                using var during = new CancellationTokenSource(TimeSpan.FromMilliseconds(70)); context.RequestAborted = during.Token;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MistakeSessionClientTests.Call(client, method));
                Assert.Equal(count + 1, capture.Requests.Count);
            }
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task MistakeSession_RealPrimaryNeverFollowsRedirectsStoresCookiesOrForwardsExtraHeaders()
    {
        await using var target = await MistakeSessionClientTests.Receiver.StartAsync();
        await using var stranger = await MistakeSessionClientTests.Receiver.StartAsync();
        using var authority = new OidcTestAuthority();
        using var factory = MistakeFactory(authority, null, new MutableAdmins(), MistakeStudent(), new Mock<IOssService>(), origin: target.Origin);
        using var browser = Browser(factory); var cookie = await LoginSession(browser, authority);
        foreach (var location in new[] { new Uri(target.Origin, "/redirect-receiver"), new Uri(stranger.Origin, "/redirect-receiver") })
        {
            target.Status = 302; target.Location = location.AbsoluteUri;
            var before = target.Sends;
            using var failed = await Api(browser, "/api/admin/mistakes", cookie);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            Assert.Equal(before + 1, target.Sends); Assert.Equal(0, stranger.Sends);
        }
        target.Status = 200; target.Location = null;
        target.Body = JsonSerializer.Serialize(new { success = true, data = MistakeSessionClientTests.Data(0) });
        for (var i = 0; i < 2; i++)
        {
            using var success = await Api(browser, "/api/admin/mistakes", cookie);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            Assert.Equal("Bearer " + OidcTestAuthority.AccessToken, target.LastAuthorization);
            Assert.False(target.LastCookie); Assert.False(target.LastCsrf); Assert.False(target.LastTrace);
        }
    }
}

internal sealed class MistakeSessionCapture : HttpMessageHandler
{
    internal readonly ConcurrentQueue<(string Method, string Path, string Query, string Host, string? Authorization, bool Cookie, bool Csrf)> Requests = [];
    internal int Status = 200;
    internal int? MethodData;
    internal string? Body;
    internal string LastBody = "";
    internal bool ReviewerReject;
    internal bool Delay;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        Requests.Enqueue((request.Method.Method, uri.AbsolutePath, uri.Query, uri.Host, request.Headers.Authorization?.ToString(), request.Headers.Contains("Cookie"), request.Headers.Contains("X-CSRF-TOKEN")));
        if (Delay) await Task.Delay(1000, ct);
        LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var status = ReviewerReject && uri.AbsolutePath == "/api/mistakes/complete-review" ? 403 : Status;
        object data = MethodData is { } index ? MistakeSessionClientTests.Data(index) : uri.AbsolutePath switch
        {
            "/api/mistakes" => MistakeSessionClientTests.Data(0),
            "/api/mistakes/presigned-url" => MistakeSessionClientTests.Data(13),
            _ when uri.AbsolutePath.StartsWith("/api/mistakes/by-upload/") => new { items = new[] { new { id = "item", studentId = "student", reviewStatus = 2, sourceRegions = Array.Empty<object>() } } },
            _ => MistakeSessionClientTests.Data(1)
        };
        return new((HttpStatusCode)status) { Content = Body is null ? JsonContent.Create(new { success = true, data }) : new StringContent(Body, System.Text.Encoding.UTF8, "application/json") };
    }
}
