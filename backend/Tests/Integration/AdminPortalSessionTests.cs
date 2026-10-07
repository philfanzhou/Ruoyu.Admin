using System.Net;
using System.Net.Http.Json;
using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    [Theory]
    [InlineData("teacher", false)][InlineData("assistant", false)]
    [InlineData("teacher", true)][InlineData("assistant", true)]
    public async Task PortalSession_RealProgramRoutesAndIsolatesCredentials(string portal, bool identityProxy)
    {
        using var authority = new OidcTestAuthority();
        var downstream = new PortalSessionCapture();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.AddHttpClient(portal == "teacher" ? "TeacherPortal" : "AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => downstream);
        }, sessionApi: true, portalProxies: true, identityProxy: identityProxy);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        var csrf = await Csrf(client, cookie);
        foreach (var path in new[] { $"/api/{portal}-portal", $"/API/{portal.ToUpperInvariant()}-PORTAL/", $"/API/{portal.ToUpperInvariant()}-PORTAL/users?size=2", $"/API/{portal.ToUpperInvariant()}-PORTAL/ADMIN/users", $"/API/{portal.ToUpperInvariant()}-PORTAL/AUTH/me" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { test = true }) };
            request.Headers.Add("Cookie", cookie + "; " + csrf.Cookie);
            request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
            request.Headers.Add("X-Admin-AppId", "browser-app"); request.Headers.Add("X-Admin-AppSecret", "browser-secret");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var last = downstream.Requests.Last();
            Assert.Equal("Bearer " + authority.LastAccessToken, last.Headers["Authorization"]);
            foreach (var forbidden in new[] { "Cookie", "Host", "X-CSRF-TOKEN", "X-Admin-AppId", "X-Admin-AppSecret" }) Assert.False(last.Headers.ContainsKey(forbidden));
            Assert.Contains("\"test\":true", last.Body);
        }
        Assert.Equal(new[] { "/api/admin", "/api/admin/", "/api/admin/users?size=2", "/api/admin/users", "/api/auth/me" }, downstream.Requests.Select(r => r.Path));
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable })
        {
            downstream.Status = status;
            using var response = await Api(client, $"/api/{portal}-portal/users", cookie);
            Assert.Equal(status, response.StatusCode); Assert.Null(response.Headers.Location);
            Assert.Equal("30", response.Headers.RetryAfter!.ToString());
        }
        downstream.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await Api(client, $"/api/{portal}-portal/users", cookie)).StatusCode);
    }

    [Theory]
    [InlineData("teacher", false)][InlineData("assistant", false)]
    [InlineData("teacher", true)][InlineData("assistant", true)]
    public async Task PortalSession_RejectsBeforeHttpAndNeverReplays(string portal, bool identityProxy)
    {
        using var authority = new OidcTestAuthority();
        var downstream = new PortalSessionCapture(); var admins = new MutableAdmins();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.AddHttpClient(portal == "teacher" ? "TeacherPortal" : "AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => downstream);
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
        }, sessionApi: true, portalProxies: true, identityProxy: identityProxy);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        var path = $"/API/{portal.ToUpperInvariant()}-PORTAL/users/";
        await Rejected(await Api(client, path), 401, "unauthorized");
        foreach (var auth in new[] { new[] { "Bearer wrong" }, new[] { "Basic wrong" }, new[] { "Bearer wrong", "Bearer other" }, new[] { "Bearer " + authority.LegacyBearer() } })
            await Rejected(await Api(client, path, cookie, authorization: auth), 401, "unauthorized");
        var empty = await factory.Server.SendAsync(ctx =>
        { ctx.Request.Path = path; ctx.Request.Method = "GET"; ctx.Request.Scheme = "https"; ctx.Request.Headers.Cookie = cookie; ctx.Request.Headers.Authorization = ""; });
        Assert.Equal(401, empty.Response.StatusCode);
        admins.CurrentValue.AdminUserIds.Clear();
        await Rejected(await Api(client, path, cookie), 403, "forbidden");
        admins.CurrentValue.AdminUserIds.Add("fake-subject");
        foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE" })
        {
            await Rejected(await Api(client, path, cookie, method), 400, "csrf_invalid");
            await Rejected(await Api(client, path, cookie + "; " + csrf.Cookie, method, csrf: ["wrong"]), 400, "csrf_invalid");
            await Rejected(await Api(client, path, cookie + "; " + csrf.Cookie, method, csrf: [csrf.Token, csrf.Token]), 400, "csrf_invalid");
        }
        Assert.Empty(downstream.Requests);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Api(client, path, cookie + "; " + csrf.Cookie, "POST", csrf: [i % 2 == 0 ? csrf.Token : "wrong"])));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK)); Assert.Equal(3, downstream.Requests.Count);
        var (key, _) = await Stored(factory, cookie);
        // An expired session is indistinguishable from a removed one: the fixed 401.
        await factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>().RemoveAsync(key, CancellationToken.None);
        await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        await factory.Services.GetRequiredService<SignaCore.Client.AspNetCore.ITicketStore>().RemoveAsync(key, CancellationToken.None);
        await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        Assert.Equal(3, downstream.Requests.Count);
    }

    [Theory]
    [InlineData("teacher", false)][InlineData("assistant", false)]
    [InlineData("teacher", true)][InlineData("assistant", true)]
    public async Task PortalSession_CancellationAndDifferentSubjectCsrfNeverReplay(string portal, bool identityProxy)
    {
        using var authority = new OidcTestAuthority();
        var downstream = new PortalSessionCapture(); var admins = new MutableAdmins();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.AddHttpClient(portal == "teacher" ? "TeacherPortal" : "AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => downstream);
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
        }, sessionApi: true, portalProxies: true, identityProxy: identityProxy);
        using var client = Browser(factory);
        var first = await LoginSession(client, authority); var csrf = await Csrf(client, first);
        admins.CurrentValue.AdminUserIds.Add("second");
        var second = await ForgedTicket(factory, subject: "second");
        await Rejected(await Api(client, $"/api/{portal}-portal/users", second + "; " + csrf.Cookie, "POST", csrf: [csrf.Token]), 400, "csrf_invalid");
        Assert.Empty(downstream.Requests);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(client, $"/api/{portal}-portal/users", first, cancellation: cancelled.Token));
        Assert.Empty(downstream.Requests);
        downstream.Wait = true;
        using var ongoing = new CancellationTokenSource();
        var pending = Api(client, $"/api/{portal}-portal/users", first, cancellation: ongoing.Token);
        await downstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); ongoing.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await downstream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(downstream.Requests);
    }

    [Theory]
    [InlineData("false")][InlineData("not-a-bool")]
    public void PortalSession_LegacyModeRejectsBeforeHost(string value)
    {
        using var factory = CreateFactory(settings: new Dictionary<string, string?> { ["AdminOidc:UseSessionForPortalProxies"] = value });
        Assert.Equal("AdminOidc:UseSessionForPortalProxies", Assert.Throws<InvalidOperationException>(() => factory.Services).Message);
    }

    [Fact]
    public async Task PortalSession_AbsentLegacyKeysRegisterOnlySessionAndOidc()
    {
        using var factory = CreateFactory();
        var schemes = (await factory.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync()).Select(s => s.Name).Order().ToArray();
        Assert.Equal(new[] { SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.AuthenticationScheme, SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.SessionAuthenticationScheme }.Order(), schemes);
    }
}

internal sealed class PortalSessionCapture : HttpMessageHandler
{
    internal readonly System.Collections.Concurrent.ConcurrentQueue<(string Path, Dictionary<string, string> Headers, string Body)> Requests = new();
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string ResponseBody = "{\"result\":true}";
    internal bool Fail;
    internal bool Wait;
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Fail) throw new HttpRequestException("fake.unreachable");
        Requests.Enqueue((request.RequestUri!.PathAndQuery, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        if (Wait)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        var response = new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json") };
        response.Headers.Add("Set-Cookie", "upstream=fake"); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
        return response;
    }
}
