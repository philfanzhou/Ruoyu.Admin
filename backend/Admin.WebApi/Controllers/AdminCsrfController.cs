using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.WebApi.Controllers;

[ApiController, Route("api/auth/csrf"), Authorize]
public sealed class AdminCsrfController(IAntiforgery antiforgery) : ControllerBase
{
    // AdminSessionMiddleware has verified the current administrator and token before this endpoint.
    [HttpGet]
    public IActionResult Get()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
        return Ok(new { requestToken = tokens.RequestToken });
    }
}
