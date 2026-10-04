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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
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

    private static async Task<(string Key, AuthenticationTicket Ticket)> Stored(WebApplicationFactory<Program> factory, string cookie)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AdminOidcSettings.SessionScheme);
        var reference = options.TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!;
        var key = Assert.Single(reference.Principal.Claims).Value;
        return (key, (await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key))!);
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
            Assert.DoesNotContain(OidcTestAuthority.AccessToken, await response.Content.ReadAsStringAsync());
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
        foreach (var path in new[] { "/api/identity/admin/users" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api(client, path, cookie, "GET")).StatusCode);
        foreach (var path in new[] { "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
            await Rejected(await Api(client, path, cookie), 503, "session_portal_proxy_disabled");
        await Rejected(await Api(client, "/api/auth/logout", cookie, "POST"), 503, "session_logout_disabled");
        // The accessor returns the server token, without leaking it through formatting.
        using var scope = factory.Services.CreateScope();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Cookie = cookie;
        var session = await context.RequestServices.GetRequiredService<AdminSessionAccessor>().AuthenticateAsync(context);
        Assert.Equal(OidcTestAuthority.AccessToken, session.AccessToken);
        Assert.DoesNotContain(OidcTestAuthority.AccessToken, session.ToString());
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
    [InlineData("subject-stamp")][InlineData("issuer-stamp")][InlineData("sub-empty")]
    [InlineData("sub-duplicate")][InlineData("iss-duplicate")]
    [InlineData("token-missing")][InlineData("deadline-invalid")][InlineData("deadline-offset-missing")]
    [InlineData("deadline-exact")][InlineData("deadline-past")]
    public async Task SessionApi_InvalidIdentityTicketOrAccessTokenFailsClosed(string defect)
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        var time = new ManualOidcTime();
        using var factory = SessionFactory(authority, probe, time);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        var (key, ticket) = await Stored(factory, cookie);
        var store = factory.Services.GetRequiredService<MemoryTicketStore>();
        if (defect == "anonymous") cookie = "";
        else if (defect == "missing") await store.RemoveAsync(key);
        else if (defect == "expired") time.Advance(TimeSpan.FromHours(8));
        else
        {
            var claims = ticket.Principal.Claims.ToList();
            if (defect == "issuer") { claims.RemoveAll(c => c.Type == "iss"); claims.Add(new("iss", "https://wrong.example.test")); }
            if (defect == "sub-empty") { claims.RemoveAll(c => c.Type == "sub"); claims.Add(new("sub", " ")); }
            if (defect == "sub-duplicate") claims.Add(new("sub", "fake-subject"));
            if (defect == "iss-duplicate") claims.Add(new("iss", OidcTestAuthority.Issuer));
            ticket = new(new ClaimsPrincipal(new ClaimsIdentity(claims, AdminOidcSettings.SessionScheme)), ticket.Properties, AdminOidcSettings.SessionScheme);
            if (defect == "subject-stamp") ticket.Properties.Items["oidc.subject"] = "wrong";
            if (defect == "issuer-stamp") ticket.Properties.Items.Remove("oidc.issuer");
            if (defect == "token-missing") ticket.Properties.StoreTokens(ticket.Properties.GetTokens().Where(t => t.Name != "access_token"));
            if (defect.StartsWith("deadline")) ticket.Properties.UpdateTokenValue("expires_at", defect switch
            {
                "deadline-invalid" => "tomorrow", "deadline-offset-missing" => "2099-01-01T00:00:00.0000000",
                "deadline-exact" => time.GetUtcNow().ToString("o"), _ => time.GetUtcNow().AddSeconds(-1).ToString("o")
            });
            await store.RenewAsync(key, ticket);
        }
        foreach (var path in new[] { "/api/admin/session-probe", "/api/auth/csrf" })
            await Rejected(await Api(client, path, cookie), 401, defect is "deadline-exact" or "deadline-past" ? "reauthentication_required" : "unauthorized");
        await Rejected(await Api(client, "/api/admin/oss-audit/records/1/resolve", cookie, "POST"), 401, defect is "deadline-exact" or "deadline-past" ? "reauthentication_required" : "unauthorized");
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
    public async Task SessionApi_RestartWithSameProtectionKeysCannotReviveServerTicket()
    {
        using var authority = new OidcTestAuthority();
        var protection = new EphemeralDataProtectionProvider();
        using var original = SessionFactory(authority, new SessionBusinessProbe(), protection: protection);
        using var client = Browser(original);
        var cookie = await LoginSession(client, authority);
        var probe = new SessionBusinessProbe();
        using var restarted = SessionFactory(authority, probe, protection: protection);
        using var browser = Browser(restarted);
        var options = restarted.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AdminOidcSettings.SessionScheme);
        Assert.NotNull(options.TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..]));
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
        var (key, ticket) = await Stored(factory, cookie);
        ticket = new(new ClaimsPrincipal(new ClaimsIdentity(ticket.Principal.Claims.Append(new Claim(ClaimTypes.Role, "admin")), AdminOidcSettings.SessionScheme)), ticket.Properties, AdminOidcSettings.SessionScheme);
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        foreach (var whitelist in new[] { new List<string> { "someone-else" }, new List<string>() })
        {
            probe.Admins.CurrentValue.AdminUserIds = whitelist;
            foreach (var path in new[] { "/api/admin/session-probe", "/api/auth/csrf" })
                await Rejected(await Api(client, path, cookie), 403, "forbidden");
            await Rejected(await Api(client, "/api/admin/oss-audit/records/1/resolve", cookie, "POST"), 403, "forbidden");
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
        var second = await LoginSession(client, authority);
        var (key, ticket) = await Stored(factory, second);
        ticket.Properties.Items["oidc.subject"] = "fake-second";
        ticket = new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("iss", OidcTestAuthority.Issuer), new Claim("sub", "fake-second")], AdminOidcSettings.SessionScheme)), ticket.Properties, AdminOidcSettings.SessionScheme);
        probe.Admins.CurrentValue.AdminUserIds.Add("fake-second");
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
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
    public async Task SessionApi_RetiresPasswordBeforeBindingKeepsCallbacksSpaHealthAndLegacyProxyScheme()
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
            var selector = factory.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.PolicySchemeOptions>>().Get("AdminApiAuthentication");
            foreach (var path in new[] { "/api/identity/admin/users" })
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext(); context.Request.Path = path;
                Assert.Equal("Bearer", selector.ForwardDefaultSelector!(context));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("single", "Student")][InlineData("single", "Homework")][InlineData("single", "Mistake")]
    [InlineData("batch", "Student")][InlineData("batch", "Homework")][InlineData("batch", "Mistake")]
    public async Task SessionApi_OssDeleteStillRechecksEverySourceAndRejectsUnreachable(string mode, string source)
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
            // Actual controller/database/object-store routes; rejected requests never touch references or DeleteAsync.
            await Rejected(await Api(client, route, method: "POST", body: Body()), 401, "unauthorized");
            probe.Admins.CurrentValue.AdminUserIds.Clear();
            await Rejected(await Api(client, route, session, "POST", body: Body()), 403, "forbidden");
            probe.Admins.CurrentValue.AdminUserIds.Add("fake-subject");
            await Rejected(await Api(client, route, session, "POST", body: Body()), 400, "csrf_invalid");
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            probe.Student.Verify(s => s.GetAllUploadRecordsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            var cookies = session + "; " + csrf.Cookie;
            if (source == "Student") probe.StudentPaths.Add(path);
            if (source == "Homework") probe.Homework.Paths = [path];
            if (source == "Mistake") probe.MistakePaths.Add(path);
            using var referenced = await Api(client, route, cookies, "POST", csrf: [csrf.Token], body: Body());
            Assert.Equal(mode == "single" ? HttpStatusCode.BadRequest : HttpStatusCode.OK, referenced.StatusCode);
            if (mode == "batch") Assert.Equal(0, (await referenced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resolvedCount").GetInt32());
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            probe.StudentPaths.Clear(); probe.Homework.Paths = []; probe.MistakePaths.Clear();
            probe.ReferenceFailure = source; probe.Homework.Unavailable = source == "Homework";
            using var unreachable = await Api(client, route, cookies, "POST", csrf: [csrf.Token], body: Body());
            Assert.Equal(HttpStatusCode.BadGateway, unreachable.StatusCode);
            probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
            Assert.True(await db.OssAuditRecords.AsNoTracking().AnyAsync(r => r.Id == record.Id));
            probe.ReferenceFailure = null; probe.Homework.Unavailable = false;
            using var success = await Api(client, route, cookies, "POST", csrf: [csrf.Token], body: Body());
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            probe.Oss.Verify(s => s.DeleteAsync(path), Times.Once);
            Assert.False(await db.OssAuditRecords.AsNoTracking().AnyAsync(r => r.Id == record.Id));
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

    [Fact]
    public async Task SessionApi_DefaultFalseRejectsLegacyHeaderCookieAndLogout()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = OidcFactory(authority, sessionApi: false, configure: services =>
        {
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => probe.Identity);
            services.Configure<JwtBearerOptions>("Bearer", options => options.Events.OnMessageReceived = context =>
            { Interlocked.Increment(ref probe.BearerAuthentications); return Task.CompletedTask; });
        });
        using var client = Browser(factory);
        foreach (var path in new[] { ProtectedApiRoute, "/API/ADMIN/ENUM-OPTIONS/", "/api/admin/image?path=uploads/test.jpg", "/api/auth/csrf/", "/API/AUTH/SESSION/" })
        foreach (var auth in new string[]?[] { null, ["Bearer " + authority.LegacyBearer()], ["Bearer invalid"], ["a", "b"] })
            await Rejected(await Api(client, path, "adminAuthToken=" + authority.LegacyBearer(), authorization: auth), 503, "session_api_disabled");
        foreach (var path in new[] { "/api/auth/logout", "/API/AUTH/LOGOUT/", "/api/auth/logout/csrf" })
            await Rejected(await Api(client, path, "adminAuthToken=" + authority.LegacyBearer(), "POST", authorization: ["Bearer " + authority.LegacyBearer()]), 503, "session_logout_disabled");
        Assert.Equal(0, probe.BearerAuthentications); Assert.Equal(0, probe.Identity.Calls); Assert.Equal(0, probe.Reads);
        // The merged password-retirement contract is independent of the disabled API/logout gates.
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "fake", password = "fake" });
        Assert.Equal(HttpStatusCode.Gone, login.StatusCode); Assert.Equal(0, probe.Identity.Calls);
        Assert.Equal("{\"error\":\"legacy_login_disabled\"}", await login.Content.ReadAsStringAsync());
        Assert.False(login.Headers.Contains("Set-Cookie"));
        probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
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
    public async Task SessionApi_DisabledCsrfAndInvalidEnableCombinationAreExplicit()
    {
        using var disabled = CreateFactory();
        using var client = Browser(disabled);
        foreach (var path in new[] { "/api/auth/csrf", "/api/auth/csrf/", "/API/AUTH/CSRF/" })
            await Rejected(await client.GetAsync(path), 503, "session_api_disabled");
        using var invalid = CreateFactory(settings: new Dictionary<string, string?> { ["AdminOidc:UseSessionForAdminApi"] = "true" });
        Assert.Equal("AdminOidc:UseSessionForAdminApi requires AdminOidc:Enabled", Assert.Throws<InvalidOperationException>(() => invalid.Services).Message);
    }
}

public sealed class SessionBusinessProbe
{
    public int Reads;
    public int Writes;
    internal int BearerAuthentications;
    internal readonly MutableAdmins Admins = new();
    internal readonly Mock<IOssService> Oss = new();
    internal readonly Mock<IStudentHttpClient> Student = new();
    internal readonly Mock<IMistakeHttpClient> Mistake = new();
    internal readonly HomeworkStub Homework = new();
    internal readonly IdentityStub Identity = new();
    internal string? ReferenceFailure;
    internal readonly List<string> StudentPaths = [];
    internal readonly List<string> MistakePaths = [];
    public SessionBusinessProbe()
    {
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
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Password body must not be read");
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Password body must not be read");
}
