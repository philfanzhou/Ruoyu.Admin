using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using SignaCore.Client.AspNetCore;

namespace Admin.WebApi.Authentication;

/// <summary>
/// Thin composition over <c>SignaCore.Client.AspNetCore</c>: the package owns the protocol and
/// security duties of the hosted login (authorization code + PKCE, hardened callback, the
/// server-side ticket store, the pending-sign-in state, the prepared logout, and the
/// user-neutral antiforgery boundary of its own endpoints). This registration keeps only the
/// Admin configuration contract and the business authorization adapters.
/// </summary>
internal static class AdminOidcRegistration
{
    internal static IServiceCollection AddAdminOidc(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = AdminOidcSettings.Read(configuration, environment);
        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSignaCoreHostedLogin(options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.RedirectUri = settings.RedirectUri;
            options.Scope = "openid profile";
            // The browser keeps the same opaque session cookie name as before the migration.
            options.SessionCookieName = AdminOidcSettings.SessionCookie;
            // One antiforgery header serves both faces: the package's logout endpoint and the
            // Admin business boundary (which additionally binds its pairs to the principal).
            options.AntiforgeryHeaderName = AdminSessionBoundary.CsrfHeader;
            options.PostLogoutRedirectUri = settings.PostLogoutRedirectUri;
            options.PostLogoutReturnPath = "/login";
        });
        // The package's shared backchannel joins the correlation contract of every other
        // internal client (issue #90): an ambient request correlation id travels with the
        // Discovery, token, and logout preparations; without a request scope nothing is added.
        services.AddHttpClient(SignaCoreHostedLoginDefaults.HttpClientName)
            .AddServiceMantleCorrelationIdPropagation();
        // The adapters need live IOptionsMonitor<AdminPortalOptions> (the whitelist is mutable
        // at runtime), which the static configure lambda cannot resolve; wire them once when
        // the options are first built, before ValidateOnStart evaluates them.
        services.AddOptions<SignaCoreHostedLoginOptions>()
            .PostConfigure<IServiceProvider>((options, provider) =>
            {
                options.AuthorizationDecision = new AdminSessionAuthorizationDecision(
                    provider.GetRequiredService<AdminOidcSettings>(),
                    provider.GetRequiredService<IOptionsMonitor<AdminPortalOptions>>());
                options.PreSignInAuthorizationDecision = new AdminPreSignInAuthorizationDecision(
                    provider.GetRequiredService<IOptionsMonitor<AdminPortalOptions>>());
                options.ResponseWriter = AdminHostedLoginResponseWriter.Instance;
            });
        // The shared antiforgery pair keeps the Admin cookie contract: the package defaults
        // (name-less Strict cookie, Always secure) would rename the browser cookie and break
        // token issuance on plain-HTTP entry origins. SecurePolicy follows the configured entry
        // scheme (transport security is a deployment decision, issue #94): an HTTP redirect
        // origin maps to SameAsRequest; HTTPS paths keep Always.
        services.PostConfigure<AntiforgeryOptions>(options =>
        {
            options.Cookie.Name = "adminCsrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = settings.InsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        // No default authenticate scheme: every inbound authenticated surface resolves the
        // server ticket through AdminSessionAccessor/AdminSessionBoundary exclusively, which
        // keeps the business CSRF boundary principal-bound. Challenge and forbid fall back to
        // the package's forwarding scheme, so a protected endpoint outside the session
        // boundary redirects to the hosted-login start (or answers 403) instead of failing on
        // a missing default scheme.
        services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = SignaCoreHostedLoginDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = SignaCoreHostedLoginDefaults.AuthenticationScheme;
        });
        services.AddScoped<AdminSessionAccessor>();
        services.AddScoped<AdminSessionBoundary>();
        // Hosting diagnostics log raw query strings outside the application middleware.
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None));
        // Authorization codes must not enter server telemetry, even if an exporter is added later.
        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var prior = options.Filter;
            options.Filter = context => !context.Request.Path.StartsWithSegments(AdminOidcSettings.HostedLoginPrefix) && (prior?.Invoke(context) ?? true);
        });
        return services;
    }
}
