using Microsoft.Extensions.Configuration;
using System.Net;
using Admin.WebApi.Authentication;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Admin.WebApi.Tests.Middleware;

public class IdentityProxyMiddlewareTests
{
    private static readonly AdminOidcSettings SessionSettings = new("", "", "", "", false, TimeSpan.Zero);
    private static DefaultHttpContext TrustedContext()
    {
        var context = new DefaultHttpContext();
        context.Items[AdminSessionBoundary.TrustedSessionKey] = new AdminSessionResult(200, accessToken: "server-token");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static (Mock<HttpMessageHandler> handlerMock, HttpClient client) CreateHttpClient(
        HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "response")
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            });

        var client = new HttpClient(handlerMock.Object);
        return (handlerMock, client);
    }

    private static (Mock<IHttpClientFactory> factoryMock, Mock<HttpMessageHandler> handlerMock) SetupFactory(
        HttpClient client)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("IdentityService")).Returns(client);

        var handlerMock = new Mock<HttpMessageHandler>();
        return (factoryMock, handlerMock);
    }

    private static IdentityProxyMiddleware CreateMiddleware(
        RequestDelegate next, IHttpClientFactory factory, IdentityServiceOptions? options = null)
    {
        var optionsMock = new Mock<IOptions<IdentityServiceOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options ?? new IdentityServiceOptions());
        return new IdentityProxyMiddleware(next, factory, optionsMock.Object);
    }

    [Fact]
    public async Task NonIdentityPath_CallsNextMiddleware()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var (factoryMock, _) = SetupFactory(client);
        var middleware = CreateMiddleware(nextMock.Object, factoryMock.Object);

        var context = TrustedContext();
        context.Request.Path = "/api/other";
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextMock.Verify(n => n(It.IsAny<HttpContext>()), Times.Once);
    }

    [Fact]
    public async Task GetIdentityUsers_RewritesPathAndAddsHeaders()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("IdentityService")).Returns(client);

        var options = new IdentityServiceOptions
        {
            Authority = "http://localhost:5002",
            AppId = "test-app",
            AppSecret = "test-secret"
        };
        var optionsMock = new Mock<IOptions<IdentityServiceOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new IdentityProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = TrustedContext();
        context.Request.Path = "/api/identity/users";
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(200);
        nextMock.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);

        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5002/api/users" &&
                req.Method == HttpMethod.Get &&
                req.Headers.Contains("X-Admin-AppId") &&
                req.Headers.Contains("X-Admin-AppSecret")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task PostIdentityAccounts_ForwardsBodyAndAddsHeaders()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("IdentityService")).Returns(client);

        var options = new IdentityServiceOptions
        {
            Authority = "http://localhost:5002",
            AppId = "test-app",
            AppSecret = "test-secret"
        };
        var optionsMock = new Mock<IOptions<IdentityServiceOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new IdentityProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = TrustedContext();
        context.Request.Path = "/api/identity/accounts";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        var body = "{\"name\":\"test\"}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(200);
        nextMock.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);

        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5002/api/accounts" &&
                req.Method == HttpMethod.Post &&
                req.Content != null &&
                req.Headers.Contains("X-Admin-AppId") &&
                req.Headers.Contains("X-Admin-AppSecret")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task IdentityServiceUnreachable_Returns502()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("IdentityService")).Returns(client);

        var options = new IdentityServiceOptions { Authority = "http://localhost:5002" };
        var optionsMock = new Mock<IOptions<IdentityServiceOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new IdentityProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = TrustedContext();
        context.Request.Path = "/api/identity/users";
        context.Request.Method = "GET";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(502);
        responseBody.Position = 0;
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Identity service unreachable");
    }

    [Fact]
    public async Task BootstrapRequired_PreservesStructured503Response()
    {
        const string body =
            "{\"error\":\"bootstrap_configuration_required\",\"error_description\":\"Configure SignaCore first.\"}";
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(30));
                return response;
            });
        using var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(factory => factory.CreateClient("IdentityService")).Returns(client);
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            factoryMock.Object,
            new IdentityServiceOptions { Authority = "http://localhost:5002" });
        var context = TrustedContext();
        context.Request.Path = "/api/identity/users";
        context.Request.Method = HttpMethods.Get;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.ContentType.Should().StartWith("application/json");
        context.Response.Headers.RetryAfter.ToString().Should().Be("30");
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().Be(body);
    }

    [Fact]
    public async Task QueryParameters_PreservedInPathRewrite()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("IdentityService")).Returns(client);

        var options = new IdentityServiceOptions
        {
            Authority = "http://localhost:5002",
            AppId = "test-app",
            AppSecret = "test-secret"
        };
        var optionsMock = new Mock<IOptions<IdentityServiceOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new IdentityProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = TrustedContext();
        context.Request.Path = "/api/identity/users";
        context.Request.QueryString = new QueryString("?page=1&size=10");
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5002/api/users?page=1&size=10"),
            ItExpr.IsAny<CancellationToken>());
    }
    [Theory]
    [InlineData(false, 401)][InlineData(true, 401)]
    public async Task MissingTrustedSessionNeverForwards(bool invalidTrusted, int status)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var middleware = new IdentityProxyMiddleware(_ => throw new InvalidOperationException("No fallthrough"), factory.Object,
            Options.Create(new IdentityServiceOptions()));
        var context = new DefaultHttpContext(); context.Request.Path = "/API/IDENTITY/users/";
        context.Request.Headers.Authorization = "Bearer browser-token"; context.Request.Headers.Cookie = "adminAuthToken=browser-token";
        context.Response.Body = new MemoryStream();
        if (invalidTrusted) context.Items[AdminSessionBoundary.TrustedSessionKey] = new AdminSessionResult(200, accessToken: " ");
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode); factory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/identity", "/api")][InlineData("/API/IDENTITY/", "/api/")]
    [InlineData("/API/IDENTITY/users/", "/api/users/")]
    public async Task TrustedSessionUsesOnlyServerCredentialsAndIsolatesCookies(string path, string target)
    {
        var downstream = new Integration.SessionProxyCapture(); using var client = new HttpClient(downstream);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient("IdentityService")).Returns(client);
        var middleware = CreateMiddleware(_ => Task.CompletedTask, factory.Object,
            new IdentityServiceOptions { Authority = "https://identity.example.test", AppId = "owned-app", AppSecret = "owned-secret" });
        var context = TrustedContext(); context.Request.Path = path; context.Request.Method = "POST";
        context.Request.QueryString = new("?size=2"); context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"test\":true}"));
        foreach (var header in new[] { "Authorization", "Cookie", "Host", "X-CSRF-TOKEN", "X-Admin-AppId", "X-Admin-AppSecret" })
            context.Request.Headers[header] = "browser-input";
        await middleware.InvokeAsync(context);
        var forwarded = Assert.Single(downstream.Requests);
        Assert.Equal(target + "?size=2", forwarded.Path); Assert.Equal("{\"test\":true}", forwarded.Body);
        Assert.Equal("Bearer server-token", forwarded.Headers["Authorization"]);
        Assert.Equal("owned-app", forwarded.Headers["X-Admin-AppId"]); Assert.Equal("owned-secret", forwarded.Headers["X-Admin-AppSecret"]);
        foreach (var header in new[] { "Cookie", "Host", "X-CSRF-TOKEN" }) Assert.False(forwarded.Headers.ContainsKey(header));
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task CancellationFlowsWithoutRetry()
    {
        var downstream = new Integration.SessionProxyCapture { Wait = true }; using var client = new HttpClient(downstream);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient("IdentityService")).Returns(client);
        var middleware = CreateMiddleware(_ => Task.CompletedTask, factory.Object, new IdentityServiceOptions { Authority = "https://identity.example.test" });
        var context = TrustedContext(); context.Request.Path = "/api/identity/users"; context.Request.Method = "GET";
        using var cancellation = new CancellationTokenSource(); context.RequestAborted = cancellation.Token;
        var pending = middleware.InvokeAsync(context); await downstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await downstream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Single(downstream.Requests);
        using var pre = new CancellationTokenSource(); pre.Cancel(); context.RequestAborted = pre.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(context)); Assert.Single(downstream.Requests);
    }

    [Fact]
    public void LegacyDisabledIdentityConfigurationRejectsBeforeHost()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AdminOidc:UseSessionForIdentityProxy"] = "false" }).Build();
        Assert.Equal("AdminOidc:UseSessionForIdentityProxy", Assert.Throws<InvalidOperationException>(() =>
            AdminOidcSettings.Read(config, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment())).Message);
    }

    [Fact]
    public async Task ConcurrentTrustedSessionsNeverMixServerTokens()
    {
        var downstream = new Integration.SessionProxyCapture(); using var client = new HttpClient(downstream);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient("IdentityService")).Returns(client);
        var middleware = CreateMiddleware(_ => Task.CompletedTask, factory.Object, new IdentityServiceOptions { Authority = "https://identity.example.test" });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            var context = TrustedContext(); context.Request.Method = "GET"; context.Request.Path = "/api/identity/users/" + index;
            context.Items[AdminSessionBoundary.TrustedSessionKey] = new AdminSessionResult(200, accessToken: "server-" + index);
            await middleware.InvokeAsync(context);
        }));
        Assert.Equal(8, downstream.Requests.Count);
        foreach (var request in downstream.Requests)
            Assert.Equal("Bearer server-" + request.Path.Split('/').Last(), request.Headers["Authorization"]);
    }

}
