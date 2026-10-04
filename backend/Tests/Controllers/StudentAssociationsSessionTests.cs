using Admin.WebApi.Authentication;
using Admin.WebApi.Controllers;
using Admin.WebApi.Tests.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public sealed class StudentAssociationsSessionTests
{
    private static readonly AdminOidcSettings Settings = new(true, "", "", "", "", false, TimeSpan.Zero) { UseSessionForAdminApi = true };
    private static StudentAssociationsController Controller(IServiceProvider services, IHttpClientFactory clients, IStudentHttpClient student, string token = "server-token", CancellationToken cancellation = default)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["TeacherPortal:Url"] = "https://teacher.example.test", ["AssistantPortal:Url"] = "https://assistant.example.test" }).Build();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellation };
        context.Items[AdminSessionBoundary.TrustedSessionKey] = new AdminSessionResult(200, accessToken: token);
        context.Request.Headers.Authorization = "Bearer browser-input";
        return new StudentAssociationsController(clients, student, configuration, NullLogger<StudentAssociationsController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Fact]
    public async Task DisabledApiRejectsBeforeStudentOrPortalEvenWithTrustedItem()
    {
        using var services = new ServiceCollection().AddSingleton(Settings with { UseSessionForAdminApi = false }).BuildServiceProvider();
        var clients = new Mock<IHttpClientFactory>(MockBehavior.Strict); var student = new Mock<IStudentHttpClient>(MockBehavior.Strict);
        var controller = Controller(services, clients.Object, student.Object);
        var result = Assert.IsType<ObjectResult>(await controller.GetLinkedAccounts(Guid.NewGuid()));
        Assert.Equal(503, result.StatusCode); clients.VerifyNoOtherCalls(); student.VerifyNoOtherCalls();
        controller.HttpContext.Request.Path = "/API/ADMIN/STUDENTS/00000000-0000-0000-0000-000000000001/LINKED-ACCOUNTS/";
        await new AdminSessionMiddleware(_ => throw new InvalidOperationException("No authentication"), Settings with { UseSessionForAdminApi = false })
            .InvokeAsync(controller.HttpContext, null!);
        Assert.Equal(503, controller.HttpContext.Response.StatusCode);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task EnabledApiUsesServerTokenRegardlessOfPortalCapability(bool portals)
    {
        using var services = new ServiceCollection().AddSingleton(Settings with { UseSessionForPortalProxies = portals }).BuildServiceProvider();
        var student = new Mock<IStudentHttpClient>(); student.Setup(s => s.GetIdentityAccountsByStudentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(["known"]);
        var teacher = new PortalSessionCapture { ResponseBody = "[{\"userId\":\"known\"}]" }; var assistant = new PortalSessionCapture { ResponseBody = "[{\"userId\":\"known\"}]" };
        using var teacherClient = new HttpClient(teacher); using var assistantClient = new HttpClient(assistant);
        var clients = new Mock<IHttpClientFactory>(); clients.Setup(f => f.CreateClient("TeacherPortal")).Returns(teacherClient); clients.Setup(f => f.CreateClient("AssistantPortal")).Returns(assistantClient);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Controller(services, clients.Object, student.Object, "server-" + index).GetLinkedAccounts(Guid.NewGuid())));
        foreach (var capture in new[] { teacher, assistant })
        {
            Assert.Equal(8, capture.Requests.Count);
            Assert.Equal(Enumerable.Range(0, 8).Select(i => "Bearer server-" + i).Order(), capture.Requests.Select(r => r.Headers["Authorization"]).Order());
            Assert.All(capture.Requests, request => Assert.False(request.Headers.ContainsKey("Cookie")));
        }
    }

    [Theory]
    [InlineData("teacher", "status")][InlineData("assistant", "status")]
    [InlineData("teacher", "json")][InlineData("assistant", "json")]
    [InlineData("teacher", "network")][InlineData("assistant", "network")]
    public async Task SinglePortalFailurePreservesTheOtherSide(string failedPortal, string failure)
    {
        using var services = new ServiceCollection().AddSingleton(Settings).BuildServiceProvider();
        var student = new Mock<IStudentHttpClient>(); student.Setup(s => s.GetIdentityAccountsByStudentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(["known"]);
        var teacher = new PortalSessionCapture { ResponseBody = "[{\"userId\":\"known\"},{\"userId\":\"other\"}]" };
        var assistant = new PortalSessionCapture { ResponseBody = teacher.ResponseBody };
        var failed = failedPortal == "teacher" ? teacher : assistant;
        if (failure == "status") failed.Status = System.Net.HttpStatusCode.ServiceUnavailable;
        if (failure == "json") failed.ResponseBody = "invalid-json";
        if (failure == "network") failed.Fail = true;
        using var teacherClient = new HttpClient(teacher); using var assistantClient = new HttpClient(assistant);
        var clients = new Mock<IHttpClientFactory>(); clients.Setup(f => f.CreateClient("TeacherPortal")).Returns(teacherClient); clients.Setup(f => f.CreateClient("AssistantPortal")).Returns(assistantClient);
        var result = Assert.IsType<OkObjectResult>(await Controller(services, clients.Object, student.Object).GetLinkedAccounts(Guid.NewGuid()));
        var body = Assert.IsType<LinkedAccountsResponse>(result.Value);
        Assert.Empty(failedPortal == "teacher" ? body.Teachers : body.Assistants);
        Assert.Equal("known", Assert.Single(failedPortal == "teacher" ? body.Assistants : body.Teachers).UserId);
    }

    [Theory]
    [InlineData("teacher")][InlineData("assistant")]
    public async Task PortalCancellationDoesNotBecomeEmptySuccessfulAggregate(string heldPortal)
    {
        using var services = new ServiceCollection().AddSingleton(Settings).BuildServiceProvider();
        var student = new Mock<IStudentHttpClient>(); student.Setup(s => s.GetIdentityAccountsByStudentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(["known"]);
        var teacher = new PortalSessionCapture { Wait = heldPortal == "teacher", ResponseBody = "[]" }; var assistant = new PortalSessionCapture { Wait = heldPortal == "assistant" };
        using var teacherClient = new HttpClient(teacher); using var assistantClient = new HttpClient(assistant);
        var clients = new Mock<IHttpClientFactory>(); clients.Setup(f => f.CreateClient("TeacherPortal")).Returns(teacherClient); clients.Setup(f => f.CreateClient("AssistantPortal")).Returns(assistantClient);
        using var cancellation = new CancellationTokenSource(); var controller = Controller(services, clients.Object, student.Object, cancellation: cancellation.Token);
        var pending = controller.GetLinkedAccounts(Guid.NewGuid()); var held = heldPortal == "teacher" ? teacher : assistant;
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); await held.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Single(held.Requests);
        if (heldPortal == "teacher") Assert.Empty(assistant.Requests);
    }
}
