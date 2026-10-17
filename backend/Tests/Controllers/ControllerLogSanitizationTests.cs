using System.Security.Claims;
using Admin.WebApi.Controllers;
using Admin.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// Controller-path coverage for the <c>AdminLogValue.Sanitize</c> barrier (#91): a
/// user-controlled value that reaches <c>ILogger</c> must already be sanitized (control
/// runes replaced by spaces, the rest unchanged). Covers the failure path of
/// <see cref="MistakeController.GetMistakeItem"/> and, since #130, the success paths of
/// <see cref="AppSettingsController.UpdateSetting"/> and
/// <see cref="AppSettingsController.DeleteSetting"/>, whose route <c>key</c> can only be a
/// registered catalog key — the registry is injected through its internal constructor with a
/// definition whose key embeds log-forging runes.
/// </summary>
public sealed class ControllerLogSanitizationTests
{
    private const string ProbeKey = "probe\r\n2026-10-17 08:00:00 INFO audit.delete: done\x1b[31m";
    private const string SanitizedProbeKey = "probe  2026-10-17 08:00:00 INFO audit.delete: done [31m";

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
        var loggedId = LoggedStateValue(logger, "Id");
        Assert.Equal("item-1  2026-10-06 12:00:00 INFO audit.delete: done [31m", loggedId);
        Assert.DoesNotContain("\r", loggedId!.ToString());
        Assert.DoesNotContain("\n", loggedId.ToString());
    }

    [Fact]
    public async Task UpdateSettingSanitizesTheSettingKeyBeforeItReachesTheLogger()
    {
        var (controller, logger, settings) = CreateController(new AppSettingsRegistry(
            [new AppSettingDefinition(ProbeKey, "probe definition", 0, 23, 7)]));
        settings.Setup(service => service.UpsertAsync(ProbeKey, "7", "admin-sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAppSetting(ProbeKey, "7", AppSettingsRegistry.SourceDatabase, 123L, "admin-sub-1"));

        var result = await controller.UpdateSetting(ProbeKey, new UpdateSettingRequest("7"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var loggedKey = LoggedStateValue(logger, "SettingKey");
        Assert.Equal(SanitizedProbeKey, loggedKey);
        Assert.DoesNotContain("\r", loggedKey!.ToString());
        Assert.DoesNotContain("\n", loggedKey.ToString());
    }

    [Fact]
    public async Task DeleteSettingSanitizesTheSettingKeyBeforeItReachesTheLogger()
    {
        var (controller, logger, settings) = CreateController(new AppSettingsRegistry(
            [new AppSettingDefinition(ProbeKey, "probe definition", 0, 23, 7)]));
        settings.Setup(service => service.TryDeleteOverrideAsync(ProbeKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAppSetting(ProbeKey, "7", AppSettingsRegistry.SourceDatabase));

        var result = await controller.DeleteSetting(ProbeKey, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var loggedKey = LoggedStateValue(logger, "SettingKey");
        Assert.Equal(SanitizedProbeKey, loggedKey);
        Assert.DoesNotContain("\r", loggedKey!.ToString());
        Assert.DoesNotContain("\n", loggedKey.ToString());
    }

    [Fact]
    public async Task BenignRegisteredKeysStillLogUnchangedOnBothWritePaths()
    {
        const string key = AppSettingsRegistry.OssAuditScheduledHourKey;
        var (controller, logger, settings) = CreateController(new AppSettingsRegistry());
        settings.Setup(service => service.UpsertAsync(key, "7", "admin-sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAppSetting(key, "7", AppSettingsRegistry.SourceDatabase, 123L, "admin-sub-1"));
        settings.Setup(service => service.TryDeleteOverrideAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAppSetting(key, "7", AppSettingsRegistry.SourceStaticConfiguration));

        Assert.IsType<OkObjectResult>(await controller.UpdateSetting(key, new UpdateSettingRequest("7"), CancellationToken.None));
        Assert.Equal(key, LoggedStateValue(logger, "SettingKey"));

        Assert.IsType<OkObjectResult>(await controller.DeleteSetting(key, CancellationToken.None));
        Assert.Equal(key, LoggedStateValue(logger, "SettingKey", skip: 1));
    }

    private static (AppSettingsController Controller, Mock<ILogger<AppSettingsController>> Logger, Mock<IAppSettingsService> Settings) CreateController(
        AppSettingsRegistry registry)
    {
        var settings = new Mock<IAppSettingsService>();
        var logger = new Mock<ILogger<AppSettingsController>>();
        var controller = new AppSettingsController(settings.Object, registry, logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", "admin-sub-1")], "AdminSession")),
                },
            },
        };
        return (controller, logger, settings);
    }

    /// <summary>Returns the state value the controller passed to ILogger, skipping the first <paramref name="skip"/> Log calls.</summary>
    private static string? LoggedStateValue<TCategory>(Mock<ILogger<TCategory>> logger, string stateKey, int skip = 0)
    {
        var logged = logger.Invocations.Where(invocation => invocation.Method.Name == "Log").Skip(skip).Single();
        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(logged.Arguments[2]);
        return (string?)state.Single(pair => pair.Key == stateKey).Value;
    }
}
