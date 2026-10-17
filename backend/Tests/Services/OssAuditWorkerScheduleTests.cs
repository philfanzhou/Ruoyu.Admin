using Admin.WebApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// Schedule-resolution tests for the audit worker's settings wiring (issue #118): the
/// per-cycle schedule comes from the layered application-settings service (a database
/// override participates in the next run's computation), and a settings-store outage
/// falls back to the static-configuration/default layers instead of stopping the loop.
/// </summary>
public sealed class OssAuditWorkerScheduleTests
{
    private static Task<EffectiveAppSetting> Effective(string key, string value, string source) =>
        Task.FromResult(new EffectiveAppSetting(key, value, source));

    private static OssAuditWorker CreateWorker(
        Mock<IAppSettingsService> settings, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings.Object);
        services.AddSingleton(new AppSettingsRegistry());
        return new OssAuditWorker(
            services.BuildServiceProvider(),
            Mock.Of<ILogger<OssAuditWorker>>(),
            configuration);
    }

    [Fact]
    public async Task ResolveSchedule_DatabaseOverrideParticipatesInTheNextCycle()
    {
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(service => service.GetEffectiveAsync("OssAudit:ScheduledHour", It.IsAny<CancellationToken>()))
            .Returns(Effective("OssAudit:ScheduledHour", "3", "db"));
        settings.Setup(service => service.GetEffectiveAsync("OssAudit:ScheduledMinute", It.IsAny<CancellationToken>()))
            .Returns(Effective("OssAudit:ScheduledMinute", "45", "db"));
        // Static configuration would say 5:00; the database override must win.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OssAudit:ScheduledHour"] = "5",
        }).Build();

        var schedule = await CreateWorker(settings, configuration).ResolveScheduleAsync(CancellationToken.None);

        schedule.Should().Be(new OssAuditWorker.Schedule(3, 45));
    }

    [Fact]
    public async Task ResolveSchedule_ServiceValueDomainGuaranteesIntegerParsing()
    {
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(service => service.GetEffectiveAsync("OssAudit:ScheduledHour", It.IsAny<CancellationToken>()))
            .Returns(Effective("OssAudit:ScheduledHour", "23", "db"));
        settings.Setup(service => service.GetEffectiveAsync("OssAudit:ScheduledMinute", It.IsAny<CancellationToken>()))
            .Returns(Effective("OssAudit:ScheduledMinute", "0", "default"));

        var schedule = await CreateWorker(settings, new ConfigurationBuilder().Build())
            .ResolveScheduleAsync(CancellationToken.None);

        schedule.Should().Be(new OssAuditWorker.Schedule(23, 0));
    }

    [Fact]
    public async Task ResolveSchedule_SettingsStoreOutage_FallsBackToStaticAndDefaultLayers()
    {
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(service => service.GetEffectiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("injected settings-store outage"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OssAudit:ScheduledMinute"] = "45",
        }).Build();

        var schedule = await CreateWorker(settings, configuration).ResolveScheduleAsync(CancellationToken.None);

        schedule.Should().Be(new OssAuditWorker.Schedule(2, 45),
            "the hour falls back to the OssAuditScheduleOptions default and the minute to static configuration");
    }

    [Fact]
    public async Task ResolveSchedule_CancellationPropagates()
    {
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(service => service.GetEffectiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, token) =>
                Task.FromCanceled<EffectiveAppSetting>(token));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => CreateWorker(settings, new ConfigurationBuilder().Build())
            .ResolveScheduleAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ComputeNextRun_ScheduleStillAheadToday_IsTodayAtTheScheduledTime()
    {
        var nextRun = OssAuditWorker.ComputeNextRun(
            new DateTime(2026, 10, 17, 1, 0, 0), 2, 0);
        nextRun.Should().Be(new DateTime(2026, 10, 17, 2, 0, 0));
    }

    [Fact]
    public void ComputeNextRun_ExactlyTheScheduledMoment_RollsToTomorrow()
    {
        var nextRun = OssAuditWorker.ComputeNextRun(
            new DateTime(2026, 10, 17, 2, 0, 0), 2, 0);
        nextRun.Should().Be(new DateTime(2026, 10, 18, 2, 0, 0));
    }

    [Fact]
    public void ComputeNextRun_AlreadyPastToday_RollsToTomorrow()
    {
        var nextRun = OssAuditWorker.ComputeNextRun(
            new DateTime(2026, 10, 17, 23, 30, 0), 0, 30);
        nextRun.Should().Be(new DateTime(2026, 10, 18, 0, 30, 0));
    }

    [Fact]
    public void ComputeNextRun_MonthBoundary_RollsOverTheMonth()
    {
        var nextRun = OssAuditWorker.ComputeNextRun(
            new DateTime(2026, 10, 31, 12, 0, 0), 3, 15);
        nextRun.Should().Be(new DateTime(2026, 11, 1, 3, 15, 0));
    }
}
