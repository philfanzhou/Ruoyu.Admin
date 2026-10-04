using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Admin.WebApi.Authentication;

internal sealed class AdminPreparedLogout(AdminOidcSettings settings, MemoryTicketStore tickets, AdminLogoutStateStore states,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions, IAntiforgery antiforgery, IHttpClientFactory clients, TimeProvider time)
{
    internal const string ClientName = "AdminPreparedLogout";
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";
    private const string BindingPrefix = "adminLogout.";
    private const int MaxResponseBytes = 4096;

    // This identity check grants only the ability to end one's own ticket. Neither access-token
    // lifetime nor the mutable administrator whitelist can strand a user in a local session.
    private bool IsIdentity(AuthenticationTicket? ticket)
    {
        if (ticket?.Principal.Identity?.IsAuthenticated != true) return false;
        var issuers = ticket.Principal.FindAll("iss").ToArray();
        var subjects = ticket.Principal.FindAll("sub").ToArray();
        return issuers.Length == 1 && issuers[0].Value == settings.Authority
            && subjects.Length == 1 && !string.IsNullOrWhiteSpace(subjects[0].Value)
            && ticket.Properties.Items.TryGetValue("oidc.issuer", out var issuer) && issuer == issuers[0].Value
            && ticket.Properties.Items.TryGetValue("oidc.subject", out var subject) && subject == subjects[0].Value;
    }
    private async Task<AuthenticationTicket?> Identity(HttpContext context)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (!settings.Enabled || context.Request.Headers.ContainsKey("Authorization")) return null;
        var result = await context.AuthenticateAsync(AdminOidcSettings.SessionScheme);
        return result.Succeeded && IsIdentity(result.Ticket) ? result.Ticket : null;
    }
    private string? ReferenceKey(HttpContext context)
    {
        var options = cookieOptions.Get(AdminOidcSettings.SessionScheme);
        var value = options.CookieManager.GetRequestCookie(context, options.Cookie.Name!);
        var reference = value is null ? null : options.TicketDataFormat.Unprotect(value);
        var claims = reference?.Principal.FindAll(SessionIdClaim).ToArray();
        return claims is { Length: 1 } && reference!.Principal.Claims.Count() == 1 && AdminLogoutStateStore.IsKey(claims[0].Value)
            ? claims[0].Value : null;
    }
    private async Task Clear(HttpContext context)
    {
        await context.SignOutAsync(AdminOidcSettings.SessionScheme);
        context.Response.Cookies.Delete("adminAuthToken", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
    }
    internal async Task Csrf(HttpContext context)
    {
        var identity = await Identity(context);
        if (identity is null) { await AdminSessionBoundary.RejectAsync(context, 401, "unauthorized"); return; }
        context.User = identity.Principal;
        var tokens = antiforgery.GetAndStoreTokens(context);
        await context.Response.WriteAsJsonAsync(new { requestToken = tokens.RequestToken }, context.RequestAborted);
    }
    internal async Task Logout(HttpContext context)
    {
        var identity = await Identity(context);
        if (identity is null)
        {
            // An injected Authorization must not mutate the legitimate Cookie session.
            if (!context.Request.Headers.ContainsKey("Authorization")) await Clear(context);
            await AdminSessionBoundary.RejectAsync(context, 401, "unauthorized"); return;
        }
        context.User = identity.Principal;
        if (!context.Request.Headers.TryGetValue(AdminSessionBoundary.CsrfHeader, out var header)
            || header.Count != 1 || string.IsNullOrWhiteSpace(header[0]))
        { await AdminSessionBoundary.RejectAsync(context, 400, "csrf_invalid"); return; }
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        { await AdminSessionBoundary.RejectAsync(context, 400, "csrf_invalid"); return; }
        context.RequestAborted.ThrowIfCancellationRequested();
        var key = ReferenceKey(context);
        var taken = key is null ? null : tickets.Take(key);
        await Clear(context);
        if (taken is null || !IsIdentity(taken) || taken.Principal.FindFirst("sub")?.Value != identity.Principal.FindFirst("sub")?.Value)
        { await AdminSessionBoundary.RejectAsync(context, 401, "unauthorized"); return; }
        var idToken = taken.Properties.GetTokenValue("id_token");
        string? state = null, logoutUrl = null;
        var binding = AdminLogoutStateStore.RandomKey();
        try
        {
            if (!string.IsNullOrWhiteSpace(idToken) && idToken.Length <= 8192 && idToken.All(c => c is >= ' ' and <= '~')
                && (state = states.Create(binding)) is not null)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var cancellation = deadline.Token;
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.Authority + "/oauth2/logout/requests")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
                        ["id_token_hint"] = idToken, ["post_logout_redirect_uri"] = settings.PostLogoutRedirectUri, ["state"] = state
                    })
                };
                // The private client has no loggers, cookies, redirect or retry handlers. Suppress
                // outbound protocol diagnostics: only a validated opaque URL may reach the browser.
                using var suppression = SuppressInstrumentationScope.Begin();
                using var client = clients.CreateClient(ClientName);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                    var buffer = new byte[MaxResponseBytes + 1]; var length = 0;
                    while (length < buffer.Length)
                    {
                        var read = await stream.ReadAsync(buffer.AsMemory(length), cancellation);
                        if (read == 0) break;
                        length += read;
                    }
                    if (length <= MaxResponseBytes)
                    {
                        using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
                        if (json.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var fields = json.RootElement.EnumerateObject().ToArray();
                            if (fields.Length == 1 && fields[0].Name == "logout_uri" && fields[0].Value.ValueKind == JsonValueKind.String)
                                logoutUrl = ValidateUrl(fields[0].Value.GetString());
                        }
                    }
                }
            }
        }
        catch { /* Fixed local-only result. Local revocation is never rolled back or retried. */ }
        if (logoutUrl is null)
        {
            if (state is not null) states.Remove(state);
            await context.Response.WriteAsJsonAsync(new { success = true, upstreamLogout = false }, context.RequestAborted);
            return;
        }
        try
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            context.Response.Cookies.Append(BindingPrefix + state, binding, BindingCookie(context));
            await context.Response.WriteAsJsonAsync(new { success = true, upstreamLogout = true, logoutUrl }, context.RequestAborted);
        }
        catch
        {
            states.Remove(state!); // A failed release cannot leave a usable completion state.
            throw;
        }
    }
    internal string? ValidateUrl(string? value)
    {
        const string prefix = "/oauth2/logout?logout_handle=";
        if (value is null || value.Any(char.IsControl)) return null;
        var origin = new Uri(settings.Authority).GetLeftPart(UriPartial.Authority);
        var relative = value.StartsWith(prefix, StringComparison.Ordinal) ? value
            : value.StartsWith(origin + prefix, StringComparison.Ordinal) ? value[origin.Length..] : null;
        return relative is not null && AdminLogoutStateStore.IsKey(relative[prefix.Length..]) ? origin + relative : null;
    }
    private CookieOptions BindingCookie(HttpContext context) => new()
    {
        HttpOnly = true, Secure = !settings.InsecureLoopback || context.Request.IsHttps, SameSite = SameSiteMode.Lax,
        Path = AdminOidcSettings.LogoutCallbackPath, MaxAge = AdminLogoutStateStore.Lifetime,
        Expires = time.GetUtcNow() + AdminLogoutStateStore.Lifetime
    };
    internal async Task Complete(HttpContext context)
    {
        var query = context.Request.Query;
        var state = query.TryGetValue("state", out var values) && values.Count == 1 ? values[0] : null;
        var binding = AdminLogoutStateStore.IsKey(state) ? context.Request.Cookies[BindingPrefix + state] : null;
        if (query.Count != 1 || !states.Consume(state, binding))
        { await AdminSessionBoundary.RejectAsync(context, 400, "logout_callback_invalid"); return; }
        context.Response.Cookies.Delete(BindingPrefix + state, BindingCookie(context));
        context.Response.Redirect("/login?loggedOut=1");
    }
}

internal sealed class AdminLogoutMiddleware(RequestDelegate next, AdminOidcSettings settings)
{
    public async Task InvokeAsync(HttpContext context, AdminPreparedLogout logout)
    {
        var csrf = AdminSessionBoundary.IsAuthPath(context.Request.Path, "/api/auth/logout/csrf");
        var callback = AdminSessionBoundary.IsAuthPath(context.Request.Path, AdminOidcSettings.LogoutCallbackPath);
        var post = AdminSessionBoundary.IsAuthPath(context.Request.Path, "/api/auth/logout");
        if (!csrf && !callback && !post) { await next(context); return; }
        // The cache-suppression headers previously written here come from the ServiceMantle
        // security response-header baseline: these paths carry marker-only route endpoints
        // (mapped in Program.cs), and route selection happens before this middleware.
        if (!settings.UseSessionForLogout)
        { await AdminSessionBoundary.RejectAsync(context, 503, "session_logout_disabled"); return; }
        if ((post && !HttpMethods.IsPost(context.Request.Method))
            || ((csrf || callback) && !HttpMethods.IsGet(context.Request.Method)))
        { context.Response.StatusCode = 405; return; }
        if (csrf) await logout.Csrf(context);
        else if (callback) await logout.Complete(context);
        else await logout.Logout(context);
    }
}
