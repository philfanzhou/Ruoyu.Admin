using Admin.WebApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// Registry catalog and value-domain tests for the database-hosted application settings
/// (issue #118): the first-version catalog is exactly the two storage-audit schedule keys,
/// defaults come from OssAuditScheduleOptions, and only in-range integers normalize.
/// </summary>
public sealed class AppSettingsRegistryTests
{
    private readonly AppSettingsRegistry _registry = new();

    [Fact]
    public void Catalog_ContainsExactlyTheTwoScheduleKeys()
    {
        _registry.Definitions.Should().HaveCount(2);
        _registry.Definitions.Select(d => d.Key).Should().Equal(
            "OssAudit:ScheduledHour", "OssAudit:ScheduledMinute");
        _registry.Find("OssAudit:ScheduledHour").Should().NotBeNull();
        _registry.Find("OssAudit:ScheduledMinute").Should().NotBeNull();
        _registry.Find("Not:ASetting").Should().BeNull();
        _registry.Find("").Should().BeNull();
    }

    [Fact]
    public void Defaults_ComeFromOssAuditScheduleOptions()
    {
        var defaults = new OssAuditScheduleOptions();
        _registry.Find("OssAudit:ScheduledHour")!.DefaultValue.Should().Be(defaults.ScheduledHour.ToString());
        _registry.Find("OssAudit:ScheduledMinute")!.DefaultValue.Should().Be(defaults.ScheduledMinute.ToString());
    }

    [Theory]
    [InlineData("OssAudit:ScheduledHour", "0", true, "0")]
    [InlineData("OssAudit:ScheduledHour", "23", true, "23")]
    [InlineData("OssAudit:ScheduledHour", " 7 ", true, "7")]
    [InlineData("OssAudit:ScheduledHour", "05", true, "5")]
    [InlineData("OssAudit:ScheduledHour", "-1", false, "")]
    [InlineData("OssAudit:ScheduledHour", "24", false, "")]
    [InlineData("OssAudit:ScheduledHour", "2.5", false, "")]
    [InlineData("OssAudit:ScheduledHour", "abc", false, "")]
    [InlineData("OssAudit:ScheduledHour", "", false, "")]
    [InlineData("OssAudit:ScheduledHour", null, false, "")]
    [InlineData("OssAudit:ScheduledMinute", "59", true, "59")]
    [InlineData("OssAudit:ScheduledMinute", "60", false, "")]
    [InlineData("OssAudit:ScheduledMinute", "+30", true, "30")]
    public void TryNormalize_EnforcesTheIntegerValueDomain(
        string key, string? raw, bool expectedValid, string expectedNormalized)
    {
        var definition = _registry.Find(key)!;

        definition.TryNormalize(raw, out var normalized).Should().Be(expectedValid);
        normalized.Should().Be(expectedNormalized);
    }

    [Fact]
    public void InvalidMessage_IsFixedAndNamesKeyAndConstraint()
    {
        var message = _registry.Find("OssAudit:ScheduledHour")!.InvalidMessage();
        message.Should().Be("Setting 'OssAudit:ScheduledHour' must be an integer between 0 and 23.");
        _registry.Find("OssAudit:ScheduledMinute")!.InvalidMessage().Should()
            .Be("Setting 'OssAudit:ScheduledMinute' must be an integer between 0 and 59.");
    }

    [Fact]
    public void ResolveStaticOrDefault_ValidStaticKeyWins_InvalidFallsBackToDefault()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OssAudit:ScheduledHour"] = "5",
            ["OssAudit:ScheduledMinute"] = "99",
        }).Build();

        var hour = _registry.ResolveStaticOrDefault("OssAudit:ScheduledHour", configuration);
        hour.Source.Should().Be("static");
        hour.Value.Should().Be("5");
        hour.UpdatedAt.Should().BeNull();
        hour.UpdatedBy.Should().BeNull();

        var minute = _registry.ResolveStaticOrDefault("OssAudit:ScheduledMinute", configuration);
        minute.Source.Should().Be("default", "an out-of-domain static value is treated as absent");
        minute.Value.Should().Be("0");
    }

    [Fact]
    public void ResolveStaticOrDefault_NoStaticKey_UsesDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var hour = _registry.ResolveStaticOrDefault("OssAudit:ScheduledHour", configuration);

        hour.Source.Should().Be("default");
        hour.Value.Should().Be("2");
    }

    [Fact]
    public void ResolveStaticOrDefault_UnregisteredKey_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();
        var act = () => _registry.ResolveStaticOrDefault("Not:ASetting", configuration);
        act.Should().Throw<KeyNotFoundException>();
    }
}
