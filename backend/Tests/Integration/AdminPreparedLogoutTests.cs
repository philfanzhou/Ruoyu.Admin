using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Tests.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    private WebApplicationFactory<Program> LogoutFactory(OidcTestAuthority authority, LogoutCapture upstream,
        ManualOidcTime? time = null, MutableAdmins? admins = null, Action<IServiceCollection>? configure = null, bool identityProxy = false, bool portalProxies = false)
        => OidcFactory(authority, time, services =>
        {
            services.AddHttpClient(AdminPreparedLogout.ClientName).ConfigurePrimaryHttpMessageHandler(() => upstream);
            if (admins is not null) services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
            configure?.Invoke(services);
        }, sessionApi: true, sessionLogout: true, identityProxy: identityProxy, portalProxies: portalProxies);
    private static async Task<(string Token, string Cookie)> LogoutCsrf(HttpClient client, string cookie)
    {
        using var response = await Api(client, "/api/auth/logout/csrf", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!,
            response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("adminCsrf=")).Split(';')[0]);
    }
    private static async Task<HttpResponseMessage> PostLogout(HttpClient client, string cookie, (string Token, string Cookie) csrf,
        CancellationToken cancellation = default) => await Api(client, "/api/auth/logout", cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token], cancellation: cancellation);

    [Theory]
    [InlineData(false, false, false, false)][InlineData(true, false, false, false)][InlineData(false, true, false, false)][InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)][InlineData(true, false, true, false)][InlineData(false, true, true, false)][InlineData(true, true, true, false)]
    [InlineData(false, false, false, true)][InlineData(true, false, false, true)][InlineData(false, true, false, true)][InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)][InlineData(true, false, true, true)][InlineData(false, true, true, true)][InlineData(true, true, true, true)]
    public async Task PreparedLogout_RevokesBeforeHttpWithExpiredAccessOrRemovedAdmin(bool expired, bool removedAdmin, bool identityProxy, bool portalProxies)
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture(); var admins = new MutableAdmins(); var time = new ManualOidcTime();
        var probe = new SessionBusinessProbe();
        var identity = new PortalSessionCapture(); var teacher = new PortalSessionCapture(); var assistant = new PortalSessionCapture();
        using var factory = LogoutFactory(authority, upstream, time, admins: admins, identityProxy: identityProxy, portalProxies: portalProxies, configure: services =>
        {
            services.AddControllers().AddApplicationPart(typeof(SessionProbeController).Assembly);
            services.AddSingleton(probe);
            services.Replace(ServiceDescriptor.Singleton(probe.Student.Object));
            services.Replace(ServiceDescriptor.Singleton(probe.Oss.Object));
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => identity);
            services.AddHttpClient("TeacherPortal").ConfigurePrimaryHttpMessageHandler(() => teacher);
            services.AddHttpClient("AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => assistant);
        });
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var (key, ticket) = await Stored(factory, cookie);
        // Prove the real migrated proxy paths and native image work before local revocation.
        using (var image = await Api(client, "/api/admin/image?path=uploads/test.jpg&size=small", cookie))
            Assert.Equal(HttpStatusCode.Redirect, image.StatusCode);
        Assert.Single(probe.Student.Invocations); probe.Student.Invocations.Clear();
        foreach (var (enabled, path, capture) in new[]
        {
            (identityProxy, "/api/identity/users", identity),
            (portalProxies, "/api/teacher-portal/admin/users", teacher),
            (portalProxies, "/api/assistant-portal/admin/users", assistant)
        })
        {
            if (enabled)
            {
                using var reachable = await Api(client, path, cookie);
                Assert.Equal(HttpStatusCode.OK, reachable.StatusCode);
                Assert.Equal("Bearer " + OidcTestAuthority.AccessToken, Assert.Single(capture.Requests).Headers["Authorization"]);
                capture.Requests.Clear();
            }
        }
        if (expired) ticket.Properties.UpdateTokenValue("expires_at", DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        if (removedAdmin) admins.CurrentValue.AdminUserIds.Clear();
        if (expired) time.Advance(TimeSpan.FromMinutes(6)); // Stored ID token is now beyond its original exp, but still a valid logout hint.
        if (expired || removedAdmin) await Rejected(await Api(client, "/api/admin/session-probe", cookie), removedAdmin ? 403 : 401, removedAdmin ? "forbidden" : "reauthentication_required");
        upstream.BeforeSend = async () => Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key));
        var csrf = await LogoutCsrf(client, cookie);
        using var response = await PostLogout(client, cookie, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean()); Assert.True(result.GetProperty("upstreamLogout").GetBoolean());
        Assert.Equal(OidcTestAuthority.Issuer + "/oauth2/logout?logout_handle=" + LogoutCapture.Handle, result.GetProperty("logoutUrl").GetString());
        Assert.DoesNotContain(authority.LastIdToken!, await response.Content.ReadAsStringAsync());
        var form = Assert.Single(upstream.Forms);
        Assert.Equal(new[] { "client_id", "client_secret", "id_token_hint", "post_logout_redirect_uri", "state" }, form.Keys.Order());
        Assert.Equal(OidcTestAuthority.ClientId, form["client_id"]); Assert.Equal(OidcTestAuthority.Secret, form["client_secret"]);
        Assert.Equal(authority.LastIdToken, form["id_token_hint"]); Assert.Equal("https://admin.example.test/api/auth/oidc/logout-callback", form["post_logout_redirect_uri"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", form["state"]); Assert.Empty(upstream.Headers.Single());
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, c => c.StartsWith("adminSession=;")); Assert.Contains(cookies, c => c.StartsWith("adminAuthToken=;") && c.Contains("samesite=strict"));
        var binding = cookies.Single(c => c.StartsWith("adminLogout."));
        foreach (var flag in new[] { "httponly", "secure", "samesite=lax", "path=/api/auth/oidc/logout-callback", "max-age=300" }) Assert.Contains(flag, binding.ToLowerInvariant());
        foreach (var path in new[] { "/api/admin/image?path=test", "/api/admin/session-probe", "/api/auth/csrf" })
            await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        foreach (var path in new[] { "/api/identity/users", "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api(client, path, cookie)).StatusCode);
        Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key));
        Assert.Empty(probe.Student.Invocations); Assert.Empty(probe.Oss.Invocations);
        Assert.Equal(0, probe.Reads); Assert.Equal(0, probe.Writes);
        Assert.Empty(identity.Requests); Assert.Empty(teacher.Requests); Assert.Empty(assistant.Requests);
        await Rejected(await PostLogout(client, cookie, csrf), 401, "unauthorized"); Assert.Single(upstream.Forms);
        var state = form["state"]; var callback = AdminOidcSettings.LogoutCallbackPath + "?state=" + state;
        await Rejected(await Api(client, callback), 400, "logout_callback_invalid");
        await Rejected(await Api(client, callback, binding.Split(';')[0].Split('=')[0] + "=" + new string('B', 43)), 400, "logout_callback_invalid");
        await Rejected(await Api(client, callback + "&state=" + state, binding.Split(';')[0]), 400, "logout_callback_invalid");
        using var complete = await Api(client, callback, binding.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode); Assert.Equal("/login?loggedOut=1", complete.Headers.Location!.OriginalString);
        Assert.Equal("no-referrer", complete.Headers.GetValues("Referrer-Policy").Single());
        await Rejected(await Api(client, callback, binding.Split(';')[0]), 400, "logout_callback_invalid");
    }

    [Fact]
    public async Task PreparedLogout_CsrfAndAuthorizationCannotRevokeAndBadTicketsCannotPrepare()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture(); var time = new ManualOidcTime();
        using var factory = LogoutFactory(authority, upstream, time); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie); var (key, ticket) = await Stored(factory, cookie);
        await Rejected(await Api(client, "/api/auth/logout", cookie, "POST"), 400, "csrf_invalid");
        await Rejected(await Api(client, "/api/auth/logout", cookie + "; " + csrf.Cookie, "POST", csrf: ["wrong"]), 400, "csrf_invalid");
        await Rejected(await Api(client, "/api/auth/logout", cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token, csrf.Token]), 400, "csrf_invalid");
        await Rejected(await Api(client, "/api/auth/logout", cookie, "POST", csrf: [csrf.Token]), 400, "csrf_invalid");
        foreach (var path in new[] { "/api/auth/logout", "/api/auth/logout/csrf" })
            foreach (var auth in new[] { new[] { "Basic wrong" }, new[] { "a", "b" }, new[] { "Bearer " + authority.LegacyBearer() } })
                await Rejected(await Api(client, path, cookie, path.EndsWith("csrf") ? "GET" : "POST", authorization: auth), 401, "unauthorized");
        var empty = await factory.Server.SendAsync(ctx =>
        { ctx.Request.Path = "/api/auth/logout"; ctx.Request.Method = "POST"; ctx.Request.Scheme = "https"; ctx.Request.Headers.Cookie = cookie; ctx.Request.Headers.Authorization = ""; });
        Assert.Equal(401, empty.Response.StatusCode);
        await Rejected(await Api(client, "/api/auth/logout", "adminSession=" + key, "POST"), 401, "unauthorized");
        Assert.NotNull(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key)); Assert.Empty(upstream.Forms);
        ticket.Properties.Items["oidc.subject"] = "wrong";
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        await Rejected(await Api(client, "/api/auth/logout/csrf", cookie), 401, "unauthorized"); Assert.Empty(upstream.Forms);
        cookie = await LoginSession(client, authority); csrf = await LogoutCsrf(client, cookie); time.Advance(TimeSpan.FromHours(8));
        using var expired = await PostLogout(client, cookie, csrf); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Contains(expired.Headers.GetValues("Set-Cookie"), c => c.StartsWith("adminSession=;")); Assert.Empty(upstream.Forms);
    }

    [Theory]
    [InlineData("503")][InlineData("json")][InlineData("duplicate")][InlineData("large")][InlineData("external")][InlineData("encoded")]
    [InlineData("unknown-field")][InlineData("relative-origin")][InlineData("extra-query")][InlineData("userinfo")][InlineData("fragment")][InlineData("redirect")][InlineData("timeout")]
    public async Task PreparedLogout_UpstreamFailuresNeverRestoreOrReleaseState(string defect)
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture { Defect = defect };
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie); var (key, _) = await Stored(factory, cookie);
        using var response = await PostLogout(client, cookie, csrf);
        Assert.Equal("{\"success\":true,\"upstreamLogout\":false}", await response.Content.ReadAsStringAsync());
        Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key));
        Assert.Equal(0, factory.Services.GetRequiredService<AdminLogoutStateStore>().Count); Assert.Single(upstream.Forms);
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("adminLogout."));
    }

    [Fact]
    public async Task PreparedLogout_DifferentSubjectCsrfIsRejectedWithoutRevocation()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture();
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var first = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, first);
        var second = await LoginSession(client, authority); var (key, ticket) = await Stored(factory, second);
        ticket.Properties.Items["oidc.subject"] = "second";
        ticket = new(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new("iss", OidcTestAuthority.Issuer), new("sub", "second")], AdminOidcSettings.SessionScheme)), ticket.Properties, AdminOidcSettings.SessionScheme);
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        await Rejected(await PostLogout(client, second, csrf), 400, "csrf_invalid");
        Assert.NotNull(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key)); Assert.Empty(upstream.Forms);
    }

    [Fact]
    public async Task PreparedLogout_CancelledHttpKeepsLocalRevocationAndDoesNotRetry()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture { Hold = true };
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie); var (key, _) = await Stored(factory, cookie);
        using var cancellation = new CancellationTokenSource(); var pending = PostLogout(client, cookie, csrf, cancellation.Token);
        await upstream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await upstream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key)); Assert.Single(upstream.Forms);
        // The server cleans pending state while unwinding, independent of whether the response can be written.
        for (var i = 0; i < 100 && factory.Services.GetRequiredService<AdminLogoutStateStore>().Count != 0; i++) await Task.Delay(10);
        Assert.Equal(0, factory.Services.GetRequiredService<AdminLogoutStateStore>().Count);
    }

    [Fact]
    public async Task PreparedLogout_FailedResponseReleaseCannotLeaveUsableCompletionState()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture();
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie); var (key, _) = await Stored(factory, cookie);
        using var scope = factory.Services.CreateScope();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https"; context.Request.Host = new("admin.example.test");
        context.Request.Path = "/api/auth/logout"; context.Request.Method = "POST";
        context.Request.Headers.Cookie = cookie + "; " + csrf.Cookie; context.Request.Headers["X-CSRF-TOKEN"] = csrf.Token;
        context.Response.Body = new ThrowingLogoutStream();
        await Assert.ThrowsAsync<IOException>(() => scope.ServiceProvider.GetRequiredService<AdminPreparedLogout>().Logout(context));
        Assert.Single(upstream.Forms); Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key));
        Assert.Equal(0, factory.Services.GetRequiredService<AdminLogoutStateStore>().Count);
    }

    [Fact]
    public async Task PreparedLogout_ConcurrentCachedAuthenticationHasExactlyOneWinner()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture(); var gate = new CachedLogoutGate();
        using var factory = LogoutFactory(authority, upstream, configure: services => services.AddSingleton<IStartupFilter>(gate)); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie);
        var (key, _) = await Stored(factory, cookie);
        gate.Arm(); // Only the two concurrent requests below enter the server-controlled barrier.
        var responses = await Task.WhenAll(PostLogout(client, cookie, csrf), PostLogout(client, cookie, csrf));
        Assert.Equal(2, gate.Authenticated);
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Single(upstream.Forms);
        Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key));
    }

    [Fact]
    public async Task PreparedLogout_StateExpiryCapacityMissingHintAndTraceSafety()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture(); var time = new ManualOidcTime();
        var logs = new OidcLogCapture(); var traces = new OidcTraceCapture();
        using var factory = LogoutFactory(authority, upstream, time, configure: services =>
        {
            services.RemoveAll<ILoggerFactory>(); services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace);
            services.AddSingleton<ILoggerFactory>(provider => new LoggerFactory([logs], provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>()));
            services.AddOpenTelemetry().WithTracing(builder => builder.AddProcessor(traces));
        });
        using var client = Browser(factory); var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie);
        using var response = await PostLogout(client, cookie, csrf); var state = upstream.Forms.Single()["state"];
        var binding = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("adminLogout.")).Split(';')[0];
        var capacityCookie = await LoginSession(client, authority); var capacityCsrf = await LogoutCsrf(client, capacityCookie);
        var missingCookie = await LoginSession(client, authority); var missingCsrf = await LogoutCsrf(client, missingCookie);
        time.Advance(TimeSpan.FromMinutes(5));
        await Rejected(await Api(client, AdminOidcSettings.LogoutCallbackPath + "?state=" + state, binding), 400, "logout_callback_invalid");
        var messages = string.Join("\n", logs.Messages.Concat(traces.Messages));
        foreach (var value in new[] { authority.LastIdToken!, OidcTestAuthority.Secret, LogoutCapture.Handle, state, binding.Split('=')[1] }) Assert.DoesNotContain(value, messages);
        var store = factory.Services.GetRequiredService<AdminLogoutStateStore>();
        for (var i = 0; i < AdminLogoutStateStore.Capacity; i++) Assert.NotNull(store.Create(AdminLogoutStateStore.RandomKey()));
        using var capacity = await PostLogout(client, capacityCookie, capacityCsrf); Assert.Equal("{\"success\":true,\"upstreamLogout\":false}", await capacity.Content.ReadAsStringAsync()); Assert.Single(upstream.Forms);
        time.Advance(TimeSpan.FromMinutes(5)); store.RemoveExpired(); Assert.Equal(0, store.Count);
        var (key, ticket) = await Stored(factory, missingCookie);
        ticket.Properties.StoreTokens(ticket.Properties.GetTokens().Where(t => t.Name != "id_token"));
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        using var missing = await PostLogout(client, missingCookie, missingCsrf); Assert.Equal("{\"success\":true,\"upstreamLogout\":false}", await missing.Content.ReadAsStringAsync()); Assert.Single(upstream.Forms);
        Assert.Null(await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(key));
    }
}

internal sealed class CachedLogoutGate : IStartupFilter
{
    internal int Authenticated;
    private int _armed;
    internal void Arm() => Volatile.Write(ref _armed, 1);
    private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, onward) =>
        {
            // Cache authentication for every request; no user-controlled path/method gates it.
            var result = await context.AuthenticateAsync(AdminOidcSettings.SessionScheme);
            if (Volatile.Read(ref _armed) != 0)
            {
                Assert.True(result.Succeeded);
                if (Interlocked.Increment(ref Authenticated) == 2) _both.TrySetResult();
                await _both.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await onward();
        });
        next(app);
    };
}
internal sealed class LogoutCapture : HttpMessageHandler
{
    internal const string Handle = "HHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHHH";
    internal readonly System.Collections.Concurrent.ConcurrentQueue<Dictionary<string, string>> Forms = new();
    internal readonly System.Collections.Concurrent.ConcurrentQueue<Dictionary<string, string>> Headers = new();
    internal string? Defect;
    internal bool Hold;
    internal Func<Task>? BeforeSend;
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(OidcTestAuthority.Issuer + "/oauth2/logout/requests", request.RequestUri!.AbsoluteUri); Assert.Equal(HttpMethod.Post, request.Method);
        Forms.Enqueue(QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.Value.ToString()));
        Headers.Enqueue(request.Headers.ToDictionary(p => p.Key, p => string.Join(",", p.Value)));
        if (BeforeSend is not null) await BeforeSend(); Entered.TrySetResult();
        if (Hold)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        if (Defect == "timeout") throw new TaskCanceledException("fake.timeout-canary");
        var uri = Defect switch
        {
            "relative-origin" => "//identity.example.test/oauth2/logout?logout_handle=" + Handle,
            "external" => "https://external.example/oauth2/logout?logout_handle=" + Handle,
            "encoded" => "/oauth2/logout?logout_handle=%48" + Handle[1..],
            "extra-query" => "/oauth2/logout?logout_handle=" + Handle + "&state=wrong",
            "userinfo" => "https://user@identity.example.test/oauth2/logout?logout_handle=" + Handle,
            "fragment" => "/oauth2/logout?logout_handle=" + Handle + "#fragment",
            _ => "/oauth2/logout?logout_handle=" + Handle
        };
        var body = Defect switch
        {
            "unknown-field" => JsonSerializer.Serialize(new { logout_uri = uri, unsupported = true }),
            "json" => "invalid-json", "duplicate" => "{\"logout_uri\":\"" + uri + "\",\"logout_uri\":\"" + uri + "\"}",
            "large" => new string('X', 4097), _ => JsonSerializer.Serialize(new { logout_uri = uri })
        };
        var response = new HttpResponseMessage(Defect == "503" ? HttpStatusCode.ServiceUnavailable : Defect == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        if (Defect == "redirect") response.Headers.Location = new Uri("https://external.example/should-not-follow");
        return response;
    }
}

internal sealed class ThrowingLogoutStream : MemoryStream
{
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => ValueTask.FromException(new IOException("fake.output_failure"));
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Task.FromException(new IOException("fake.output_failure"));
}
