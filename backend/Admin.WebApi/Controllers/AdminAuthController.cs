using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Admin.WebApi.Controllers;

/// <summary>
/// Identity callback + auth configuration for admin_portal.
///
/// <see cref="Callback"/> is invoked by Identity during token issuance (password grant via
/// admin_portal's registered App). It returns <c>["admin"]</c> when the user is in the
/// configured <see cref="AdminPortalOptions.AdminUserIds"/> whitelist, so Identity embeds
/// <c>role:admin</c> into the JWT. Teacher/Assistant portals then accept the proxied request
/// via <c>[Authorize(Roles = "admin")]</c>.
/// </summary>
[Route("api/auth")]
[ApiController]
public class AdminAuthController : ControllerBase
{
    private readonly AdminPortalOptions _options;
    private readonly IdentityServiceOptions _identityOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdminAuthController> _logger;

    public AdminAuthController(
        IOptions<AdminPortalOptions> options,
        IOptions<IdentityServiceOptions> identityOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<AdminAuthController> logger)
    {
        _options = options.Value;
        _identityOptions = identityOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Admin login: exchanges username/password for a JWT via Identity password grant.
    /// Identity calls back to <see cref="Callback"/>, which returns <c>role:admin</c> for
    /// whitelisted users. The returned access/refresh tokens are stored in localStorage by
    /// the frontend and sent as <c>Authorization: Bearer</c> on subsequent requests.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { success = false, message = "Username and password are required." });
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("IdentityService");

            // Present admin_portal's AppId/AppSecret so Identity triggers the admin callback
            // (injects role:admin into the issued JWT).
            // NOTE: Identity's TokenRequest contract uses camelCase field names (grantType, username,
            // password). The legacy snake_case "grant_type" does NOT bind — ASP.NET Core's
            // PropertyNameCaseInsensitive can't match across the added underscore, leaving GrantType
            // empty and Identity returning "unsupported_grant_type". See Identity docs:
            // docs/deploy-test/02-test-cases.md.
            var tokenRequest = new
            {
                grantType = "password",
                username = request.Username,
                password = request.Password
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_identityOptions.Authority.TrimEnd('/')}/api/auth/token")
            {
                Content = JsonContent.Create(tokenRequest)
            };
            httpRequest.Headers.TryAddWithoutValidation("X-Admin-AppId", _identityOptions.AppId);
            httpRequest.Headers.TryAddWithoutValidation("X-Admin-AppSecret", _identityOptions.AppSecret);

            using var response = await client.SendAsync(httpRequest);
            var content = await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (!response.IsSuccessStatusCode || content == null || !content.Success)
            {
                _logger.LogWarning("Admin login failed for user {Username}: {Message}", request.Username, content?.Message);
                return BadRequest(new { success = false, message = content?.Message ?? "Login failed." });
            }

            return Ok(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin login error for user {Username}", request.Username);
            return StatusCode(502, new { success = false, message = "Identity service unavailable." });
        }
    }

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
}

public class CallbackRequest
{
    public string UserId { get; set; } = "";
}

public class CallbackResponse
{
    public List<string> Roles { get; set; } = new();
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public long ExpiresIn { get; set; }
    public long ExpiresAt { get; set; }
}
