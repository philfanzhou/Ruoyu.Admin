using System.Security.Claims;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.WebApi.Controllers;

/// <summary>
/// Management API for the database-hosted application settings (issue #118). Reads list
/// every registered key with its effective value and source (<c>db</c> / <c>static</c> /
/// <c>default</c>); writes upsert or delete the database override only. Unregistered keys
/// are refused with 404 and never create rows. The session and CSRF gates are the shared
/// <c>/api/admin</c> boundary (401 without a session, 400 <c>csrf_invalid</c> without a
/// bound CSRF token); database outages surface through the shared ProblemDetails boundary
/// with fixed error codes. Setting values never enter the log — only key names and sources.
/// </summary>
[Route("api/admin/settings")]
[ApiController, RequireSecurityResponseHeaders]
[Authorize]
public sealed class AppSettingsController : ControllerBase
{
    private readonly IAppSettingsService _settings;
    private readonly AppSettingsRegistry _registry;
    private readonly ILogger<AppSettingsController> _logger;

    public AppSettingsController(
        IAppSettingsService settings,
        AppSettingsRegistry registry,
        ILogger<AppSettingsController> logger)
    {
        _settings = settings;
        _registry = registry;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _settings.ListEffectiveAsync(cancellationToken);
        return Ok(settings.Select(ToDto).ToArray());
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSettingRequest? body, CancellationToken cancellationToken)
    {
        if (_registry.Find(key) is not { } definition)
            return NotFound(new ErrorResponse($"Setting '{key}' is not a registered application setting."));
        var rawValue = body?.Value;
        if (rawValue is null || !definition.TryNormalize(rawValue, out _))
            return BadRequest(new ErrorResponse(definition.InvalidMessage()));

        var updatedBy = User.FindFirstValue("sub");
        var effective = await _settings.UpsertAsync(key, rawValue, updatedBy, cancellationToken);
        _logger.LogInformation("Application setting {SettingKey} saved (source: database)", key);
        return Ok(ToDto(effective));
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> DeleteSetting(string key, CancellationToken cancellationToken)
    {
        if (_registry.Find(key) is null)
            return NotFound(new ErrorResponse($"Setting '{key}' is not a registered application setting."));
        var effective = await _settings.TryDeleteOverrideAsync(key, cancellationToken);
        if (effective is null)
            return NotFound(new ErrorResponse($"Setting '{key}' has no database override to delete."));

        _logger.LogInformation("Application setting {SettingKey} override deleted (effective source: {SettingSource})",
            key, effective.Source);
        return Ok(ToDto(effective));
    }

    private static AppSettingDto ToDto(EffectiveAppSetting setting) =>
        new(setting.Key, setting.Value, setting.Source, setting.UpdatedAt, setting.UpdatedBy);
}

public sealed record UpdateSettingRequest(string? Value);

/// <summary>The wire shape of one effective setting; <c>updatedAt</c>/<c>updatedBy</c> exist only for the <c>db</c> source.</summary>
public sealed record AppSettingDto(string Key, string Value, string Source, long? UpdatedAt = null, string? UpdatedBy = null);
