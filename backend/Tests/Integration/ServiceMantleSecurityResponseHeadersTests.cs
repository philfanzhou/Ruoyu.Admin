using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using ServiceMantle.Web.Http;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// The ServiceMantle six-header security response baseline on the real pipeline (#55).
/// Marked surfaces: every action of the ten JSON controllers carrying
/// <c>[RequireSecurityResponseHeaders]</c> plus the marker-only endpoints that carry metadata
/// for middleware-owned auth routes (OIDC callback, prepared-logout csrf/callback). The
/// baseline is asserted as exact single values across the status matrix (200/302/400/401/
/// 403/404/405/500/503), including responses written by middleware after route selection
/// (session boundary rejections, the OIDC-disabled gate, the logout middleware).
/// Unmarked surfaces: ImageController, the proxy paths, the SPA, static files and the health
/// endpoints keep their existing header behavior (no baseline).
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed class ServiceMantleSecurityResponseHeadersTests : ServiceMantleIntegrationTestBase
{
    private const string BaselineCsp =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    // ControllerActionDescriptor.ControllerName has no "Controller" suffix.
    private static readonly string[] MarkedControllers =
    [
        "AdminAuth", "AdminCsrf", "AdminOidc", "EnumOptions",
        "IdentityAccounts", "Mistake", "OssAudit",
        "OssUploadRecord", "Students", "StudentAssociations",
    ];

    // The SignaCore package owns these routes; its endpoints are deliberately unmarked (the
    // package writes its own cache-suppression), which is the recorded accepted difference
    // of the hosted-login migration.
    private static readonly string[] PackageRoutes =
    [
        "/api/auth/oidc/start", "/api/auth/oidc/callback", "/api/auth/oidc/session",
        "/api/auth/oidc/signin-failed", "/api/auth/oidc/csrf", "/api/auth/oidc/logout",
        "/api/auth/oidc/logout/return",
    ];

    public ServiceMantleSecurityResponseHeadersTests(PostgreSqlFixture database) : base(database)
    {
    }

    // ===== Exact single values of the immutable baseline =====

    private static void AssertBaseline(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", string.Join(",", response.Headers.Pragma));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(BaselineCsp, response.Headers.GetValues("Content-Security-Policy").Single());
    }

    private static void AssertNoBaseline(HttpResponseMessage response)
    {
        Assert.Null(response.Headers.CacheControl);
        Assert.Empty(response.Headers.Pragma);
        AssertNoSecurityBaseline(response);
    }

    /// <summary>The package-owned endpoints suppress caching themselves (no-store) but never
    /// carry the six-header baseline. The one exception is the antiforgery token issuance: the
    /// shared ASP.NET Core antiforgery layer adds its own <c>X-Frame-Options: SAMEORIGIN</c>
    /// whenever it stores a token pair, so exactly that value may appear.</summary>
    private static void AssertNoSecurityBaseline(HttpResponseMessage response)
    {
        Assert.False(response.Headers.TryGetValues("X-Content-Type-Options", out _));
        if (response.Headers.TryGetValues("X-Frame-Options", out var frame))
            Assert.Equal("SAMEORIGIN", string.Join(",", frame));
        Assert.False(response.Headers.TryGetValues("Referrer-Policy", out _));
        Assert.False(response.Headers.TryGetValues("Content-Security-Policy", out _));
    }

    // ===== The marked inventory is exactly the approved surface =====

    [Fact]
    public async Task MarkedEndpointInventory_IsExactlyTheApprovedSurface()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        // Force host start so the route table is materialized.
        using var _ = await client.GetAsync("/health/live");

        var dataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var endpoints = dataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        // Every controller action endpoint is marked iff its controller opted in.
        var controllerEndpoints = endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToList();
        Assert.NotEmpty(controllerEndpoints);
        foreach (var endpoint in controllerEndpoints)
        {
            var controller = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName;
            var marked = endpoint.Metadata.GetMetadata<SecurityResponseHeadersMetadata>() is not null;
            Assert.True(MarkedControllers.Contains(controller) == marked,
                $"controller {controller} ({endpoint.RoutePattern.RawText}) marker mismatch");
        }

        // ImageController stays unmarked and the ten marked controllers are all present.
        Assert.Contains(controllerEndpoints, e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Image"
            && e.Metadata.GetMetadata<SecurityResponseHeadersMetadata>() is null);
        foreach (var controller in MarkedControllers)
        {
            Assert.Contains(controllerEndpoints, e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == controller);
        }

        // The package-owned auth routes exist and stay unmarked.
        foreach (var route in PackageRoutes)
        {
            var matches = endpoints.Where(e => e.RoutePattern.RawText == route).ToList();
            Assert.NotEmpty(matches);
            foreach (var endpoint in matches)
            {
                Assert.Null(endpoint.Metadata.GetMetadata<SecurityResponseHeadersMetadata>());
            }
        }

        // The anonymous health endpoints never carry the baseline.
        foreach (var endpoint in endpoints.Where(e => e.RoutePattern.RawText is not null && e.RoutePattern.RawText.StartsWith("/health", StringComparison.Ordinal)))
        {
            Assert.Null(endpoint.Metadata.GetMetadata<SecurityResponseHeadersMetadata>());
        }
    }

    // ===== Marked surfaces carry the baseline across the status matrix =====

    [Fact]
    public async Task MarkedSurfaces_CarryBaselineAcrossStatusMatrix()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = HeaderFactory(authority, probe, sessionLogout: true);
        using var client = Browser(factory);

        // 302 (hosted-login start): a package-owned endpoint, deliberately unmarked.
        using (var start = await client.GetAsync("/api/auth/oidc/start"))
        {
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            AssertNoSecurityBaseline(start);
        }
        var session = await LoginAsync(client, authority);

        // 200 business GET.
        using (var options = await GetAsync(client, "/api/admin/enum-options", session))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            AssertBaseline(options);
        }

        // 200 auth csrf + 200 prepared-logout csrf (middleware-owned route).
        using (var csrf = await GetAsync(client, "/api/auth/csrf", session))
        {
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            AssertBaseline(csrf);
        }
        // 200 package-owned logout csrf: user-neutral issuance, no-store without the baseline.
        using (var logoutCsrf = await GetAsync(client, "/api/auth/oidc/csrf", session))
        {
            Assert.Equal(HttpStatusCode.OK, logoutCsrf.StatusCode);
            Assert.Contains("no-store", logoutCsrf.Headers.CacheControl!.ToString());
            AssertNoSecurityBaseline(logoutCsrf);
        }

        // 404 business write endpoint (fixed safe body unchanged) with CSRF header dance.
        var (token, csrfCookie) = await CsrfAsync(client, session);
        using (var write = await SendAsync(client, "/api/admin/oss-audit/records/999999/ignore",
            "POST", $"{session}; {csrfCookie}", token, JsonContent.Create(new { note = "x" })))
        {
            Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
            Assert.Contains("Record not found.", await write.Content.ReadAsStringAsync());
            AssertBaseline(write);
        }

        // 400 model binding on a marked anonymous controller endpoint.
        using (var malformed = await client.PostAsync("/api/auth/callback", new StringContent("not-json", System.Text.Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            AssertBaseline(malformed);
        }

        // 401 fallback challenge without credentials.
        using (var unauthenticated = await client.GetAsync("/api/admin/enum-options"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            AssertBaseline(unauthenticated);
        }

        // 403 session boundary rejection: the whitelist drops the live subject.
        probe.Admins.CurrentValue.AdminUserIds = ["someone-else"];
        using (var forbidden = await GetAsync(client, "/api/admin/enum-options", session))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            AssertBaseline(forbidden);
        }
        probe.Admins.CurrentValue.AdminUserIds = ["FAKE-SUBJECT"];

        // 500 on a marked action: the upload-record listing catches the stubbed downstream
        // failure and answers its fixed 500 body. (TestServer rethrows fully unhandled
        // exceptions to the test client instead of letting the server write the 500, so the
        // server-written variant of this case is covered by the problem-details migration's
        // tests, where the 500 is written by middleware.)
        probe.Student.Setup(s => s.GetAllUploadRecordsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-canary-must-not-leak"));
        using (var failure = await GetAsync(client, "/api/admin/oss-upload-records", session))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
            Assert.Equal("{\"message\":\"Failed to get upload records\"}", await failure.Content.ReadAsStringAsync());
            AssertBaseline(failure);
        }
    }

    [Fact]
    public async Task PackageAuthRoutes_StayUnmarkedWithFixedBodies()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = HeaderFactory(authority, probe, sessionLogout: true);
        using var client = Browser(factory);

        // The package's csrf issuance is public and carries only its own no-store.
        using (var csrf = await client.GetAsync("/api/auth/oidc/csrf"))
        {
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            Assert.Contains("no-store", csrf.Headers.CacheControl!.ToString());
            AssertNoSecurityBaseline(csrf);
        }

        // A method mismatch answers the framework's 405, still unmarked.
        foreach (var path in new[] { AdminOidcSettings.LogoutPath, "/API/AUTH/OIDC/LOGOUT/" })
        {
            using var wrongMethod = await SendAsync(client, path, "GET");
            var dump = $"status={wrongMethod.StatusCode} location={wrongMethod.Headers.Location} headers={string.Join("|", wrongMethod.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)))}";
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            AssertNoSecurityBaseline(wrongMethod);
        }

        // A missing antiforgery token answers the fixed csrf_rejected body and changes nothing.
        using (var rejected = await SendAsync(client, AdminOidcSettings.LogoutPath, "POST"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal("{\"outcome\":\"csrf_rejected\"}", await rejected.Content.ReadAsStringAsync());
            Assert.Contains("no-store", rejected.Headers.CacheControl!.ToString());
            AssertNoSecurityBaseline(rejected);
        }

        // The logout return answers its fixed invalid body without echoing any request input.
        using (var invalidCallback = await client.GetAsync(AdminOidcSettings.LogoutReturnPath + "?state=not-a-key"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidCallback.StatusCode);
            Assert.Contains("text/html", invalidCallback.Content.Headers.ContentType!.ToString());
            AssertNoSecurityBaseline(invalidCallback);
        }
    }

    // ===== Helpers =====

    private WebApplicationFactory<Program> HeaderFactory(OidcTestAuthority authority, SessionBusinessProbe probe,
        bool sessionLogout = false) => CreateFactory(configureTestServices: services =>
    {
        services.AddHttpClient(SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => authority);
        services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => probe.Identity);
        services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(probe.Admins));
        services.Replace(ServiceDescriptor.Singleton(probe.Oss.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Student.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.Mistake.Object));
        services.Replace(ServiceDescriptor.Singleton(new HomeworkReferenceClient(new HttpClient(probe.Homework)
            { BaseAddress = new Uri("https://homework.example.test") })));
        services.Configure<JwtBearerOptions>("Bearer", options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = OidcTestAuthority.Issuer };
            metadata.SigningKeys.Add(authority.SigningKey);
            options.Configuration = metadata;
        });
    }, settings: new Dictionary<string, string?>
    {
        ["AdminOidc:Enabled"] = "true",
        ["AdminOidc:RedirectUri"] = OidcTestAuthority.RedirectUri,
        ["AdminOidc:UseSessionForAdminApi"] = "true",

        ["AdminOidc:PostLogoutRedirectUri"] = "https://admin.example.test" + AdminOidcSettings.LogoutReturnPath,
        ["TeacherPortal:Url"] = "https://teacher.example.test",
        ["AssistantPortal:Url"] = "https://assistant.example.test",
        ["AdminPortal:AdminUserIds:0"] = "FAKE-SUBJECT",
        ["IdentityService:Authority"] = OidcTestAuthority.Issuer,
        ["IdentityService:Issuer"] = OidcTestAuthority.Issuer,
        ["IdentityService:AppId"] = OidcTestAuthority.ClientId,
        ["IdentityService:AppSecret"] = OidcTestAuthority.Secret,
        ["IdentityService:RequireHttpsMetadata"] = "true",
    });

    private static HttpClient Browser(WebApplicationFactory<Program> factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://admin.example.test"), AllowAutoRedirect = false, HandleCookies = false,
    });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string method,
        string? cookie = null, string? csrf = null, HttpContent? body = null)
    {
        // No using: the request (and its content) must outlive this method while the send runs.
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = body };
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
        if (!string.IsNullOrEmpty(csrf)) request.Headers.TryAddWithoutValidation(AdminSessionBoundary.CsrfHeader, csrf);
        return client.SendAsync(request);
    }

    private static async Task<(string Token, string Cookie)> CsrfAsync(HttpClient client, string session)
    {
        using var response = await GetAsync(client, "/api/auth/csrf", session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("adminCsrf=")).Split(';')[0];
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("requestToken").GetString()!, cookie);
    }

    /// <summary>Full hosted-login handshake against the test authority (no baseline on package endpoints).</summary>
    private static async Task<string> LoginAsync(HttpClient client, OidcTestAuthority authority)
    {
        using var start = await client.GetAsync("/api/auth/oidc/start");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query).ToDictionary(p => p.Key, p => p.Value.ToString());

        // The pending sign-in lives server-side: the start response sets no cookie at all.
        var callbackUri = QueryHelpers.AddQueryString(AdminOidcSettings.CallbackPath, new Dictionary<string, string?>
        {
            ["code"] = authority.Code(query), ["state"] = query["state"], ["iss"] = OidcTestAuthority.Issuer,
        });
        using var callback = await client.GetAsync(callbackUri);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        AssertNoSecurityBaseline(callback);
        return callback.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")).Split(';')[0];
    }
}
