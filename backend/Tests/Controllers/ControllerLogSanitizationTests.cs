using Admin.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// #91 controller-path coverage: a route id is user-controlled and reaches the logger on the
/// failure path of <see cref="MistakeController.GetMistakeItem"/> — the value passed to
/// ILogger must already be sanitized (control runes replaced by spaces, the rest unchanged).
/// </summary>
public sealed class ControllerLogSanitizationTests
{
    [Fact]
    public async Task MistakeItemFailureSanitizesRouteIdBeforeItReachesTheLogger()
    {
        var logger = new Mock<ILogger<MistakeController>>();
        var mistakeClient = new Mock<IMistakeHttpClient>();
        mistakeClient.Setup(client => client.GetMistakeItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("downstream unreachable"));
        var controller = new MistakeController(mistakeClient.Object, new Mock<IStudentHttpClient>().Object, logger.Object);
        const string malicious = "item-1\r\n2026-10-06 12:00:00 INFO audit.delete: done\x1b[31m";

        var result = await controller.GetMistakeItem(malicious);

        Assert.IsType<ObjectResult>(result);
        var logged = logger.Invocations.Single(invocation => invocation.Method.Name == "Log");
        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(logged.Arguments[2]);
        var loggedId = state.Single(pair => pair.Key == "Id").Value;
        const string sanitized = "item-1  2026-10-06 12:00:00 INFO audit.delete: done [31m";
        Assert.Equal(sanitized, loggedId);
        Assert.DoesNotContain("\r", loggedId!.ToString());
        Assert.DoesNotContain("\n", loggedId.ToString());
    }
}
