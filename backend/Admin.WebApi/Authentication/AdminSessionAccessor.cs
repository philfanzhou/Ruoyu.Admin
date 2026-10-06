using System.Security.Claims;
using Microsoft.Extensions.Options;
using SignaCore.Client.AspNetCore;

namespace Admin.WebApi.Authentication;

/// <summary>
/// Reads only the SignaCore hosted-login server-side session ticket, never browser credentials
/// or mixed <see cref="HttpContext.User"/> claims. The session cookie carries the opaque store
/// key alone, so resolving the store directly keeps the Admin CSRF boundary principal-bound
/// (the package's session handler would validate it user-neutrally) while the ticket, the
/// access token, and the ID token never leave the server. An unknown, revoked, and expired
/// session are indistinguishable by design: the store reclaims expired tickets on read.
/// </summary>
internal sealed class AdminSessionAccessor(AdminOidcSettings settings, IOptionsMonitor<AdminPortalOptions> admins,
    IOptionsMonitor<SignaCoreHostedLoginOptions> login, ITicketStore tickets)
{
    internal async Task<AdminSessionResult> AuthenticateAsync(HttpContext context)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (context.Request.Headers.ContainsKey("Authorization")) return new(401);
        var cookieName = login.CurrentValue.SessionCookieName;
        var key = context.Request.Cookies[cookieName];
        if (string.IsNullOrEmpty(key)) return new(401);
        var ticket = await tickets.RetrieveAsync(key, context.RequestAborted);
        context.RequestAborted.ThrowIfCancellationRequested();
        if (ticket is null) return new(401);
        var principal = ticket.Principal;
        var issuers = principal.FindAll("iss").ToArray();
        var subjects = principal.FindAll("sub").ToArray();
        // Only the ticket's own verified identity claims decide; roles are never trusted.
        if (principal.Identity?.IsAuthenticated != true || issuers.Length != 1 || subjects.Length != 1
            || issuers[0].Value != settings.Authority || string.IsNullOrWhiteSpace(subjects[0].Value)) return new(401);
        if (!admins.CurrentValue.AdminUserIds.Contains(subjects[0].Value, StringComparer.OrdinalIgnoreCase)) return new(403);
        // Only the verified identity and the display name become the boundary principal; the
        // ticket's other ID-token claims (including any role claim) are never trusted here,
        // matching the previous contract where roles could not authorize anything.
        var display = principal.FindFirst("nickname")?.Value ?? principal.FindFirst("name")?.Value;
        var identity = new ClaimsIdentity(
        [
            new Claim("iss", issuers[0].Value),
            new Claim("sub", subjects[0].Value),
            ..(display is null ? [] : new[] { new Claim("display_name", display) })
        ], "AdminSession");
        return new(200, new ClaimsPrincipal(identity), ticket.AccessToken, displayName: display);
    }

    internal string? SessionKey(HttpContext context)
    {
        var key = context.Request.Cookies[login.CurrentValue.SessionCookieName];
        return string.IsNullOrEmpty(key) ? null : key;
    }
}

// Keep secrets out of implicit record/diagnostic formatting.
internal sealed class AdminSessionResult(int statusCode, ClaimsPrincipal? principal = null, string? accessToken = null,
    string? error = null, string? displayName = null)
{
    internal int StatusCode { get; } = statusCode;
    internal string? Error { get; } = error ?? (statusCode == 403 ? "forbidden" : statusCode == 401 ? "unauthorized" : null);
    internal string? DisplayName { get; } = displayName;
    internal ClaimsPrincipal? Principal { get; } = principal;
    internal string? AccessToken { get; } = accessToken;
    public override string ToString() => $"AdminSessionResult({StatusCode})";
}
