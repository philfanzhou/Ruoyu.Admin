using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SignaCore.Client.AspNetCore;

namespace Admin.WebApi.Authentication;

/// <summary>
/// The pre-sign-in gate of the administrator whitelist: the package invokes it after both
/// tokens are verified and correlated and before any ticket or cookie is written. Only a
/// timely <c>Allowed</c> for a subject on the mutable whitelist establishes a new session; a
/// denial — or a decision failure or timeout inside the package's bounded wait — leaves
/// existing tickets and cookies untouched and ends in the fixed cancelled failure redirect.
/// </summary>
internal sealed class AdminPreSignInAuthorizationDecision(IOptionsMonitor<AdminPortalOptions> admins)
    : ISignaCorePreSignInAuthorizationDecision
{
    public ValueTask<SignaCoreAuthorizationDecisionResult> DecideAsync(
        SignaCorePreSignInAuthorizationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = admins.CurrentValue.AdminUserIds.Contains(
            context.Subject, StringComparer.OrdinalIgnoreCase);
        return ValueTask.FromResult(allowed
            ? SignaCoreAuthorizationDecisionResult.Allowed
            : SignaCoreAuthorizationDecisionResult.Denied);
    }
}

/// <summary>
/// The session-status decision of the same mutable whitelist: it decides whether an EXISTING
/// verified session still belongs to an administrator. It never removes a session; the
/// package only reports the result on its session endpoint.
/// </summary>
internal sealed class AdminSessionAuthorizationDecision(AdminOidcSettings settings, IOptionsMonitor<AdminPortalOptions> admins)
    : ISignaCoreAuthorizationDecision
{
    public ValueTask<SignaCoreAuthorizationDecisionResult> DecideAsync(
        ClaimsPrincipal subject, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issuers = subject.FindAll("iss").ToArray();
        var subjects = subject.FindAll("sub").ToArray();
        var allowed = issuers.Length == 1 && issuers[0].Value == settings.Authority
            && subjects.Length == 1 && !string.IsNullOrWhiteSpace(subjects[0].Value)
            && admins.CurrentValue.AdminUserIds.Contains(subjects[0].Value, StringComparer.OrdinalIgnoreCase);
        return ValueTask.FromResult(allowed
            ? SignaCoreAuthorizationDecisionResult.Allowed
            : SignaCoreAuthorizationDecisionResult.Denied);
    }
}

/// <summary>
/// Presents the package's protocol outcomes in the Admin SPA contract: sign-in failures
/// redirect to <c>/login?authError=...</c> with the closed reason set of the previous
/// self-written implementation, and the package's session endpoint answers the same JSON
/// shape as <c>/api/auth/session</c>, including the Authorization-header rejection and the
/// whitelist's fixed 403. Bodies never carry a token, code, or query string of the flow.
/// </summary>
internal sealed class AdminHostedLoginResponseWriter : ISignaCoreHostedLoginResponseWriter
{
    public static AdminHostedLoginResponseWriter Instance { get; } = new();

    private AdminHostedLoginResponseWriter()
    {
    }

    public Task WriteSignInFailureAsync(
        HttpContext context, SignaCoreSignInReason reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Response.Redirect("/login?authError=" + AuthError(reason));
        return Task.CompletedTask;
    }

    public Task WriteFailurePageAsync(
        HttpContext context, SignaCoreSignInReason? reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Response.Redirect("/login?authError=" + (reason is { } value ? AuthError(value) : "sign_in_failed"));
        return Task.CompletedTask;
    }

    public async Task WriteSessionStatusAsync(
        HttpContext context, SignaCoreSessionStatus status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Response.Headers.CacheControl = "no-store";
        // The API surface never falls back to browser credentials, matching the boundary of
        // /api/auth/session.
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            await AdminSessionBoundary.RejectAsync(context, 401, "unauthorized");
            return;
        }
        if (status.Authenticated && status.Authorization == SignaCoreAuthorizationDecisionResult.Denied)
        {
            await AdminSessionBoundary.RejectAsync(context, 403, "forbidden");
            return;
        }
        // The ticket store reclaims expired sessions, so an expired session is reported by the
        // still-present cookie — exactly the reauthentication signal the SPA consumes — while a
        // browser without any session cookie stays plain anonymous.
        var hasSessionCookie = !string.IsNullOrEmpty(context.Request.Cookies[AdminOidcSettings.SessionCookie]);
        await context.Response.WriteAsJsonAsync(new
        {
            authenticated = status.Authenticated,
            displayName = status.Authenticated ? status.DisplayName : null,
            requiresReauthentication = !status.Authenticated && hasSessionCookie
        }, cancellationToken);
    }

    private static string AuthError(SignaCoreSignInReason reason) => reason switch
    {
        SignaCoreSignInReason.AuthorityUnreachable => "identity_unavailable",
        SignaCoreSignInReason.AccessDenied => "cancelled",
        _ => "sign_in_failed"
    };
}
