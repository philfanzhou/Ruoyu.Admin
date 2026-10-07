using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Tests.Authentication;
using Microsoft.AspNetCore.Builder;
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
            // One composite backchannel serves the whole package client: Discovery, JWKS, and
            // the token endpoint come from the authority; the logout preparation is captured.
            services.AddHttpClient(SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SplitLogoutHandler(authority, upstream));
            if (admins is not null) services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
            configure?.Invoke(services);
        }, sessionApi: true, sessionLogout: true, identityProxy: identityProxy, portalProxies: portalProxies);
    private static async Task<(string Token, string Cookie)> LogoutCsrf(HttpClient client, string cookie)
    {
        using var response = await Api(client, AdminOidcSettings.HostedLoginPrefix + "/csrf", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!,
            response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("adminCsrf=")).Split(';')[0]);
    }
    private static Task<HttpResponseMessage> PostLogout(HttpClient client, string cookie, (string Token, string Cookie) csrf,
        CancellationToken cancellation = default) => Api(client, AdminOidcSettings.LogoutPath, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token], cancellation: cancellation);

    [Fact]
    public async Task PreparedLogout_NonPostNeverRevokesOrPrepares()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture();
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var (key, _) = await Stored(factory, cookie);
        // The package maps POST only; every other method answers the framework's 405.
        foreach (var method in new[] { "GET", "HEAD", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE" })
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Api(client, AdminOidcSettings.LogoutPath, cookie, method)).StatusCode);
        var store = factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>();
        Assert.NotNull(await store.RetrieveAsync(key, CancellationToken.None));
        Assert.Empty(upstream.Forms);
        using var missingCsrf = await Api(client, AdminOidcSettings.LogoutPath, cookie, "POST");
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal("{\"outcome\":\"csrf_rejected\"}", await missingCsrf.Content.ReadAsStringAsync());
        Assert.Contains("no-store", missingCsrf.Headers.CacheControl!.ToString());
        Assert.NotNull(await store.RetrieveAsync(key, CancellationToken.None));
        Assert.Empty(upstream.Forms);
    }

    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)][InlineData(true, true)]
    public async Task PreparedLogout_RevokesBeforeHttpWithExpiredOrRemovedAdmin(bool expired, bool removedAdmin)
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture(); var admins = new MutableAdmins(); var time = new ManualOidcTime();
        var probe = new SessionBusinessProbe();
        var identity = new PortalSessionCapture(); var teacher = new PortalSessionCapture(); var assistant = new PortalSessionCapture();
        using var factory = LogoutFactory(authority, upstream, time, admins: admins, identityProxy: true, portalProxies: true, configure: services =>
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
        var cookie = await LoginSession(client, authority); var (key, _) = await Stored(factory, cookie);
        // Prove the real migrated proxy paths and native image work before local revocation.
        using (var image = await Api(client, "/api/admin/image?path=uploads/test.jpg&size=small", cookie))
            Assert.Equal(HttpStatusCode.Redirect, image.StatusCode);
        Assert.Single(probe.Student.Invocations); probe.Student.Invocations.Clear();
        foreach (var (path, portalcapture) in new[]
        {
            ("/api/identity/users", identity), ("/api/teacher-portal/admin/users", teacher), ("/api/assistant-portal/admin/users", assistant)
        })
        {
            using var reachable = await Api(client, path, cookie);
            Assert.Equal(HttpStatusCode.OK, reachable.StatusCode);
            Assert.Equal("Bearer " + authority.LastAccessToken, portalcapture.Requests.Single().Headers["Authorization"]);
        }
        identity.Requests.Clear(); teacher.Requests.Clear(); assistant.Requests.Clear();
        if (removedAdmin) admins.CurrentValue.AdminUserIds.Clear();
        if (expired)
        {
            // The ticket dies with the access token; an expired session is a nonexistent
            // session, and its own logout completes locally without an upstream preparation.
            time.Advance(TimeSpan.FromMinutes(16));
            await Rejected(await Api(client, "/api/admin/session-probe", cookie), 401, "unauthorized");
        }
        else if (removedAdmin)
            await Rejected(await Api(client, "/api/admin/session-probe", cookie), 403, "forbidden");
        var store = factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>();
        // The local session is revoked BEFORE the upstream request leaves the process.
        upstream.BeforeSend = async () => Assert.Null(await store.RetrieveAsync(key, CancellationToken.None));
        var csrf = await LogoutCsrf(client, cookie);
        using var response = await PostLogout(client, cookie, csrf);
        if (expired)
        {
            // No session cookie left to revoke: the fixed local-only answer, no preparation.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"outcome\":\"local_only\"}", await response.Content.ReadAsStringAsync());
            Assert.Empty(upstream.Forms);
            return;
        }
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(OidcTestAuthority.Issuer + "/oauth2/logout?logout_handle=" + LogoutCapture.Handle,
            response.Headers.Location!.OriginalString);
        var form = Assert.Single(upstream.Forms);
        Assert.Equal(new[] { "id_token_hint", "post_logout_redirect_uri", "state" }, form.Keys.Order());
        Assert.Equal(authority.LastIdToken, form["id_token_hint"]);
        Assert.Equal("https://admin.example.test" + AdminOidcSettings.LogoutReturnPath, form["post_logout_redirect_uri"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", form["state"]);
        // client_secret_basic: the only credential carrier is the Authorization header; no
        // browser cookie or CSRF token ever reaches the backchannel.
        var outgoingHeaders = upstream.Headers.Single();
        Assert.StartsWith("Basic ", outgoingHeaders["Authorization"]);
        Assert.False(outgoingHeaders.ContainsKey("Cookie"));
        Assert.False(outgoingHeaders.ContainsKey(AdminSessionBoundary.CsrfHeader));
        Assert.DoesNotContain(authority.LastIdToken!, await response.Content.ReadAsStringAsync());
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, c => c.StartsWith("adminSession=;"));
        // The retired password-era browser credential is cleared on every logout attempt.
        Assert.Contains(cookies, c => c.StartsWith("adminAuthToken=;") && c.Contains("samesite=strict"));
        var correlationPair = ExtractCookie(response, AdminOidcSettings.SessionCookie + "-logout-return");
        var correlationAttributes = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AdminOidcSettings.SessionCookie + "-logout-return"));
        foreach (var flag in new[] { "httponly", "secure", "samesite=lax", "path=" + AdminOidcSettings.LogoutPath }) Assert.Contains(flag, correlationAttributes.ToLowerInvariant());
        foreach (var path in new[] { "/api/admin/image?path=test", "/api/admin/session-probe", "/api/auth/csrf" })
            await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(client, "/api/identity/users", cookie)).StatusCode);
        foreach (var path in new[] { "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
            await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        Assert.Null(await store.RetrieveAsync(key, CancellationToken.None));
        Assert.Empty(probe.Student.Invocations); Assert.Empty(probe.Oss.Invocations);
        Assert.Equal(0, probe.Reads); Assert.Equal(0, probe.Writes);
        Assert.Empty(identity.Requests); Assert.Empty(teacher.Requests); Assert.Empty(assistant.Requests);
        // A replayed logout is the fixed local-only answer; the second preparation never happens.
        using var replay = await PostLogout(client, cookie, csrf);
        Assert.Equal("{\"outcome\":\"local_only\"}", await replay.Content.ReadAsStringAsync());
        Assert.Single(upstream.Forms);
        // The logout return consumes its one-time state only with the correlation cookie.
        var state = form["state"];
        var correlation = correlationPair;
        var callback = AdminOidcSettings.LogoutReturnPath + "?state=" + state;
        await LogoutReturnRejected(await Api(client, callback));
        await LogoutReturnRejected(await Api(client, callback + "&state=" + state, correlation));
        await LogoutReturnRejected(await Api(client, callback, correlation.Split('=')[0] + "=" + new string('B', 43)));
        using var complete = await Api(client, callback, correlation);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Equal("/login", complete.Headers.Location!.OriginalString);
        await LogoutReturnRejected(await Api(client, callback, correlation));
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
        => response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(name + "=")).Split(';')[0];

    private static async Task LogoutReturnRejected(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        }
    }

    [Fact]
    public async Task PreparedLogout_CsrfCannotRevokeAndBadCookiesCannotPrepare()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture();
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie); var (key, _) = await Stored(factory, cookie);
        var store = factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>();
        await LogoutRejected(await Api(client, AdminOidcSettings.LogoutPath, cookie, "POST"));
        await LogoutRejected(await Api(client, AdminOidcSettings.LogoutPath, cookie + "; " + csrf.Cookie, "POST", csrf: ["wrong"]));
        await LogoutRejected(await Api(client, AdminOidcSettings.LogoutPath, cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token, csrf.Token]));
        await LogoutRejected(await Api(client, AdminOidcSettings.LogoutPath, cookie, "POST", csrf: [csrf.Token]));
        // A form-field token of the same pair is accepted: the SPA performs a navigational
        // form POST, which carries the antiforgery token as a hidden field.
        using var navigational = await Api(client, AdminOidcSettings.LogoutPath, cookie + "; " + csrf.Cookie, "POST",
            body: new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf.Token }));
        Assert.Equal(HttpStatusCode.Redirect, navigational.StatusCode);
        Assert.Single(upstream.Forms);
        Assert.Null(await store.RetrieveAsync(key, CancellationToken.None));
    }

    private static async Task LogoutRejected(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("{\"outcome\":\"csrf_rejected\"}", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    // Not listed (recorded accepted difference): a percent-encoded unreserved character inside
    // the logout handle decodes during URI normalization, and the equivalent normalized handle
    // is correctly accepted.
    [InlineData("503")][InlineData("json")][InlineData("large")][InlineData("external")]
    [InlineData("extra-query")][InlineData("userinfo")][InlineData("fragment")][InlineData("redirect")][InlineData("timeout")]
    public async Task PreparedLogout_UpstreamFailuresNeverRestoreTheLocalSignOut(string defect)
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture { Defect = defect };
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie); var (key, _) = await Stored(factory, cookie);
        using var response = await PostLogout(client, cookie, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"outcome\":\"local_only\"}", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Null(await factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>().RetrieveAsync(key, CancellationToken.None));
        Assert.Single(upstream.Forms);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(c => c.StartsWith(AdminOidcSettings.SessionCookie + "-logout-return")));
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
        Assert.Null(await factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>().RetrieveAsync(key, CancellationToken.None));
        Assert.Single(upstream.Forms);
    }

    [Fact]
    public async Task PreparedLogout_ConcurrentLogoutsRevokeOnceAndPrepareAtMostOnce()
    {
        using var authority = new OidcTestAuthority(); var upstream = new LogoutCapture();
        using var factory = LogoutFactory(authority, upstream); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await LogoutCsrf(client, cookie);
        var (key, _) = await Stored(factory, cookie);
        // The package's per-session gate serializes the revoke-then-prepare sequence: the
        // later concurrent request finds the ticket gone and answers the fixed local-only
        // result without a second preparation.
        var responses = await Task.WhenAll(PostLogout(client, cookie, csrf), PostLogout(client, cookie, csrf));
        var bodies = await Task.WhenAll(responses.Select(async response => (response.StatusCode, await response.Content.ReadAsStringAsync())));
        Assert.Single(bodies.Where(body => body.StatusCode == HttpStatusCode.Redirect));
        Assert.Single(bodies.Where(body => body.Item1 == HttpStatusCode.OK && body.Item2 == "{\"outcome\":\"local_only\"}"));
        Assert.Single(upstream.Forms);
        Assert.Null(await factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>().RetrieveAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task PreparedLogout_StateExpiryAndTraceSafety()
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
        var correlation = ExtractCookie(response, AdminOidcSettings.SessionCookie + "-logout-return");
        time.Advance(TimeSpan.FromMinutes(5));
        // The one-time return state lives exactly the five-minute prepared-logout window.
        await LogoutReturnRejected(await Api(client, AdminOidcSettings.LogoutReturnPath + "?state=" + state, correlation));
        var messages = string.Join("\n", logs.Messages.Concat(traces.Messages));
        foreach (var value in new[] { authority.LastIdToken!, OidcTestAuthority.Secret, LogoutCapture.Handle, state, correlation.Split('=')[1] })
            Assert.DoesNotContain(value, messages);
    }
}

internal sealed class SplitLogoutHandler(OidcTestAuthority authority, LogoutCapture logout) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => request.RequestUri!.AbsolutePath == "/oauth2/logout/requests"
            ? logout.DispatchAsync(request, cancellationToken)
            : authority.DispatchAsync(request, cancellationToken);
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
    internal Task<HttpResponseMessage> DispatchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken);
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
            "json" => "invalid-json", "large" => new string('X', 4097),
            _ => JsonSerializer.Serialize(new { logout_uri = uri })
        };
        var response = new HttpResponseMessage(Defect == "503" ? HttpStatusCode.ServiceUnavailable : Defect == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        if (Defect == "redirect") response.Headers.Location = new Uri("https://external.example/should-not-follow");
        return response;
    }
}
