using Microsoft.AspNetCore.Antiforgery;

namespace Admin.WebApi.Authentication;

/// <summary>Shared, explicit request boundary for this and later proxy migration slices.</summary>
internal sealed class AdminSessionBoundary(AdminSessionAccessor sessions, IAntiforgery antiforgery)
{
    internal static readonly object TrustedSessionKey = new();
    internal const string CsrfHeader = "X-CSRF-TOKEN";
    // MVC accepts a trailing slash and case-insensitive literal routes. The boundary must
    // cover the same input set, especially the retired password-login action.
    internal static bool IsAuthPath(PathString path, string route) => string.Equals(path.Value?.TrimEnd('/'), route, StringComparison.OrdinalIgnoreCase);
    internal static bool IsSessionPath(PathString path, AdminOidcSettings settings) => path.StartsWithSegments("/api/admin")
        || IsAuthPath(path, "/api/auth/csrf")
        || settings.UseSessionForPortalProxies && (path.StartsWithSegments("/api/teacher-portal") || path.StartsWithSegments("/api/assistant-portal"))
        || settings.UseSessionForIdentityProxy && path.StartsWithSegments("/api/identity");

    internal async Task<bool> ValidateAsync(HttpContext context)
    {
        var session = await sessions.AuthenticateAsync(context);
        if (session.StatusCode != 200)
        {
            await RejectAsync(context, session.StatusCode, session.Error!);
            return false;
        }
        context.User = session.Principal!;
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method))
        {
            if (!context.Request.Headers.TryGetValue(CsrfHeader, out var header)
                || header.Count != 1 || string.IsNullOrWhiteSpace(header[0]))
            {
                await RejectAsync(context, 400, "csrf_invalid");
                return false;
            }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await RejectAsync(context, 400, "csrf_invalid");
                return false;
            }
        }
        context.RequestAborted.ThrowIfCancellationRequested();
        context.Items[TrustedSessionKey] = session;
        return true;
    }

    internal static Task RejectAsync(HttpContext context, int status, string error)
    {
        // The cache-suppression headers previously written here come from the ServiceMantle
        // security response-header baseline instead: rejections happen after route selection,
        // so the marked endpoint's metadata already registered the OnStarting header write.
        // Paths without a marked endpoint (e.g. the three proxy prefixes when session mode
        // fronts them) intentionally carry no baseline, matching the proxy exclusion.
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error }, context.RequestAborted);
    }
}

internal sealed class AdminSessionMiddleware(RequestDelegate next, AdminOidcSettings settings)
{
    public async Task InvokeAsync(HttpContext context, AdminSessionBoundary boundary)
    {
        if ((context.Request.Path.StartsWithSegments("/api/teacher-portal") || context.Request.Path.StartsWithSegments("/api/assistant-portal"))
            && !settings.UseSessionForPortalProxies)
        {
            await AdminSessionBoundary.RejectAsync(context, 503, "session_portal_proxy_disabled");
            return;
        }
        // This aggregate follows the API capability, independently of the portal proxy flag.
        if (!settings.UseSessionForAdminApi && context.Request.Path.StartsWithSegments("/api/admin/students", out var associationPath)
            && associationPath.Value?.TrimEnd('/').EndsWith("/linked-accounts", StringComparison.OrdinalIgnoreCase) == true)
        {
            await AdminSessionBoundary.RejectAsync(context, 503, "session_api_disabled");
            return;
        }
        var csrf = AdminSessionBoundary.IsAuthPath(context.Request.Path, "/api/auth/csrf");
        if (csrf && !settings.UseSessionForAdminApi)
        {
            await AdminSessionBoundary.RejectAsync(context, 503, "session_api_disabled");
            return;
        }
        if (settings.UseSessionForAdminApi)
        {
            // Run before model binding: retired password input is never accepted or forwarded,
            // including invalid JSON, missing body or invalid credentials.
            if (HttpMethods.IsPost(context.Request.Method) && AdminSessionBoundary.IsAuthPath(context.Request.Path, "/api/auth/login"))
            {
                await AdminSessionBoundary.RejectAsync(context, 410, "legacy_login_disabled");
                return;
            }
            if (AdminSessionBoundary.IsSessionPath(context.Request.Path, settings) && !await boundary.ValidateAsync(context)) return;
        }
        await next(context);
    }
}
