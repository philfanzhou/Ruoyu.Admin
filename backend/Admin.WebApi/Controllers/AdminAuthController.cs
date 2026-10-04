using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Admin.WebApi.Controllers;

/// <summary>
/// Identity callback + auth configuration for admin_portal.
///
/// <see cref="Callback"/> is invoked by Identity during token issuance for
/// admin_portal's registered App. It returns <c>["admin"]</c> when the user is in the
/// configured <see cref="AdminPortalOptions.AdminUserIds"/> whitelist, so Identity embeds
/// <c>role:admin</c> into the JWT. Teacher/Assistant portals then accept the proxied request
/// via <c>[Authorize(Roles = "admin")]</c>.
/// </summary>
[Route("api/auth")]
[ApiController, RequireSecurityResponseHeaders]
public class AdminAuthController : ControllerBase
{
    private readonly AdminPortalOptions _options;
    private readonly ILogger<AdminAuthController> _logger;

    public AdminAuthController(
        IOptions<AdminPortalOptions> options,
        ILogger<AdminAuthController> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Retired password endpoint. The pre-authentication boundary returns the same response.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login() => StatusCode(410, new { error = "legacy_login_disabled" });

    /// <summary>
    /// Identity callback: returns the admin role for whitelisted users.
    /// Request body: { "user_id": "&lt;guid&gt;" }.
    /// Response: { "roles": ["admin"] } when whitelisted, otherwise { "roles": [] }.
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    public IActionResult Callback([FromBody] CallbackRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return Ok(new CallbackResponse());
        }

        if (_options.AdminUserIds.Contains(request.UserId, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Identity callback: user {UserId} recognized as admin", request.UserId);
            return Ok(new CallbackResponse { Roles = new List<string> { "admin" } });
        }

        return Ok(new CallbackResponse());
    }

    /// <summary>
    /// Clears the legacy adminAuthToken cookie. Frontend calls
    /// this on logout so subsequent browser-native &lt;img&gt; requests stop carrying
    /// the JWT. localStorage tokens are cleared client-side by services/auth.ts.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("adminAuthToken", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
        return Ok(new { success = true });
    }
}

public class CallbackRequest
{
    public string UserId { get; set; } = "";
}

public class CallbackResponse
{
    public List<string> Roles { get; set; } = new();
}
