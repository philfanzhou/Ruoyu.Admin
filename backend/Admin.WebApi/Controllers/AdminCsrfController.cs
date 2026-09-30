using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.WebApi.Controllers;

[ApiController, Route("api/auth/csrf"), Authorize, RequireSecurityResponseHeaders]
public sealed class AdminCsrfController(IAntiforgery antiforgery) : ControllerBase
{
    // AdminSessionMiddleware has verified the current administrator and token before this endpoint.
    [HttpGet]
    public IActionResult Get()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        // Cache suppression comes from the ServiceMantle security response-header baseline
        // (RequireSecurityResponseHeaders on this controller), not per-action header writes.
        return Ok(new { requestToken = tokens.RequestToken });
    }
}
