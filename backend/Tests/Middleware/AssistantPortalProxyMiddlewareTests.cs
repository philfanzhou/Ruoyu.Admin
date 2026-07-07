using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Admin.WebApi.Tests.Middleware;

public class AssistantPortalProxyMiddlewareTests
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

    private static AssistantPortalProxyMiddleware CreateMiddleware(
        RequestDelegate next, IHttpClientFactory factory, AssistantPortalOptions? options = null)
    {
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options ?? new AssistantPortalOptions());
        return new AssistantPortalProxyMiddleware(next, factory, optionsMock.Object);
    }

    [Fact]
    public async Task NonAssistantPortalPath_CallsNextMiddleware()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);
        var middleware = CreateMiddleware(nextMock.Object, factoryMock.Object,
            new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "key" });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/other";
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextMock.Verify(n => n(It.IsAny<HttpContext>()), Times.Once);
    }

    [Fact]
    public async Task AdminPath_RewritesToApiAdminAndAddsHeader()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "my-api-key" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/admin/assistants";
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(200);
        nextMock.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);

        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5021/api/admin/assistants" &&
                req.Headers.Contains("X-Admin-Key") &&
                req.Headers.GetValues("X-Admin-Key").First() == "my-api-key"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AuthPath_RewritesToApiAuth()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "my-api-key" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/auth/check-assistant";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        var body = "{\"phone\":\"13800138000\"}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5021/api/auth/check-assistant" &&
                req.Method == HttpMethod.Post),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task OtherPath_RewritesToApiAdmin()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "my-api-key" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/other";
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5021/api/admin/other"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AssistantPortalUnreachable_Returns502()
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
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "my-api-key" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/admin/assistants";
        context.Request.Method = "GET";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(502);
        responseBody.Position = 0;
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Assistant portal service unreachable");
    }

    [Fact]
    public async Task NotConfigured_EmptyAddress_Returns503()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "", AdminApiKey = "some-key" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/admin/assistants";
        context.Request.Method = "GET";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(503);
        responseBody.Position = 0;
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Assistant portal not configured");
    }

    [Fact]
    public async Task NotConfigured_EmptyAdminApiKey_Returns503()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/admin/assistants";
        context.Request.Method = "GET";
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(503);
        responseBody.Position = 0;
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Assistant portal not configured");
    }

    [Fact]
    public async Task QueryParameters_PreservedInPathRewrite()
    {
        // Arrange
        var nextMock = new Mock<RequestDelegate>();
        var (handlerMock, client) = CreateHttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AssistantPortal")).Returns(client);

        var options = new AssistantPortalOptions { Address = "http://localhost:5021", AdminApiKey = "my-api-key" };
        var optionsMock = new Mock<IOptions<AssistantPortalOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var middleware = new AssistantPortalProxyMiddleware(nextMock.Object, factoryMock.Object, optionsMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/assistant-portal/admin/assistants";
        context.Request.QueryString = new QueryString("?page=2&filter=active");
        context.Request.Method = "GET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://localhost:5021/api/admin/assistants?page=2&filter=active"),
            ItExpr.IsAny<CancellationToken>());
    }
}
