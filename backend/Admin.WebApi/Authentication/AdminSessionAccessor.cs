using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Admin.WebApi.Authentication;

/// <summary>Reads only the explicitly authenticated server ticket, never browser credentials or mixed User claims.</summary>
internal sealed class AdminSessionAccessor(AdminOidcSettings settings, IOptionsMonitor<AdminPortalOptions> admins, TimeProvider time)
{
    internal async Task<AdminSessionResult> AuthenticateAsync(HttpContext context)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (!settings.Enabled || context.Request.Headers.ContainsKey("Authorization")) return new(401);
        var authenticated = await context.AuthenticateAsync(AdminOidcSettings.SessionScheme);
        context.RequestAborted.ThrowIfCancellationRequested();
        if (!authenticated.Succeeded || authenticated.Principal is null || authenticated.Properties is null) return new(401);
        var principal = authenticated.Principal;
        var issuers = principal.FindAll("iss").ToArray();
        var subjects = principal.FindAll("sub").ToArray();
        var properties = authenticated.Properties;
        if (principal.Identity?.IsAuthenticated != true || issuers.Length != 1 || subjects.Length != 1
            || issuers[0].Value != settings.Authority || string.IsNullOrWhiteSpace(subjects[0].Value)
            || !properties.Items.TryGetValue("oidc.issuer", out var issuer) || issuer != issuers[0].Value
            || !properties.Items.TryGetValue("oidc.subject", out var subject) || subject != subjects[0].Value) return new(401);
        if (!admins.CurrentValue.AdminUserIds.Contains(subject, StringComparer.OrdinalIgnoreCase)) return new(403);
        var token = properties.GetTokenValue("access_token");
        var deadline = properties.GetTokenValue("expires_at");
        // Framework SaveTokens writes round-trip ISO timestamps. No culture-dependent dates,
        // missing offsets, permissive parsing or session-lifetime fallback may authorize a request.
        if (string.IsNullOrWhiteSpace(token)
            || !DateTimeOffset.TryParseExact(deadline, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)
            || deadline != expires.ToString("o", CultureInfo.InvariantCulture)) return new(401);
        if (time.GetUtcNow() >= expires) return new(401, error: "reauthentication_required", displayName: principal.FindFirst("display_name")?.Value);
        return new(200, principal, token);
    }
}

// Keep secrets out of implicit record/diagnostic formatting.
internal sealed class AdminSessionResult(int statusCode, ClaimsPrincipal? principal = null, string? accessToken = null, string? error = null, string? displayName = null)
{
    internal int StatusCode { get; } = statusCode;
    internal string? Error { get; } = error ?? (statusCode == 403 ? "forbidden" : statusCode == 401 ? "unauthorized" : null);
    internal string? DisplayName { get; } = displayName;
    internal ClaimsPrincipal? Principal { get; } = principal;
    internal string? AccessToken { get; } = accessToken;
    public override string ToString() => $"AdminSessionResult({StatusCode})";
}
