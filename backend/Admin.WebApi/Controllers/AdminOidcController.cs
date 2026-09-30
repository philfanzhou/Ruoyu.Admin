using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Admin.WebApi.Controllers;

[ApiController, AllowAnonymous, Route("api/auth")]
public sealed class AdminOidcController : ControllerBase
{
    [HttpGet("oidc/start")]
    public async Task<IActionResult> Start([FromQuery] string? returnUrl)
    {
        var settings = HttpContext.RequestServices.GetRequiredService<AdminOidcSettings>();
        if (!settings.Enabled) return StatusCode(503, new { error = "oidc_disabled" });
        var options = HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AdminOidcSettings.OidcScheme);
        try { await AdminOidcRegistration.Metadata(options, settings, HttpContext.RequestAborted); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch { return Redirect("/login?authError=identity_unavailable"); }
        HttpContext.RequestAborted.ThrowIfCancellationRequested();
        return Challenge(new AuthenticationProperties { RedirectUri = AdminOidcSettings.ReturnPath(returnUrl) }, AdminOidcSettings.OidcScheme);
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session()
    {
        var settings = HttpContext.RequestServices.GetRequiredService<AdminOidcSettings>();
        if (!settings.Enabled) return StatusCode(503, new { error = "oidc_disabled" });
        var result = await HttpContext.AuthenticateAsync(AdminOidcSettings.SessionScheme);
        return Ok(new { authenticated = result.Succeeded, displayName = result.Succeeded ? result.Principal?.FindFirst("display_name")?.Value : null });
    }
}
