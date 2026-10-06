using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;

namespace Admin.WebApi.Authentication;

internal static class AdminOidcRegistration
{
    internal static IServiceCollection AddAdminOidc(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = AdminOidcSettings.Read(configuration, environment);
        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CompactStateDataFormat>();
        services.AddSingleton<MemoryTicketStore>();
        services.AddSingleton<AdminLogoutStateStore>();
        services.AddScoped<AdminPreparedLogout>();
        services.AddHttpClient(AdminPreparedLogout.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            { AllowAutoRedirect = false, UseCookies = false, ActivityHeadersPropagator = null })
            .AddServiceMantleCorrelationIdPropagation();
        services.Configure<HttpClientTraceInstrumentationOptions>(options =>
        {
            var prior = options.FilterHttpRequestMessage;
            options.FilterHttpRequestMessage = request => request.RequestUri?.AbsolutePath != "/oauth2/logout/requests"
                && (prior?.Invoke(request) ?? true);
        });
        services.AddHostedService<OidcStoreCleanup>();
        services.AddScoped<AdminSessionAccessor>();
        services.AddScoped<AdminSessionBoundary>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AdminSessionBoundary.CsrfHeader;
            options.Cookie.Name = "adminCsrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = settings.InsecureLoopback ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        // Every inbound authenticated surface uses the server-side session exclusively.
        var authentication = services.AddAuthentication(AdminOidcSettings.SessionScheme).AddCookie(AdminOidcSettings.SessionScheme, options =>
        {
            options.Cookie.Name = AdminOidcSettings.SessionCookie;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = settings.InsecureLoopback ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = MemoryTicketStore.Lifetime;
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddOptions<CookieAuthenticationOptions>(AdminOidcSettings.SessionScheme)
            .Configure<MemoryTicketStore, TimeProvider>((options, store, time) => { options.SessionStore = store; options.TimeProvider = time; });
        // Hosting diagnostics log raw query strings outside the application middleware.
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None));

        authentication.AddOpenIdConnect(AdminOidcSettings.OidcScheme, options =>
        {
            options.SignInScheme = AdminOidcSettings.SessionScheme;
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.CallbackPath = AdminOidcSettings.CallbackPath;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.SaveTokens = true;
            options.UseTokenLifetime = false;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.ClaimActions.Clear();
            options.DisableTelemetry = true;
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
            options.RequireHttpsMetadata = settings.Authority.StartsWith("https://", StringComparison.Ordinal);
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.RemoteAuthenticationTimeout = CompactStateDataFormat.Lifetime;
            foreach (var cookie in new[] { options.NonceCookie, options.CorrelationCookie })
            {
                cookie.HttpOnly = true;
                cookie.SameSite = SameSiteMode.Lax;
                cookie.SecurePolicy = settings.InsecureLoopback ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                cookie.MaxAge = CompactStateDataFormat.Lifetime;
            }
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = settings.Authority,
                ValidateAudience = true, ValidAudience = settings.ClientId,
                RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                RequireExpirationTime = true, ValidateLifetime = true, ClockSkew = settings.ClockSkew,
                NameClaimType = "name", RoleClaimType = "oidc.roles.not_authorized"
            };
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    context.ProtocolMessage.RedirectUri = settings.RedirectUri;
                    return Task.CompletedTask;
                },
                OnMessageReceived = async context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    var query = context.Request.Query;
                    static bool One(IQueryCollection q, string key) => q.TryGetValue(key, out var values)
                        && values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]);
                    if (!HttpMethods.IsGet(context.Request.Method) || !One(query, "state") || !One(query, "iss")
                        || context.Properties is null || query.ContainsKey("id_token") || query.ContainsKey("access_token")
                        || (query.ContainsKey("error") ? !One(query, "error") || query.ContainsKey("code") : !One(query, "code")))
                    { context.Fail("oidc.invalid_callback"); return; }
                    try
                    {
                        var metadata = await Metadata(context.Options, settings, context.HttpContext.RequestAborted);
                        if (!string.Equals(query["iss"][0], metadata.Issuer, StringComparison.Ordinal)) context.Fail("oidc.invalid_callback");
                    }
                    catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
                    catch { context.Fail("oidc.invalid_callback"); }
                },
                OnTokenResponseReceived = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    var token = context.TokenEndpointResponse;
                    if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.IdToken)
                        || token.TokenType != "Bearer" || !int.TryParse(token.ExpiresIn, out var expires) || expires <= 0
                        || !string.IsNullOrEmpty(token.RefreshToken)) context.Fail("oidc.invalid_token_response");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    var subjects = context.Principal?.FindAll("sub").ToArray() ?? [];
                    if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value)
                        || context.SecurityToken.Issuer != settings.Authority)
                    { context.Fail("oidc.invalid_id_token"); return Task.CompletedTask; }
                    context.Properties!.Items["oidc.issuer"] = context.SecurityToken.Issuer;
                    context.Properties.Items["oidc.subject"] = subjects[0].Value;
                    // Only verified identity and optional display data become local claims; roles are not trusted.
                    var display = context.Principal?.FindFirst("nickname")?.Value ?? context.Principal?.FindFirst("name")?.Value;
                    var claims = new List<Claim> { new("iss", context.SecurityToken.Issuer), new("sub", subjects[0].Value) };
                    if (display is not null) claims.Add(new("display_name", display));
                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AdminOidcSettings.SessionScheme));
                    return Task.CompletedTask;
                },
                OnTicketReceived = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    context.Properties!.IssuedUtc = now;
                    context.Properties.ExpiresUtc = now + MemoryTicketStore.Lifetime;
                    context.Properties.AllowRefresh = false;
                    return Task.CompletedTask;
                },
                OnAccessDenied = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    context.Response.Redirect("/login?authError=cancelled"); context.HandleResponse(); return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    context.Response.Redirect("/login?authError=sign_in_failed"); context.HandleResponse(); return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                    context.Response.Redirect("/login?authError=sign_in_failed"); context.HandleResponse(); return Task.CompletedTask;
                }
            };
        });
        services.AddOptions<OpenIdConnectOptions>(AdminOidcSettings.OidcScheme)
            .Configure<CompactStateDataFormat, TimeProvider>((options, state, time) =>
            {
                options.StateDataFormat = state;
                options.TimeProvider = time;
                options.TokenHandler = new StrictIdTokenHandler(settings, time);
            });
        services.PostConfigure<AuthenticationOptions>(options =>
            options.Schemes.Single(s => s.Name == AdminOidcSettings.OidcScheme).HandlerType = typeof(SafeOpenIdConnectHandler));
        services.AddTransient<SafeOpenIdConnectHandler>();
        // Authorization codes must not enter server telemetry, even if an exporter is added later.
        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var prior = options.Filter;
            options.Filter = context => !context.Request.Path.StartsWithSegments("/api/auth/oidc") && (prior?.Invoke(context) ?? true);
        });
        return services;
    }

    internal static async Task<OpenIdConnectConfiguration> Metadata(OpenIdConnectOptions options, AdminOidcSettings settings, CancellationToken cancellationToken)
    {
        var metadata = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        if (metadata.Issuer != settings.Authority
            || !AdminOidcSettings.IsSafeUri(metadata.AuthorizationEndpoint, !options.RequireHttpsMetadata, out _)
            || !AdminOidcSettings.IsSafeUri(metadata.TokenEndpoint, !options.RequireHttpsMetadata, out _)
            || !AdminOidcSettings.IsSafeUri(metadata.JwksUri, !options.RequireHttpsMetadata, out _)
            || metadata.RequirePushedAuthorizationRequests)
            throw new InvalidOperationException("oidc.invalid_metadata");
        return metadata;
    }

}
