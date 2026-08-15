using System.Net;
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

        var context = new DefaultHttpContext();
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

        var context = new DefaultHttpContext();
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

        var context = new DefaultHttpContext();
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

        var context = new DefaultHttpContext();
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
        var context = new DefaultHttpContext();
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

        var context = new DefaultHttpContext();
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
}
