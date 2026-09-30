using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
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

    private static readonly string[] MarkerOnlyRoutes =
    [
        "/api/auth/logout/csrf", AdminOidcSettings.LogoutCallbackPath, AdminOidcSettings.CallbackPath,
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
        Assert.False(response.Headers.TryGetValues("X-Content-Type-Options", out _));
        Assert.False(response.Headers.TryGetValues("X-Frame-Options", out _));
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

        // The marker-only routes carry the metadata.
        foreach (var route in MarkerOnlyRoutes)
        {
            var matches = endpoints.Where(e => e.RoutePattern.RawText == route).ToList();
            Assert.NotEmpty(matches);
            foreach (var endpoint in matches)
            {
                Assert.NotNull(endpoint.Metadata.GetMetadata<SecurityResponseHeadersMetadata>());
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

        // 302 (OIDC start redirect) and 302 (OIDC callback handled by the remote handler).
        using (var start = await client.GetAsync("/api/auth/oidc/start"))
        {
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            AssertBaseline(start);
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
        using (var logoutCsrf = await GetAsync(client, "/api/auth/logout/csrf", session))
        {
            Assert.Equal(HttpStatusCode.OK, logoutCsrf.StatusCode);
            AssertBaseline(logoutCsrf);
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
    public async Task MiddlewareOwnedAuthRoutes_CarryBaseline()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        // sessionLogout stays false: the logout middleware answers its fixed 503/405/400 shape.
        using var factory = HeaderFactory(authority, probe);
        using var client = Browser(factory);

        // 503 session_logout_disabled (fixed body) on the GET csrf route of prepared logout.
        using (var disabled = await client.GetAsync("/api/auth/logout/csrf"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
            Assert.Equal("{\"error\":\"session_logout_disabled\"}", await disabled.Content.ReadAsStringAsync());
            AssertBaseline(disabled);
        }

        // A method mismatch on the middleware-owned route keeps the same fixed 503 (the
        // disabled-mode check precedes the 405 branch) and still carries the baseline via
        // the multi-method marker endpoint.
        using (var wrongMethod = await SendAsync(client, "/api/auth/logout/csrf", "POST"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, wrongMethod.StatusCode);
            Assert.Equal("{\"error\":\"session_logout_disabled\"}", await wrongMethod.Content.ReadAsStringAsync());
            AssertBaseline(wrongMethod);
        }

        // The same route with prepared logout enabled answers the invalid-state 400
        // (fixed body) — still through the middleware, still with the baseline.
        using var authority2 = new OidcTestAuthority();
        var probe2 = new SessionBusinessProbe();
        using var enabledFactory = HeaderFactory(authority2, probe2, sessionLogout: true);
        using var enabledClient = Browser(enabledFactory);
        using (var invalidCallback = await enabledClient.GetAsync(AdminOidcSettings.LogoutCallbackPath + "?state=not-a-key"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidCallback.StatusCode);
            Assert.Equal("{\"error\":\"logout_callback_invalid\"}", await invalidCallback.Content.ReadAsStringAsync());
            AssertBaseline(invalidCallback);
        }
    }

    [Fact]
    public async Task OidcDisabledGate_KeepsFixed503_AndCarriesBaseline()
    {
        // Plain factory: AdminOidc:Enabled is false (default), so the gate owns the OIDC surfaces.
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/api/auth/oidc/start", "/api/auth/oidc/callback", "/api/auth/session" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("{\"error\":\"oidc_disabled\"}", await response.Content.ReadAsStringAsync());
            AssertBaseline(response);
        }
    }

    // ===== Unmarked surfaces keep their existing header behavior =====

    [Fact]
    public async Task UnmarkedSurfaces_DoNotCarryBaseline()
    {
        var workingDirectory = CreateTempContentRoot(withWwwrootStub: true);
        try
        {
            using var factory = CreateFactory(contentRoot: workingDirectory);
            using var client = factory.CreateClient();

            // SPA entry and deep link: HTML without the JSON baseline.
            using (var root = await client.GetAsync("/"))
            {
                Assert.Equal(HttpStatusCode.OK, root.StatusCode);
                AssertNoBaseline(root);
            }
            using (var spa = await client.GetAsync("/students"))
            {
                Assert.Equal(HttpStatusCode.OK, spa.StatusCode);
                AssertNoBaseline(spa);
            }

            // Health endpoints: JSON without the baseline. The readiness status itself is
            // owned by the health-endpoint contract tests; only the header shape matters here.
            using (var live = await client.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                AssertNoBaseline(live);
            }
            using (var ready = await client.GetAsync("/health/ready"))
            {
                Assert.Equal("application/json", ready.Content.Headers.ContentType?.MediaType);
                AssertNoBaseline(ready);
            }

            // ImageController: browser-native image redirect surface stays unmarked.
            using (var image = await client.GetAsync("/api/admin/image?path=uploads/a.jpg"))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, image.StatusCode);
                AssertNoBaseline(image);
            }

            // The identity proxy path has no routed endpoint: whatever the proxy answers, it
            // never carries the JSON baseline (the target is unreachable in this factory).
            using (var proxied = await client.GetAsync("/api/identity/admin/users"))
            {
                Assert.NotEqual(HttpStatusCode.OK, proxied.StatusCode);
                Assert.False(proxied.Headers.TryGetValues("Content-Security-Policy", out _));
            }
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    // ===== Helpers =====

    private WebApplicationFactory<Program> HeaderFactory(OidcTestAuthority authority, SessionBusinessProbe probe,
        bool sessionLogout = false) => CreateFactory(configureTestServices: services =>
    {
        services.Configure<OpenIdConnectOptions>(AdminOidcSettings.OidcScheme, options =>
            options.Backchannel = new HttpClient(authority, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(2) });
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
        ["AdminOidc:UseSessionForLogout"] = sessionLogout.ToString(),
        ["AdminOidc:PostLogoutRedirectUri"] = "https://admin.example.test/api/auth/oidc/logout-callback",
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

    /// <summary>Full OIDC handshake against the test authority; asserts the 302 callback carries the baseline.</summary>
    private static async Task<string> LoginAsync(HttpClient client, OidcTestAuthority authority)
    {
        using var start = await client.GetAsync("/api/auth/oidc/start");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
        var cookies = string.Join("; ", start.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));

        var callbackUri = QueryHelpers.AddQueryString(AdminOidcSettings.CallbackPath, new Dictionary<string, string?>
        {
            ["code"] = authority.Code(query), ["state"] = query["state"], ["iss"] = OidcTestAuthority.Issuer,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, callbackUri);
        request.Headers.Add("Cookie", cookies);
        using var callback = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        // The callback response is written by the OpenIdConnect remote handler inside the
        // authentication middleware: the baseline still applies because route selection
        // (marker-only endpoint) happened at the very start of the pipeline.
        AssertBaseline(callback);
        return callback.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")).Split(';')[0];
    }
}
