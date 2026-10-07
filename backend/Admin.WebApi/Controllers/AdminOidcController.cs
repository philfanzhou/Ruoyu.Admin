using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.WebApi.Controllers;

/// <summary>
/// The Admin SPA's session-status surface. The hosted-login protocol endpoints (start,
/// callback, csrf, logout, logout/return) are owned by the SignaCore client package and are
/// mapped in Program.cs; this controller keeps the stable <c>/api/auth/session</c> JSON
/// contract on top of the package's server-side ticket.
/// </summary>
[ApiController, AllowAnonymous, Route("api/auth"), RequireSecurityResponseHeaders]
public sealed class AdminOidcController : ControllerBase
{
    [HttpGet("session")]
    public async Task<IActionResult> Session()
    {
        var session = await HttpContext.RequestServices.GetRequiredService<AdminSessionAccessor>().AuthenticateAsync(HttpContext);
        // The API surface never falls back to browser credentials: an Authorization header is
        // rejected with the accessor's fixed 401 even though no bearer scheme exists.
        if (Request.Headers.ContainsKey("Authorization") || session.StatusCode == 403)
        {
            await AdminSessionBoundary.RejectAsync(HttpContext, session.StatusCode, session.Error!);
            return new EmptyResult();
        }
        // The accessor rejects an Authorization header with 401 itself; an expired or absent
        // ticket is indistinguishable, and the still-present cookie is the reauthentication signal.
        var hasSessionCookie = Request.Cookies.ContainsKey(AdminOidcSettings.SessionCookie);
        return Ok(new
        {
            authenticated = session.StatusCode == 200,
            displayName = session.StatusCode == 200 ? session.DisplayName : null,
            requiresReauthentication = session.StatusCode != 200 && hasSessionCookie
        });
    }
}
