using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Models;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using Admin.WebApi.Tests.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using Ruoyu.Admin.ServiceClients.StorageReferences;
using SignaCore.Client.AspNetCore;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    private WebApplicationFactory<Program> SessionFactory(OidcTestAuthority authority, SessionBusinessProbe probe,
        ManualOidcTime? time = null, string? root = null, IDataProtectionProvider? protection = null) => OidcFactory(authority, time, services =>
    {
        services.AddControllers().AddApplicationPart(typeof(SessionProbeController).Assembly);
        services.AddSingleton(probe);
        services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => probe.Identity);
        services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(probe.Admins));
        services.Replace(ServiceDescriptor.Singleton(probe.Oss.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.References.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Student.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Mistake.Object));
        services.Replace(ServiceDescriptor.Singleton(new HomeworkReferenceClient(new HttpClient(probe.Homework)
            { BaseAddress = new Uri("https://homework.example.test") })));
        services.Configure<JwtBearerOptions>("Bearer", options =>
        {
            options.Events.OnMessageReceived = context => { Interlocked.Increment(ref probe.BearerAuthentications); return Task.CompletedTask; };
            var metadata = new OpenIdConnectConfiguration { Issuer = OidcTestAuthority.Issuer };
            metadata.SigningKeys.Add(authority.SigningKey);
            options.Configuration = metadata;
        });
    }, root: root, protection: protection, sessionApi: true);

    private static async Task<string> LoginSession(HttpClient client, OidcTestAuthority authority)
    {
        var start = await Start(client);
        using var response = await Callback(client, start, authority.Code(start.Query));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return SessionCookie(response);
    }

    // The session cookie value is the opaque store key itself; the ticket it references keeps
    // the principal and both tokens server-side.
    private static async Task<(string Key, SignaCoreSessionTicket Ticket)> Stored(WebApplicationFactory<Program> factory, string cookie)
    {
        var key = cookie[(cookie.IndexOf('=') + 1)..];
        return (key, (await factory.Services.GetRequiredService<ITicketStore>().RetrieveAsync(key, CancellationToken.None))!);
    }

    // A forged ticket enters only through the public ITicketStore contract, exactly like any
    // hypothetical corrupt-store content the accessor must fail closed against.
    private static async Task<string> ForgedTicket(WebApplicationFactory<Program> factory, string? subject = "fake-subject",
        string issuer = OidcTestAuthority.Issuer, string? accessToken = "forged-access-token", params Claim[] extraClaims)
    {
        var claims = new List<Claim>();
        if (issuer is not null) claims.Add(new("iss", issuer));
        if (subject is not null) claims.Add(new("sub", subject));
        claims.Add(new Claim("name", "fake-name"));
        claims.AddRange(extraClaims);
        var ticket = new SignaCoreSessionTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SignaCoreHostedLoginDefaults.SessionAuthenticationScheme)),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), accessToken ?? "", "forged-id-token");
        var key = await factory.Services.GetRequiredService<ITicketStore>().StoreAsync(ticket, CancellationToken.None);
        return AdminOidcSettings.SessionCookie + "=" + key;
    }

    private static async Task<HttpResponseMessage> Api(HttpClient client, string path, string? cookie = null,
        string method = "GET", string[]? authorization = null, string[]? csrf = null, HttpContent? body = null,
        CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = body };
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (csrf is not null) request.Headers.TryAddWithoutValidation(AdminSessionBoundary.CsrfHeader, csrf);
        return await client.SendAsync(request, cancellation);
    }

    private static async Task<(string Token, string Cookie)> Csrf(HttpClient client, string session)
    {
        using var response = await Api(client, "/api/auth/csrf", session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // #55: the six-header ServiceMantle baseline splits the two directives —
        // Cache-Control carries exactly no-store, and no-cache moved to Pragma.
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("no-cache", string.Join(",", response.Headers.Pragma));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("adminCsrf="));
        foreach (var flag in new[] { "httponly", "secure", "samesite=lax", "path=/" }) Assert.Contains(flag, cookie.ToLowerInvariant());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("requestToken").GetString()!, cookie.Split(';')[0]);
    }

    private static async Task Rejected(HttpResponseMessage response, int code, string error)
    {
        using (response)
        {
            Assert.Equal(code, (int)response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("{\"error\":\"" + error + "\"}", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task SessionApi_RealHandshakeAuthorizesBareAuthorizeFallbackAndNativeImages()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        foreach (var path in new[] { ProtectedApiRoute, "/api/admin/session-probe", "/api/admin/session-fallback" })
            Assert.Equal(HttpStatusCode.OK, (await Api(client, path, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api(client, "/api/admin/session-probe", cookie, "HEAD")).StatusCode);
        using var image = await Api(client, "/api/admin/image?path=uploads/test.jpg&size=small", cookie);
        Assert.Equal(HttpStatusCode.Redirect, image.StatusCode);
        Assert.Equal("https://images.example.test/test.jpg", image.Headers.Location!.OriginalString);
        probe.Student.Verify(s => s.GetPresignedUrlAsync("uploads/test.jpg", 3600, "small", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, probe.Writes);
        foreach (var path in new[] { "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
            Assert.Equal(HttpStatusCode.OK, (await Api(client, path, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api(client, "/api/identity/admin/users", cookie)).StatusCode);
        // The package's logout endpoint owns its gate: no antiforgery token, no session change.
        using var logout = await Api(client, AdminOidcSettings.LogoutPath, cookie, "POST");
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal("{\"outcome\":\"csrf_rejected\"}", await logout.Content.ReadAsStringAsync());
        // The accessor returns the server token, without leaking it through formatting.
        using var scope = factory.Services.CreateScope();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Cookie = cookie;
        var session = await context.RequestServices.GetRequiredService<AdminSessionAccessor>().AuthenticateAsync(context);
        Assert.Equal(authority.LastAccessToken, session.AccessToken);
        Assert.DoesNotContain(authority.LastAccessToken!, session.ToString());
        // The boundary principal carries only the verified identity; ID-token roles never enter it.
        Assert.False(session.Principal!.IsInRole("admin"));
        Assert.Null(session.Principal.FindFirst("role"));
    }

    [Theory]
    [InlineData("empty")][InlineData("invalid")][InlineData("multi")][InlineData("valid")]
    public async Task SessionApi_AnyAuthorizationHeaderPreventsCookieFallback(string header)
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        var headers = header switch { "empty" => new[] { "" }, "multi" => new[] { "Bearer wrong", "Bearer other" },
            "valid" => new[] { "Bearer " + authority.LegacyBearer() }, _ => new[] { "Basic wrong" } };
        foreach (var path in new[] { "/api/admin/session-probe", "/api/auth/csrf" })
        {
            if (header == "empty")
            {
                // HttpClient drops a zero-length Authorization while copying to TestServer.
                // Construct the actual inbound header dictionary, as Kestrel receives it.
                var response = await factory.Server.SendAsync(context =>
                {
                    context.Request.Method = "GET"; context.Request.Path = path;
                    context.Request.Scheme = "https"; context.Request.Host = new("admin.example.test");
                    context.Request.Headers.Cookie = cookie;
                    context.Request.Headers["Authorization"] = "";
                });
                Assert.Equal(401, response.Response.StatusCode);
                Assert.Equal("application/json; charset=utf-8", response.Response.ContentType);
            }
            else
            await Rejected(await Api(client, path, cookie, authorization: headers), 401, "unauthorized");
        }
        Assert.Equal(0, probe.Reads);
        await Rejected(await Api(client, "/api/admin/session-probe", "adminAuthToken=" + authority.LegacyBearer()), 401, "unauthorized");
    }

    [Theory]
    [InlineData("anonymous")][InlineData("missing")][InlineData("expired")][InlineData("issuer")]
    [InlineData("subject-empty")][InlineData("subject-missing")][InlineData("sub-duplicate")]
    [InlineData("iss-duplicate")]
    public async Task SessionApi_InvalidIdentityTicketOrAccessTokenFailsClosed(string defect)
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        var time = new ManualOidcTime();
        using var factory = SessionFactory(authority, probe, time);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        var (key, _) = await Stored(factory, cookie);
        if (defect == "anonymous") cookie = "";
        else if (defect == "missing") await factory.Services.GetRequiredService<ITicketStore>().RemoveAsync(key, CancellationToken.None);
        else if (defect == "expired")
            // The session never outlives the access token; the store reclaims it on read, and
            // an expired session is indistinguishable from an absent one by design.
            time.Advance(TimeSpan.FromMinutes(16));
        else
            cookie = defect switch
            {
                "issuer" => await ForgedTicket(factory, issuer: "https://wrong.example.test"),
                "subject-empty" => await ForgedTicket(factory, subject: " "),
                "subject-missing" => await ForgedTicket(factory, subject: null),
                "sub-duplicate" => await ForgedTicket(factory, extraClaims: new Claim("sub", "duplicate-subject")),
                _ => await ForgedTicket(factory, extraClaims: new Claim("iss", OidcTestAuthority.Issuer))
            };
        foreach (var path in new[] { "/api/admin/session-probe", "/api/auth/csrf" })
            await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        await Rejected(await Api(client, "/api/admin/oss-audit/records/1/resolve", cookie, "POST"), 401, "unauthorized");
        Assert.Equal(0, probe.Reads);
        using var scope = factory.Services.CreateScope();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Cookie = cookie;
        var result = await context.RequestServices.GetRequiredService<AdminSessionAccessor>().AuthenticateAsync(context);
        Assert.Equal(401, result.StatusCode);
        Assert.Null(result.AccessToken);
        Assert.Null(result.Principal);
        probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SessionApi_RestartCannotReviveServerTicket()
    {
        using var authority = new OidcTestAuthority();
        using var original = SessionFactory(authority, new SessionBusinessProbe());
        using var client = Browser(original);
        var cookie = await LoginSession(client, authority);
        var probe = new SessionBusinessProbe();
        using var restarted = SessionFactory(authority, probe);
        using var browser = Browser(restarted);
        await Rejected(await Api(browser, "/api/admin/session-probe", cookie), 401, "unauthorized");
        await Rejected(await Api(browser, "/api/auth/csrf", cookie), 401, "unauthorized");
        Assert.Equal(0, probe.Reads);
    }

    [Fact]
    public async Task SessionApi_CurrentWhitelistRemovalAndEmptyListDenyEvenWithForgedRole()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        Assert.Equal(HttpStatusCode.OK, (await Api(client, "/api/admin/session-probe", cookie)).StatusCode);
        // A forged role claim on the ticket principal never bypasses the whitelist.
        var forged = await ForgedTicket(factory, extraClaims: new Claim(ClaimTypes.Role, "admin"));
        foreach (var whitelist in new[] { new List<string> { "someone-else" }, new List<string>() })
        {
            probe.Admins.CurrentValue.AdminUserIds = whitelist;
            foreach (var path in new[] { "/api/admin/session-probe", "/api/auth/csrf" })
                await Rejected(await Api(client, path, forged), 403, "forbidden");
            await Rejected(await Api(client, "/api/admin/oss-audit/records/1/resolve", forged, "POST"), 403, "forbidden");
        }
        Assert.Equal(1, probe.Reads);
        probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("POST")][InlineData("PUT")][InlineData("PATCH")][InlineData("DELETE")]
    public async Task SessionApi_AllUnsafeMethodsRequireHeaderAndBoundCookieBeforeBusiness(string method)
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginSession(client, authority);
        var csrf = await Csrf(client, session);
        var cookies = session + "; " + csrf.Cookie;
        var path = "/api/admin/session-probe";
        await Rejected(await Api(client, path, cookies, method), 400, "csrf_invalid");
        await Rejected(await Api(client, path, cookies, method, csrf: ["wrong"]), 400, "csrf_invalid");
        await Rejected(await Api(client, path, cookies, method, csrf: [csrf.Token, csrf.Token]), 400, "csrf_invalid");
        await Rejected(await Api(client, path, session, method, csrf: [csrf.Token]), 400, "csrf_invalid");
        var otherCsrf = await Csrf(client, session);
        await Rejected(await Api(client, path, session + "; " + otherCsrf.Cookie, method, csrf: [csrf.Token]), 400, "csrf_invalid");
        await Rejected(await Api(client, path, cookies, method, body: new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = csrf.Token })), 400, "csrf_invalid");
        Assert.Equal(0, probe.Writes);
        Assert.Equal(HttpStatusCode.OK, (await Api(client, path, cookies, method, csrf: [csrf.Token])).StatusCode);
        Assert.Equal(1, probe.Writes);
    }

    [Fact]
    public async Task SessionApi_CsrfCannotBeReusedByDifferentSubjectAndConcurrentRequestsDoNotReplay()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var first = await LoginSession(client, authority);
        var csrf = await Csrf(client, first);
        var second = await ForgedTicket(factory, subject: "fake-second");
        probe.Admins.CurrentValue.AdminUserIds.Add("fake-second");
        await Rejected(await Api(client, "/api/admin/session-probe", second + "; " + csrf.Cookie, "DELETE", csrf: [csrf.Token]), 400, "csrf_invalid");
        var valid = first + "; " + csrf.Cookie;
        var requests = Enumerable.Range(0, 8).Select(i => Api(client, "/api/admin/session-probe", valid, "POST", csrf: [i % 2 == 0 ? csrf.Token : "wrong"]));
        var responses = await Task.WhenAll(requests);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(4, probe.Writes);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(client, "/api/admin/session-probe", valid, "POST", csrf: [csrf.Token], cancellation: cancelled.Token));
        Assert.Equal(4, probe.Writes);
    }

    [Fact]
    public async Task SessionApi_RetiresPasswordBeforeBindingKeepsCallbacksSpaHealthAndRejectsLegacyCredentials()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        var root = CreateTempContentRoot(true);
        try
        {
            using var factory = SessionFactory(authority, probe, root: root);
            using var client = Browser(factory);
            foreach (var content in new HttpContent?[] { null, new StringContent("invalid json"), JsonContent.Create(new { username = "fake", password = "fake" }) })
                await Rejected(await Api(client, "/api/auth/login", method: "POST", body: content), 410, "legacy_login_disabled");
            foreach (var path in new[] { "/api/auth/login/", "/API/AUTH/LOGIN", "/API/AUTH/LOGIN/" })
                await Rejected(await Api(client, path, method: "POST", body: JsonContent.Create(new { username = "fake", password = "fake" })), 410, "legacy_login_disabled");
            await Rejected(await Api(client, "/api/auth/login", method: "POST", authorization: ["Bearer invalid"],
                body: JsonContent.Create(new { username = "fake", password = "fake" })), 410, "legacy_login_disabled");
            Assert.Equal(0, probe.BearerAuthentications);
            var legacy = "adminAuthToken=" + authority.LegacyBearer();
            foreach (var path in new[] { "/api/auth/csrf/", "/API/AUTH/CSRF/", "/API/ADMIN/SESSION-PROBE/" })
                await Rejected(await Api(client, path, legacy), 401, "unauthorized");
            Assert.Equal(0, authority.Redeems);
            Assert.Equal(0, probe.Identity.Calls);
            foreach (var path in new[] { "/", "/students", "/health/live" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
            // Readiness now reports the real evidence source (#54): the fixture database is
            // reachable and this process completed its initialization, so ready answers 200.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
            var callback = await client.PostAsJsonAsync("/api/auth/callback", new { userId = "fake-subject" });
            Assert.Equal("{\"roles\":[\"admin\"]}", await callback.Content.ReadAsStringAsync());
            // All active proxy surfaces reject retired browser credentials before outbound I/O.
            var bearerBefore = probe.BearerAuthentications;
            foreach (var path in new[] { "/api/identity/admin/users", "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
                await Rejected(await Api(client, path, legacy, authorization: ["Bearer invalid"]), 401, "unauthorized");
            Assert.Equal(bearerBefore, probe.BearerAuthentications);
            Assert.Equal(0, probe.Identity.Calls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("single", "Student")][InlineData("single", "Homework")][InlineData("single", "Mistake")]
    [InlineData("batch", "Student")][InlineData("batch", "Homework")][InlineData("batch", "Mistake")]
    public async Task SessionApi_OssResolvePreservesCsrfAndUsesSharedReferenceGate(string mode, string source)
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginSession(client, authority);
        var csrf = await Csrf(client, session);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var path = "uploads/session-test-" + Guid.NewGuid().ToString("N") + ".jpg";
        var record = new OssAuditRecord { ObjectPath = path, Bucket = "uploads", Status = 0, CreatedAt = 1 };
        db.OssAuditRecords.Add(record); await db.SaveChangesAsync();
        var route = mode == "single" ? $"/api/admin/oss-audit/records/{record.Id}/resolve" : "/api/admin/oss-audit/records/batch-resolve";
        HttpContent? Body() => mode == "batch" ? JsonContent.Create(new { ids = new[] { record.Id } }) : null;
        try
        {
            // Actual session/controller/database pipeline: rejected requests never collect or delete.
            await Rejected(await Api(client, route, method: "POST", body: Body()), 401, "unauthorized");
            probe.Admins.CurrentValue.AdminUserIds.Clear();
            await Rejected(await Api(client, route, session, "POST", body: Body()), 403, "forbidden");
            probe.Admins.CurrentValue.AdminUserIds.Add("fake-subject");
            await Rejected(await Api(client, route, session, "POST", body: Body()), 400, "csrf_invalid");
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            probe.Student.Verify(s => s.GetAllUploadRecordsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            probe.References.Verify(c => c.CollectAsync(It.IsAny<CancellationToken>()), Times.Never);
            var cookies = session + "; " + csrf.Cookie;
            if (source == "Student") probe.StudentPaths.Add(path);
            if (source == "Homework") probe.Homework.Paths = [path];
            if (source == "Mistake") probe.MistakePaths.Add(path);
            using var referenced = await Api(client, route, cookies, "POST", csrf: [csrf.Token], body: Body());
            Assert.Equal(HttpStatusCode.Conflict, referenced.StatusCode);
            Assert.Equal("cleanup_not_authorized", (await referenced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorKind").GetString());
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            probe.StudentPaths.Clear(); probe.Homework.Paths = []; probe.MistakePaths.Clear();
            probe.ReferenceFailure = source; probe.Homework.Unavailable = source == "Homework";
            using var unreachable = await Api(client, route, cookies, "POST", csrf: [csrf.Token], body: Body());
            Assert.Equal(HttpStatusCode.BadGateway, unreachable.StatusCode);
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            Assert.True(await db.OssAuditRecords.AsNoTracking().AnyAsync(r => r.Id == record.Id));
            probe.ReferenceFailure = null; probe.Homework.Unavailable = false;
            using var success = await Api(client, route, cookies, "POST", csrf: [csrf.Token], body: Body());
            Assert.Equal(HttpStatusCode.Conflict, success.StatusCode);
            probe.References.Verify(c => c.CollectAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            Assert.True(await db.OssAuditRecords.AsNoTracking().AnyAsync(r => r.Id == record.Id));
        }
        finally
        {
            await db.OssAuditRecords.Where(r => r.Id == record.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task SessionApi_AuditTriggerCannotRunBeforeCsrfValidation()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginSession(client, authority);
        var csrf = await Csrf(client, session);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var before = await db.OssAuditRuns.CountAsync();
        await Rejected(await Api(client, "/api/admin/oss-audit/trigger", session, "POST"), 400, "csrf_invalid");
        Assert.Equal(before, await db.OssAuditRuns.CountAsync());
        probe.Oss.Verify(s => s.ListObjectsAsync(It.IsAny<string>()), Times.Never);
        // A running sentinel lets us demonstrate controller admission without starting background work.
        var run = new OssAuditRun { StartedAt = 1, Status = 0, TriggerType = "manual" };
        db.OssAuditRuns.Add(run); await db.SaveChangesAsync();
        try
        {
            using var admitted = await Api(client, "/api/admin/oss-audit/trigger", session + "; " + csrf.Cookie, "POST", csrf: [csrf.Token]);
            Assert.Equal(HttpStatusCode.BadRequest, admitted.StatusCode);
            Assert.Contains("审计正在运行中", await admitted.Content.ReadAsStringAsync());
        }
        finally { await db.OssAuditRuns.Where(r => r.Id == run.Id).ExecuteDeleteAsync(); }
    }

    [Theory]
    [InlineData("Enabled")][InlineData("UseSessionForAdminApi")][InlineData("UseSessionForLogout")]
    public void SessionApi_LegacyFalseRejectsBeforeHost(string key)
    {
        using var factory = CreateFactory(settings: new Dictionary<string, string?> { ["AdminOidc:" + key] = "false" });
        Assert.Equal("AdminOidc:" + key, Assert.Throws<InvalidOperationException>(() => factory.Services).Message);
    }

    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(true, true)]
    public async Task PasswordLogin_IsAlwaysRetiredBeforeAuthenticationOrBodyRead(bool enabled, bool sessionApi)
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        var logs = new OidcLogCapture();
        void Configure(IServiceCollection services)
        {
            services.RemoveAll<ILoggerFactory>();
            services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace);
            services.AddSingleton<ILoggerFactory>(provider => new LoggerFactory([logs], provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>()));
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => probe.Identity);
            services.Configure<JwtBearerOptions>("Bearer", options => options.Events.OnMessageReceived = context =>
            { Interlocked.Increment(ref probe.BearerAuthentications); return Task.CompletedTask; });
        }
        using var factory = enabled ? OidcFactory(authority, configure: Configure, sessionApi: sessionApi)
            : CreateFactory(configureTestServices: Configure);
        using var client = Browser(factory);
        foreach (var path in new[] { "/api/auth/login", "/api/auth/login/", "/API/AUTH/LOGIN/" })
        foreach (var headers in new string[]?[] { null, ["Bearer " + authority.LegacyBearer()], ["Basic invalid"], ["Bearer a", "Bearer b"] })
        foreach (var kind in new[] { "empty", "json", "malformed", "content-type" })
        {
            HttpContent? body = kind switch
            {
                "empty" => null, "json" => JsonContent.Create(new { username = "sensitive-canary", password = "sensitive-canary" }),
                "malformed" => new StringContent("{sensitive-canary", System.Text.Encoding.UTF8, "application/json"),
                _ => new StringContent("sensitive-canary", System.Text.Encoding.UTF8, "text/plain")
            };
            using var response = await Api(client, path, "adminAuthToken=" + authority.LegacyBearer(), "POST", authorization: headers, body: body);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal("no-cache", string.Join(",", response.Headers.Pragma));
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
            await Rejected(response, 410, "legacy_login_disabled");
        }
        var unread = await factory.Server.SendAsync(context =>
        {
            context.Request.Path = "/API/AUTH/LOGIN/"; context.Request.Method = "POST";
            context.Request.Headers["Authorization"] = "";
            context.Request.ContentType = "application/json"; context.Request.ContentLength = 123;
            context.Request.Body = new UnreadPasswordStream();
        });
        Assert.Equal(410, unread.Response.StatusCode);
        Assert.Equal(0, probe.BearerAuthentications);
        Assert.Equal(0, probe.Identity.Calls);
        Assert.Equal(0, authority.Redeems);
        Assert.DoesNotContain("sensitive-canary", string.Join("\n", logs.Messages));
        using var callback = await client.PostAsJsonAsync("/api/auth/callback", new { userId = "not-an-admin" });
        Assert.Equal("{\"roles\":[]}", await callback.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SessionApi_AbsentLegacyKeysStillRequireSessionForCsrf()
    {
        using var factory = CreateFactory();
        using var client = Browser(factory);
        foreach (var path in new[] { "/api/auth/csrf", "/api/auth/csrf/", "/API/AUTH/CSRF/" })
            await Rejected(await client.GetAsync(path), 401, "unauthorized");
    }
}

public sealed class SessionBusinessProbe
{
    public int Reads;
    public int Writes;
    internal int BearerAuthentications;
    internal readonly MutableAdmins Admins = new();
    internal readonly Mock<IOssService> Oss = new();
    internal readonly Mock<IStorageReferenceCollector> References = new();
    internal readonly Mock<IStudentHttpClient> Student = new();
    internal readonly Mock<IMistakeHttpClient> Mistake = new();
    internal readonly HomeworkStub Homework = new();
    internal readonly IdentityStub Identity = new();
    internal string? ReferenceFailure;
    internal readonly List<string> StudentPaths = [];
    internal readonly List<string> MistakePaths = [];
    public SessionBusinessProbe()
    {
        References.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            if (ReferenceFailure is not null) throw new HttpRequestException("fake.unavailable");
            var captured = DateTimeOffset.UtcNow;
            var paths = new Dictionary<string, string[]>
            { ["student"] = StudentPaths.ToArray(), ["mistake"] = MistakePaths.ToArray(), ["homework"] = Homework.Paths };
            var snapshots = paths.Select(pair =>
            {
                var keys = StorageReferenceContract.CanonicalKeys(pair.Value);
                return new StorageReferenceMetadata(StorageReferenceContract.Version, pair.Key, Guid.NewGuid(), captured, captured.AddMinutes(10),
                    StorageReferenceContract.Coverage(pair.Key), keys.Length, 500, Math.Max(1, (keys.Length + 499) / 500), StorageReferenceContract.Digest(pair.Key, keys), false);
            }).ToArray();
            return new StorageReferenceCollection(paths.Values.SelectMany(keys => keys).ToHashSet(StringComparer.Ordinal), snapshots);
        });
        Student.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedUrlResult { Url = "https://images.example.test/test.jpg" });
        Student.Setup(s => s.GetAllUploadRecordsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ReferenceFailure == "Student" ? throw new HttpRequestException("fake.unavailable")
                : new UploadRecordsPage { Items = StudentPaths.Select(p => new UploadRecordDto { ImageEntries = [new ImageEntryDto { Path = p }] }).ToList() });
        Mistake.Setup(s => s.GetMistakeItemListAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MistakeReviewStatus>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ReferenceFailure == "Mistake" ? throw new HttpRequestException("fake.unavailable")
                : new MistakeItemPageResult { Items = MistakePaths.Select(p => new MistakeItemDto { SourceRegions = [new MistakeSourceRegionDto { SourceImagePath = p }] }).ToList() });
        Oss.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
    }
}
internal sealed class IdentityStub : HttpMessageHandler
{
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
        { success = true, accessToken = "fictitious-legacy-token", refreshToken = "", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() }) });
    }
}
internal sealed class MutableAdmins : IOptionsMonitor<AdminPortalOptions>
{
    public AdminPortalOptions CurrentValue { get; } = new() { AdminUserIds = ["FAKE-SUBJECT"] };
    public AdminPortalOptions Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<AdminPortalOptions, string?> listener) => null;
}
internal sealed class HomeworkStub : HttpMessageHandler
{
    internal bool Unavailable;
    internal string[] Paths = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(Unavailable ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new { paths = Paths, hasMore = false } }) });
}
[ApiController, Route("api/admin/session-probe"), Authorize]
public sealed class SessionProbeController(SessionBusinessProbe probe) : ControllerBase
{
    [HttpGet, HttpHead]
    public IActionResult Read() { Interlocked.Increment(ref probe.Reads); return Ok(); }
    [HttpPost, HttpPut, HttpPatch, HttpDelete]
    public IActionResult Write() { Interlocked.Increment(ref probe.Writes); return Ok(); }
}
[ApiController, Route("api/admin/session-fallback")]
public sealed class SessionFallbackController(SessionBusinessProbe probe) : ControllerBase
{
    [HttpGet]
    public IActionResult Read() { Interlocked.Increment(ref probe.Reads); return Ok(); }
}

internal sealed class UnreadPasswordStream : MemoryStream
{
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Password body must not be read");
    public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Password body must not be read");
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Password body must not be read");
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new InvalidOperationException("Password body must not be read");
}
