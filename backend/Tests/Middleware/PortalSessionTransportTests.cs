using Microsoft.Extensions.Configuration;
using Admin.WebApi.Authentication;
using Admin.WebApi.Tests.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Admin.WebApi.Tests.Middleware;

public sealed class PortalSessionTransportTests
{
    private static readonly AdminOidcSettings Settings = new("", "", "", "", false, TimeSpan.Zero);
    private static Func<HttpContext, Task> Proxy(string portal, IHttpClientFactory clients, bool enabled = true, string url = "https://portal.example.test")
        => portal == "teacher"
            ? new TeacherPortalProxyMiddleware(_ => throw new InvalidOperationException("No fallthrough"), clients,
                Options.Create(new TeacherPortalOptions { Url = url })).InvokeAsync
            : new AssistantPortalProxyMiddleware(_ => throw new InvalidOperationException("No fallthrough"), clients,
                Options.Create(new AssistantPortalOptions { Url = url })).InvokeAsync;
    private static DefaultHttpContext Context(string portal, string suffix = "", string token = "server-token")
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/API/" + portal.ToUpperInvariant() + "-PORTAL" + suffix;
        context.Request.Method = "GET"; context.Response.Body = new MemoryStream();
        context.Items[AdminSessionBoundary.TrustedSessionKey] = new AdminSessionResult(200, accessToken: token);
        return context;
    }

    [Theory]
    [InlineData("teacher", false, false, 401)][InlineData("assistant", false, false, 401)]
    [InlineData("teacher", true, false, 401)][InlineData("assistant", true, false, 401)]
    [InlineData("teacher", true, true, 401)][InlineData("assistant", true, true, 401)]
    public async Task DisabledOrMissingTrustedSessionNeverForwards(string portal, bool enabled, bool emptyToken, int status)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict); var proxy = Proxy(portal, factory.Object, enabled);
        var context = Context(portal, "/users", emptyToken ? " " : "server-token");
        if (!emptyToken) context.Items.Clear();
        context.Request.Headers.Authorization = "Bearer browser-token"; context.Request.Headers.Cookie = "adminAuthToken=browser-token";
        await proxy(context); Assert.Equal(status, context.Response.StatusCode); factory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("teacher", "", "/api/admin")][InlineData("assistant", "", "/api/admin")]
    [InlineData("teacher", "/", "/api/admin/")][InlineData("assistant", "/", "/api/admin/")]
    [InlineData("teacher", "/ADMIN/users/", "/api/admin/users/")][InlineData("assistant", "/ADMIN/users/", "/api/admin/users/")]
    [InlineData("teacher", "/AUTH/me", "/api/auth/me")][InlineData("assistant", "/AUTH/me", "/api/auth/me")]
    [InlineData("teacher", "/users", "/api/admin/users")][InlineData("assistant", "/users", "/api/admin/users")]
    public async Task RoutesBodyAndCredentialsUseSameMatrix(string portal, string suffix, string mapped)
    {
        var capture = new PortalSessionCapture(); using var client = new HttpClient(capture);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        var context = Context(portal, suffix); context.Request.Method = "POST"; context.Request.QueryString = new("?size=2");
        context.Request.ContentType = "application/json"; context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"test\":true}"));
        foreach (var header in new[] { "Authorization", "Cookie", "Host", "X-CSRF-TOKEN", "X-Admin-AppId", "X-Admin-AppSecret" })
            context.Request.Headers[header] = "browser-input";
        await Proxy(portal, factory.Object)(context);
        var request = Assert.Single(capture.Requests); Assert.Equal(mapped + "?size=2", request.Path); Assert.Equal("{\"test\":true}", request.Body);
        Assert.Equal("Bearer server-token", request.Headers["Authorization"]);
        foreach (var header in new[] { "Cookie", "Host", "X-CSRF-TOKEN", "X-Admin-AppId", "X-Admin-AppSecret" }) Assert.False(request.Headers.ContainsKey(header));
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Theory]
    [InlineData("teacher")][InlineData("assistant")]
    public async Task CancellationPropagatesWithoutRetry(string portal)
    {
        var capture = new PortalSessionCapture { Wait = true }; using var client = new HttpClient(capture);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        var proxy = Proxy(portal, factory.Object); var context = Context(portal, "/users");
        using var cancellation = new CancellationTokenSource(); context.RequestAborted = cancellation.Token;
        var pending = proxy(context); await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); await capture.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(capture.Requests);
        using var pre = new CancellationTokenSource(); pre.Cancel(); context.RequestAborted = pre.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proxy(context)); Assert.Single(capture.Requests);
    }

    [Theory]
    [InlineData("teacher")][InlineData("assistant")]
    public async Task ConcurrentSessionsNeverMixServerTokens(string portal)
    {
        var capture = new PortalSessionCapture(); using var client = new HttpClient(capture);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        var proxy = Proxy(portal, factory.Object);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => proxy(Context(portal, "/users/" + index, "server-" + index))));
        Assert.Equal(8, capture.Requests.Count);
        foreach (var request in capture.Requests) Assert.Equal("Bearer server-" + request.Path.Split('/').Last(), request.Headers["Authorization"]);
    }

    [Fact]
    public void LegacyDisabledPortalConfigurationRejectsBeforeHost()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AdminOidc:UseSessionForPortalProxies"] = "false" }).Build();
        Assert.Equal("AdminOidc:UseSessionForPortalProxies", Assert.Throws<InvalidOperationException>(() =>
            AdminOidcSettings.Read(config, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment())).Message);
    }
}
