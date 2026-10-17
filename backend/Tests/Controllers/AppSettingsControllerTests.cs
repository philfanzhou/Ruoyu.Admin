using System.Security.Claims;
using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// Controller-path tests for the application-settings management API (issue #118):
/// the registry gate (unknown keys 404 and never write), the fixed validation message
/// (400), the upsert/delete result shapes, and the admin subject flowing into
/// <c>updatedBy</c>. The 401/CSRF gates are the shared /api/admin session boundary and
/// are covered by the integration session tests.
/// </summary>
public sealed class AppSettingsControllerTests
{
    private readonly Mock<IAppSettingsService> _settings = new();
    private readonly AppSettingsController _controller;

    public AppSettingsControllerTests()
    {
        _controller = new AppSettingsController(
            _settings.Object,
            new AppSettingsRegistry(),
            NullLogger<AppSettingsController>.Instance)
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
    }

    [Fact]
    public async Task GetSettings_ReturnsEveryEffectiveSetting()
    {
        _settings.Setup(service => service.ListEffectiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EffectiveAppSetting("OssAudit:ScheduledHour", "7", "db", 123L, "admin-sub-1"),
                new EffectiveAppSetting("OssAudit:ScheduledMinute", "30", "default"),
            });

        var result = await _controller.GetSettings(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value.Should().BeAssignableTo<IEnumerable<AppSettingDto>>().Subject.ToArray();
        items.Should().HaveCount(2);
        items[0].Should().Be(new AppSettingDto("OssAudit:ScheduledHour", "7", "db", 123L, "admin-sub-1"));
        items[1].Should().Be(new AppSettingDto("OssAudit:ScheduledMinute", "30", "default"));
    }

    [Fact]
    public async Task UpdateSetting_UnknownKey_Is404WithoutTouchingTheService()
    {
        var result = await _controller.UpdateSetting("Not:ASetting", new UpdateSettingRequest("1"), CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ErrorResponse>()
            .Which.Message.Should().Be("Setting 'Not:ASetting' is not a registered application setting.");
        _settings.Verify(service => service.UpsertAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("24")]
    [InlineData("two")]
    public async Task UpdateSetting_InvalidValue_Is400WithTheFixedDomainMessage(string? value)
    {
        var result = await _controller.UpdateSetting(
            "OssAudit:ScheduledHour", new UpdateSettingRequest(value), CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<ErrorResponse>()
            .Which.Message.Should().Be("Setting 'OssAudit:ScheduledHour' must be an integer between 0 and 23.");
        _settings.Verify(service => service.UpsertAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSetting_MissingBody_Is400WithTheFixedDomainMessage()
    {
        var result = await _controller.UpdateSetting("OssAudit:ScheduledHour", null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ErrorResponse>()
            .Which.Message.Should().Contain("OssAudit:ScheduledHour");
    }

    [Fact]
    public async Task UpdateSetting_ValidValue_UpsertsWithTheSessionSubject()
    {
        _settings.Setup(service => service.UpsertAsync(
                "OssAudit:ScheduledHour", " 7 ", "admin-sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAppSetting("OssAudit:ScheduledHour", "7", "db", 123L, "admin-sub-1"));

        var result = await _controller.UpdateSetting(
            "OssAudit:ScheduledHour", new UpdateSettingRequest(" 7 "), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(new AppSettingDto("OssAudit:ScheduledHour", "7", "db", 123L, "admin-sub-1"));
    }

    [Fact]
    public async Task DeleteSetting_UnknownKey_Is404()
    {
        var result = await _controller.DeleteSetting("Not:ASetting", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
        _settings.Verify(service => service.TryDeleteOverrideAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSetting_RegisteredKeyWithoutOverride_Is404()
    {
        _settings.Setup(service => service.TryDeleteOverrideAsync(
                "OssAudit:ScheduledHour", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EffectiveAppSetting?)null);

        var result = await _controller.DeleteSetting("OssAudit:ScheduledHour", CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeOfType<ErrorResponse>()
            .Which.Message.Should().Be("Setting 'OssAudit:ScheduledHour' has no database override to delete.");
    }

    [Fact]
    public async Task DeleteSetting_ExistingOverride_ReturnsThePostDeleteEffectiveValue()
    {
        _settings.Setup(service => service.TryDeleteOverrideAsync(
                "OssAudit:ScheduledHour", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAppSetting("OssAudit:ScheduledHour", "5", "static"));

        var result = await _controller.DeleteSetting("OssAudit:ScheduledHour", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(new AppSettingDto("OssAudit:ScheduledHour", "5", "static"));
    }
}
