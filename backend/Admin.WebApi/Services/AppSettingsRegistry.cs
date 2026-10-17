using Microsoft.Extensions.Configuration;

namespace Admin.WebApi.Services;

/// <summary>
/// The catalog of the database-hosted application settings (issue #118). Every key the
/// settings storage and the management API know about is registered here with its value
/// domain and the default inherited from the owning options class. The first-version
/// catalog is the two storage-audit schedule keys — the only application-level runtime
/// adjustable settings that exist today. Credentials, downstream URLs, the admin-user
/// whitelist, CORS, and authentication keys deliberately stay out: they belong to the
/// static configuration layer and the security boundary.
/// </summary>
public sealed class AppSettingsRegistry
{
    public const string OssAuditScheduledHourKey = "OssAudit:ScheduledHour";
    public const string OssAuditScheduledMinuteKey = "OssAudit:ScheduledMinute";

    public const string SourceDatabase = "db";
    public const string SourceStaticConfiguration = "static";
    public const string SourceDefault = "default";

    private readonly Dictionary<string, AppSettingDefinition> _definitions;

    public AppSettingsRegistry() : this(
    [
        new AppSettingDefinition(
            OssAuditScheduledHourKey,
            "Local hour of day (0-23) at which the scheduled storage audit runs.",
            minInclusive: 0,
            maxInclusive: 23,
            defaultValue: new OssAuditScheduleOptions().ScheduledHour),
        new AppSettingDefinition(
            OssAuditScheduledMinuteKey,
            "Local minute of hour (0-59) at which the scheduled storage audit runs.",
            minInclusive: 0,
            maxInclusive: 59,
            defaultValue: new OssAuditScheduleOptions().ScheduledMinute),
    ])
    {
    }

    internal AppSettingsRegistry(IEnumerable<AppSettingDefinition> definitions) =>
        _definitions = definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);

    /// <summary>The registered definitions, ordered by key.</summary>
    public IReadOnlyList<AppSettingDefinition> Definitions =>
        _definitions.Values.OrderBy(definition => definition.Key, StringComparer.Ordinal).ToArray();

    public AppSettingDefinition? Find(string key) =>
        _definitions.TryGetValue(key, out var definition) ? definition : null;

    /// <summary>
    /// Resolves the two database-free layers of the settings model: a valid static
    /// configuration value (<see cref="IConfiguration"/> — Consul, environment variables,
    /// appsettings) wins; otherwise the registered options-class default applies. A static
    /// value that fails the value domain is treated as absent rather than surfacing an
    /// unusable schedule. Used both by the resolution service (when no database row exists)
    /// and by callers that must keep working while the settings store is unreachable.
    /// </summary>
    internal EffectiveAppSetting ResolveStaticOrDefault(string key, IConfiguration configuration)
    {
        var definition = Find(key) ?? throw new KeyNotFoundException($"Setting '{key}' is not registered.");
        var staticValue = configuration[definition.Key];
        if (staticValue is not null && definition.TryNormalize(staticValue, out var normalized))
            return new EffectiveAppSetting(definition.Key, normalized, SourceStaticConfiguration);
        return new EffectiveAppSetting(definition.Key, definition.DefaultValue, SourceDefault);
    }
}
