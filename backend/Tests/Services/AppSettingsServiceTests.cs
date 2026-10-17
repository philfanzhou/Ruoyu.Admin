using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// Layered-resolution and persistence tests for the application settings service
/// (issue #118): database override beats static configuration beats the options-class
/// default, deleting a row falls back to the lower layers, and invalid values never
/// reach the database.
/// </summary>
public sealed class AppSettingsServiceTests
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private readonly AppSettingsRegistry _registry = new();
    private readonly IConfiguration _configuration;

    public AppSettingsServiceTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OssAudit:ScheduledHour"] = "5",
        }).Build();
    }

    private AppSettingsService CreateService(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString(), _databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(
                CoreEventId.ManyServiceProvidersCreatedWarning, InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppSettingsService(new AuditDbContext(options), _registry, _configuration);
    }

    // ---------- layered resolution ----------

    [Fact]
    public async Task GetEffective_NoRowNoStaticKey_ResolvesDefault()
    {
        var service = CreateService();
        // Static configuration has OssAudit:ScheduledHour=5 but nothing for the minute.

        var minute = await service.GetEffectiveAsync("OssAudit:ScheduledMinute");

        minute.Should().Be(new EffectiveAppSetting("OssAudit:ScheduledMinute", "0", "default"));
    }

    [Fact]
    public async Task GetEffective_StaticKeyWithoutRow_ResolvesStatic()
    {
        var service = CreateService();

        var hour = await service.GetEffectiveAsync("OssAudit:ScheduledHour");

        hour.Should().Be(new EffectiveAppSetting("OssAudit:ScheduledHour", "5", "static"));
    }

    [Fact]
    public async Task GetEffective_DatabaseRowBeatsStaticConfiguration()
    {
        var databaseName = Guid.NewGuid().ToString();
        await CreateService(databaseName).UpsertAsync("OssAudit:ScheduledHour", "7", "admin-sub-1");

        var hour = await CreateService(databaseName).GetEffectiveAsync("OssAudit:ScheduledHour");

        hour.Key.Should().Be("OssAudit:ScheduledHour");
        hour.Value.Should().Be("7");
        hour.Source.Should().Be("db");
        hour.UpdatedAt.Should().BeGreaterThan(0);
        hour.UpdatedBy.Should().Be("admin-sub-1");
    }

    [Fact]
    public async Task GetEffective_UnregisteredKey_Throws()
    {
        var service = CreateService();
        var act = () => service.GetEffectiveAsync("Not:ASetting");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ListEffective_ReturnsAllRegisteredKeysOrderedByKey()
    {
        var service = CreateService();

        var settings = await service.ListEffectiveAsync();

        settings.Select(s => s.Key).Should().Equal("OssAudit:ScheduledHour", "OssAudit:ScheduledMinute");
        settings.Select(s => s.Source).Should().Equal("static", "default");
        settings.Should().OnlyContain(s => s.UpdatedAt == null && s.UpdatedBy == null);
    }

    // ---------- persistence ----------

    [Fact]
    public async Task Upsert_InsertsNormalizedRowWithAuditFields()
    {
        var databaseName = Guid.NewGuid().ToString();
        var service = CreateService(databaseName);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var result = await service.UpsertAsync("OssAudit:ScheduledMinute", " 45 ", "admin-sub-2");

        result.Source.Should().Be("db");
        result.Value.Should().Be("45");
        result.UpdatedAt.Should().BeInRange(before, before + 60);
        result.UpdatedBy.Should().Be("admin-sub-2");
        (await CreateService(databaseName).ListEffectiveAsync())
            .Single(s => s.Key == "OssAudit:ScheduledMinute").Value.Should().Be("45");
    }

    [Fact]
    public async Task Upsert_UpdatesExistingRow_LastWriterWins()
    {
        var databaseName = Guid.NewGuid().ToString();
        await CreateService(databaseName).UpsertAsync("OssAudit:ScheduledHour", "7", "admin-sub-1");

        var result = await CreateService(databaseName).UpsertAsync("OssAudit:ScheduledHour", "9", "admin-sub-2");

        result.Value.Should().Be("9");
        result.UpdatedBy.Should().Be("admin-sub-2");
        (await CreateService(databaseName).ListEffectiveAsync())
            .Single(s => s.Key == "OssAudit:ScheduledHour").Value.Should().Be("9");
    }

    [Fact]
    public async Task Upsert_InvalidValue_IsRejectedBeforeTheDatabase()
    {
        var databaseName = Guid.NewGuid().ToString();
        var service = CreateService(databaseName);

        var act = () => service.UpsertAsync("OssAudit:ScheduledHour", "24", "admin-sub");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Setting 'OssAudit:ScheduledHour' must be an integer between 0 and 23.*");
        (await CreateService(databaseName).ListEffectiveAsync())
            .Single(s => s.Key == "OssAudit:ScheduledHour").Source.Should().Be("static",
                "the rejected value never created a database row");
    }

    [Fact]
    public async Task Upsert_UnregisteredKey_ThrowsWithoutWriting()
    {
        var databaseName = Guid.NewGuid().ToString();
        var service = CreateService(databaseName);

        var act = () => service.UpsertAsync("Not:ASetting", "1", "admin-sub");

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await CreateService(databaseName).ListEffectiveAsync())
            .Should().OnlyContain(s => s.Source == "static" || s.Source == "default");
    }

    [Fact]
    public async Task DeleteOverride_RemovesRowAndFallsBackToLowerLayers()
    {
        var databaseName = Guid.NewGuid().ToString();
        var service = CreateService(databaseName);
        await service.UpsertAsync("OssAudit:ScheduledHour", "7", "admin-sub-1");

        var effective = await service.TryDeleteOverrideAsync("OssAudit:ScheduledHour");

        effective!.Key.Should().Be("OssAudit:ScheduledHour");
        effective.Value.Should().Be("5");
        effective.Source.Should().Be("static", "deleting the row exposes the static layer again");
        (await CreateService(databaseName).ListEffectiveAsync())
            .Single(s => s.Key == "OssAudit:ScheduledHour").Source.Should().Be("static");
    }

    [Fact]
    public async Task DeleteOverride_WithoutRow_ReturnsNull()
    {
        var service = CreateService();

        var effective = await service.TryDeleteOverrideAsync("OssAudit:ScheduledHour");

        effective.Should().BeNull();
    }

    [Fact]
    public async Task DeleteOverride_UnregisteredKey_Throws()
    {
        var service = CreateService();
        var act = () => service.TryDeleteOverrideAsync("Not:ASetting");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
