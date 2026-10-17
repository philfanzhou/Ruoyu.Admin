using Admin.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Admin.WebApi.Services;

/// <summary>
/// The effective value of one registered setting plus the layer it was resolved from.
/// <see cref="UpdatedAt"/> and <see cref="UpdatedBy"/> are only set for the database layer.
/// </summary>
public sealed record EffectiveAppSetting(
    string Key,
    string Value,
    string Source,
    long? UpdatedAt = null,
    string? UpdatedBy = null)
{
    public override string ToString() => $"AppSetting({Key}, source: {Source})";
}

/// <summary>
/// Layered resolution and persistence of the database-hosted application settings
/// (issue #118). Every registered key resolves exactly one way: a database row wins
/// (source <c>db</c>), then a valid same-name static configuration key (source
/// <c>static</c>), then the options-class default (source <c>default</c>). Resolution is a
/// pure read with no side effects; deleting a row falls back to the lower layers. Writes
/// are last-writer-wins primary-key upserts — single-admin scenario, no optimistic locking.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>The effective values of all registered keys, ordered by key.</summary>
    Task<IReadOnlyList<EffectiveAppSetting>> ListEffectiveAsync(CancellationToken cancellationToken = default);

    /// <summary>The effective value of one registered key.</summary>
    /// <exception cref="KeyNotFoundException">The key is not registered.</exception>
    Task<EffectiveAppSetting> GetEffectiveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the raw value against the registry and upserts the database override.
    /// Invalid values never reach the database.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The key is not registered.</exception>
    /// <exception cref="ArgumentException">The raw value fails the registry value domain.</exception>
    Task<EffectiveAppSetting> UpsertAsync(string key, string rawValue, string? updatedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the database override and returns the post-delete effective value, or
    /// <see langword="null"/> when no override row exists.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The key is not registered.</exception>
    Task<EffectiveAppSetting?> TryDeleteOverrideAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class AppSettingsService(
    AuditDbContext dbContext,
    AppSettingsRegistry registry,
    IConfiguration configuration) : IAppSettingsService
{
    public async Task<IReadOnlyList<EffectiveAppSetting>> ListEffectiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.AppSettings.ToDictionaryAsync(row => row.Key, cancellationToken);
        return registry.Definitions
            .Select(definition => Resolve(definition, rows.GetValueOrDefault(definition.Key)))
            .ToArray();
    }

    public async Task<EffectiveAppSetting> GetEffectiveAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = RequireDefinition(key);
        var row = await dbContext.AppSettings.SingleOrDefaultAsync(row => row.Key == key, cancellationToken);
        return Resolve(definition, row);
    }

    public async Task<EffectiveAppSetting> UpsertAsync(string key, string rawValue, string? updatedBy, CancellationToken cancellationToken = default)
    {
        var definition = RequireDefinition(key);
        if (!definition.TryNormalize(rawValue, out var normalized))
            throw new ArgumentException(definition.InvalidMessage(), nameof(rawValue));

        var row = await dbContext.AppSettings.SingleOrDefaultAsync(row => row.Key == key, cancellationToken);
        if (row is null)
        {
            row = new AppSetting { Key = key };
            dbContext.AppSettings.Add(row);
        }
        row.Value = normalized;
        row.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        row.UpdatedBy = updatedBy;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new EffectiveAppSetting(key, row.Value, AppSettingsRegistry.SourceDatabase, row.UpdatedAt, row.UpdatedBy);
    }

    public async Task<EffectiveAppSetting?> TryDeleteOverrideAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = RequireDefinition(key);
        var row = await dbContext.AppSettings.SingleOrDefaultAsync(row => row.Key == key, cancellationToken);
        if (row is null)
            return null;
        dbContext.AppSettings.Remove(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return registry.ResolveStaticOrDefault(definition.Key, configuration);
    }

    private AppSettingDefinition RequireDefinition(string key) =>
        registry.Find(key) ?? throw new KeyNotFoundException($"Setting '{key}' is not registered.");

    private EffectiveAppSetting Resolve(AppSettingDefinition definition, AppSetting? row) =>
        row is null
            ? registry.ResolveStaticOrDefault(definition.Key, configuration)
            : new EffectiveAppSetting(definition.Key, row.Value, AppSettingsRegistry.SourceDatabase, row.UpdatedAt, row.UpdatedBy);
}
