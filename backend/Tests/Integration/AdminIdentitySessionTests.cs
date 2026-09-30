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
    [InlineData(false)][InlineData(true)]
    public async Task IdentitySession_RealProgramRoutesAndIsolatesCredentials(bool portalProxies)
    {
        using var authority = new OidcTestAuthority();
        var downstream = new SessionProxyCapture();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => downstream);
        }, sessionApi: true, identityProxy: true, portalProxies: portalProxies);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority);
        var csrf = await Csrf(client, cookie);
        foreach (var path in new[] { "/api/identity", "/API/IDENTITY/", "/API/IDENTITY/users?size=2" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { test = true }) };
            request.Headers.Add("Cookie", cookie + "; " + csrf.Cookie);
            request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
            request.Headers.Add("X-Admin-AppId", "browser-app"); request.Headers.Add("X-Admin-AppSecret", "browser-secret");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var last = downstream.Requests.Last();
            Assert.Equal("Bearer " + OidcTestAuthority.AccessToken, last.Headers["Authorization"]);
            Assert.Equal(OidcTestAuthority.ClientId, last.Headers["X-Admin-AppId"]);
            Assert.Equal(OidcTestAuthority.Secret, last.Headers["X-Admin-AppSecret"]);
            foreach (var forbidden in new[] { "Cookie", "Host", "X-CSRF-TOKEN" }) Assert.False(last.Headers.ContainsKey(forbidden));
            Assert.Contains("\"test\":true", last.Body);
        }
        Assert.Equal(new[] { "/api", "/api/", "/api/users?size=2" }, downstream.Requests.Select(r => r.Path));
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable })
        {
            downstream.Status = status;
            using var response = await Api(client, "/api/identity/users", cookie);
            Assert.Equal(status, response.StatusCode); Assert.Null(response.Headers.Location);
            Assert.Equal("30", response.Headers.RetryAfter!.ToString());
        }
        downstream.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await Api(client, "/api/identity/users", cookie)).StatusCode);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task IdentitySession_RejectsBeforeHttpAndNeverReplays(bool portalProxies)
    {
        using var authority = new OidcTestAuthority();
        var downstream = new SessionProxyCapture(); var admins = new MutableAdmins();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => downstream);
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
        }, sessionApi: true, identityProxy: true, portalProxies: portalProxies);
        using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        var path = "/API/IDENTITY/users/";
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
        var (key, ticket) = await Stored(factory, cookie);
        ticket.Properties.UpdateTokenValue("expires_at", DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        await Rejected(await Api(client, path, cookie), 401, "reauthentication_required");
        await factory.Services.GetRequiredService<MemoryTicketStore>().RemoveAsync(key);
        await Rejected(await Api(client, path, cookie), 401, "unauthorized");
        Assert.Equal(3, downstream.Requests.Count);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task IdentitySession_CancellationAndDifferentSubjectCsrfNeverReplay(bool portalProxies)
    {
        using var authority = new OidcTestAuthority();
        var downstream = new SessionProxyCapture(); var admins = new MutableAdmins();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => downstream);
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
        }, sessionApi: true, identityProxy: true, portalProxies: portalProxies);
        using var client = Browser(factory);
        var first = await LoginSession(client, authority); var csrf = await Csrf(client, first);
        var second = await LoginSession(client, authority); var (key, ticket) = await Stored(factory, second);
        ticket.Properties.Items["oidc.subject"] = "second";
        ticket = new(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new("iss", OidcTestAuthority.Issuer), new("sub", "second")], AdminOidcSettings.SessionScheme)), ticket.Properties, AdminOidcSettings.SessionScheme);
        admins.CurrentValue.AdminUserIds.Add("second");
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        await Rejected(await Api(client, "/api/identity/users", second + "; " + csrf.Cookie, "POST", csrf: [csrf.Token]), 400, "csrf_invalid");
        Assert.Empty(downstream.Requests);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(client, "/api/identity/users", first, cancellation: cancelled.Token));
        Assert.Empty(downstream.Requests);
        downstream.Wait = true;
        using var ongoing = new CancellationTokenSource();
        var pending = Api(client, "/api/identity/users", first, cancellation: ongoing.Token);
        await downstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); ongoing.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await downstream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(downstream.Requests);
    }

    [Fact]
    public void IdentitySession_InvalidSwitchCombinationFailsWithoutValues()
    {
        using var invalid = CreateFactory(settings: new Dictionary<string, string?> { ["AdminOidc:UseSessionForIdentityProxy"] = "true" });
        Assert.Equal("AdminOidc:UseSessionForIdentityProxy requires AdminOidc:Enabled and AdminOidc:UseSessionForAdminApi",
            Assert.Throws<InvalidOperationException>(() => invalid.Services).Message);
    }
}

internal sealed class SessionProxyCapture : HttpMessageHandler
{
    internal readonly System.Collections.Concurrent.ConcurrentQueue<(string Path, Dictionary<string, string> Headers, string Body)> Requests = new();
    internal HttpStatusCode Status = HttpStatusCode.OK;
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
        var response = new HttpResponseMessage(Status) { Content = new StringContent("{\"result\":true}", System.Text.Encoding.UTF8, "application/json") };
        response.Headers.Add("Set-Cookie", "upstream=fake"); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
        return response;
    }
}
