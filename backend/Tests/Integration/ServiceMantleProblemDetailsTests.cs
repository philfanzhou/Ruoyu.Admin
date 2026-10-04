using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using Admin.WebApi.Services;
using Admin.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// The ServiceMantle safe Problem Details boundary on the real pipeline (#56). Unhandled
/// exceptions escaping a JSON controller action answer deterministic application/problem+json
/// (exactly type/title/status/correlationId/errorCode; mapped HttpRequestException -&gt; 502
/// downstream.unavailable, anything else -&gt; 500 http.internal_server_error) with no exception
/// message, stack or Data in the response. Endpoint-returned business JSON (validation 4xx,
/// fixed-text 5xx catches, auth 401/403) is unchanged; the image surface, health endpoints and
/// SPA never enter the boundary; caller cancellation propagates; a response that already
/// started is left exactly as sent.
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed class ServiceMantleProblemDetailsTests : ServiceMantleIntegrationTestBase
{
    public ServiceMantleProblemDetailsTests(PostgreSqlFixture database) : base(database)
    {
    }

    private static void AssertProblemShape(HttpResponseMessage response, int status, string errorCode, string title)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = Assert.IsType<System.Text.Json.JsonElement>(JsonSerializer.Deserialize<JsonElement>(
            response.Content.ReadAsStringAsync().GetAwaiter().GetResult()));
        // exactly the five fixed fields, no more
        var names = body.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "correlationId", "errorCode", "status", "title", "type" }, names);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, body.GetProperty("errorCode").GetString());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal("urn:servicemantle:error:" + errorCode, body.GetProperty("type").GetString());
        // the body correlation id equals the response header
        Assert.Equal(body.GetProperty("correlationId").GetString(),
            response.Headers.GetValues(CorrelationHeaderName).Single());
    }

    private static async Task AssertNoLeakAsync(HttpResponseMessage response, params string[] canaries)
    {
        var raw = await response.Content.ReadAsStringAsync();
        foreach (var canary in canaries)
        {
            Assert.DoesNotContain(canary, raw);
        }
    }

    [Fact]
    public async Task UnknownException_ReturnsFixedProblemDetails()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        probe.Student.Setup(s => s.ListStudentsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-canary-unknown-exception"));
        using var factory = ProbeFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);

        using var response = await GetAsync(client, "/api/admin/students", session);

        AssertProblemShape(response, 500, "http.internal_server_error", "An unexpected error occurred.");
        await AssertNoLeakAsync(response, "secret-canary-unknown-exception", "InvalidOperationException", "StudentsController");
    }

    [Fact]
    public async Task OssResolve_DeniesCleanupWithoutCallingDeleteAndKeepsTheAuditRecord()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        probe.Oss.Setup(s => s.DeleteAsync(It.IsAny<string>()))
            .ThrowsAsync(new IOException("secret-canary-oss-delete-failure"));
        using var factory = ProbeFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);

        long recordId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var record = new OssAuditRecord { ObjectPath = "uploads/orphan.jpg", Bucket = "ruoyu-study", Size = 1, LastModified = 1, CreatedAt = 1 };
            db.OssAuditRecords.Add(record);
            await db.SaveChangesAsync();
            recordId = record.Id;
        }

        var (token, csrfCookie) = await CsrfAsync(client, session);
        using var response = await SendAsync(client, $"/api/admin/oss-audit/records/{recordId}/resolve", "POST",
            $"{session}; {csrfCookie}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cleanup_not_authorized", rejected.GetProperty("errorKind").GetString());
        probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        await AssertNoLeakAsync(response, "secret-canary-oss-delete-failure", "IOException", "Failed to delete object");

        // Reference proof cannot authorize deletion; its existing audit trail remains.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            Assert.True(await db.OssAuditRecords.AnyAsync(r => r.Id == recordId));
        }
    }

    [Fact]
    public async Task DownstreamException_MapsTo502DownstreamUnavailable()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        probe.Student.Setup(s => s.ListStudentsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("secret-canary-downstream-text", null, HttpStatusCode.InternalServerError));
        using var factory = ProbeFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);

        using var response = await GetAsync(client, "/api/admin/students", session);

        AssertProblemShape(response, 502, "downstream.unavailable", "A downstream service request failed.");
        await AssertNoLeakAsync(response, "secret-canary-downstream-text");
    }

    [Fact]
    public async Task CallerCancellation_Propagates_InsteadOfAProblemBody()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        var accessor = new HttpContextAccessor();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Student.Setup(s => s.ListStudentsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (int? _, int _, int _, string? _, CancellationToken _) =>
            {
                entered.TrySetResult();
                // The controller calls the client without a token, so the wait is bound to
                // the request's own abort signal instead of the (default None) parameter.
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan,
                        accessor.HttpContext?.RequestAborted ?? CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    observedCancellation.TrySetResult();
                    throw;
                }
                return new PagedStudentsResult();
            });
        using var factory = ProbeFactory(authority, probe,
            extraServices: services => services.AddSingleton<IHttpContextAccessor>(accessor));
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);
        using var cts = new CancellationTokenSource();

        var request = GetAsync(client, "/api/admin/students", session, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        // The caller observes its own cancellation - no 200, no fake problem body.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StartedResponse_IsLeftExactlyAsSent()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = ProbeFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);

        // The probe controller (test assembly, mapped via AddApplicationPart) starts the
        // response, flushes, and then throws: the library's documented boundary leaves the
        // already-sent status and bytes untouched and only writes a safe log. TestServer
        // keeps the flushed bytes for this in-process JSON path.
        using var response = await GetAsync(client, "/api/test-started-response", session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("partial-body", raw);
        Assert.DoesNotContain("http.internal_server_error", raw);
        Assert.DoesNotContain("secret-canary-started-response", raw);
    }

    [Fact]
    public async Task Business4xxResponses_KeepTheirExistingBodies()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        using var factory = ProbeFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);

        // Endpoint-returned validation 400: same {message} JSON, not problem+json.
        var (token, csrfCookie) = await CsrfAsync(client, session);
        using (var invalid = await SendAsync(client, "/api/admin/students", "POST",
            $"{session}; {csrfCookie}", token, JsonContent.Create(new { name = "", grade = 1, identityAccountIds = new List<string>() })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("application/json", invalid.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"message\":\"Name is required.\"}", await invalid.Content.ReadAsStringAsync());
        }

        // The retired password login keeps its fixed 410 JSON.
        using (var retired = await SendAsync(client, "/api/auth/login", "POST",
            body: JsonContent.Create(new { username = "x", password = "y" })))
        {
            Assert.Equal(HttpStatusCode.Gone, retired.StatusCode);
            Assert.Equal("{\"error\":\"legacy_login_disabled\"}", await retired.Content.ReadAsStringAsync());
        }

        // Auth challenges keep the 401 shape (no body, no problem conversion).
        using (var unauthenticated = await client.GetAsync("/api/admin/students"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            Assert.NotEqual("application/problem+json", unauthenticated.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task UnmarkedSurfaces_DoNotEnterTheProblemBoundary()
    {
        using var authority = new OidcTestAuthority();
        var probe = new SessionBusinessProbe();
        probe.Student.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("secret-canary-image-path", null, HttpStatusCode.BadGateway));
        using var factory = ProbeFactory(authority, probe);
        using var client = Browser(factory);
        var session = await LoginAsync(client, authority);

        // ImageController keeps its own fixed 502 JSON.
        using (var image = await GetAsync(client, "/api/admin/image?path=uploads/a.jpg", session))
        {
            Assert.Equal(HttpStatusCode.BadGateway, image.StatusCode);
            Assert.Equal("application/json", image.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"message\":\"图片服务暂不可用\"}", await image.Content.ReadAsStringAsync());
        }

        // Health endpoints keep the library JSON.
        using (var live = await client.GetAsync("/health/live"))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("{\"status\":\"live\"}", await live.Content.ReadAsStringAsync());
        }

        // The SPA keeps serving HTML anonymously (explicit wwwroot content root: the
        // project directory itself has no built SPA to serve).
        var workingDirectory = CreateTempContentRoot(withWwwrootStub: true);
        try
        {
            using var spaFactory = CreateFactory(contentRoot: workingDirectory);
            using var spaClient = spaFactory.CreateClient();
            using var spa = await spaClient.GetAsync("/students");
            Assert.Equal(HttpStatusCode.OK, spa.StatusCode);
            Assert.Contains("stub-index-marker", await spa.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    
    // ===== helpers =====

    private WebApplicationFactory<Program> ProbeFactory(OidcTestAuthority authority, SessionBusinessProbe probe,
        Action<IServiceCollection>? extraServices = null) => CreateFactory(configureTestServices: services =>
    {
        services.AddControllers().AddApplicationPart(typeof(StartedResponseProbeController).Assembly);
        services.Configure<OpenIdConnectOptions>(AdminOidcSettings.OidcScheme, options =>
            options.Backchannel = new HttpClient(authority, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(2) });
        services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => probe.Identity);
        services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(probe.Admins));
        services.Replace(ServiceDescriptor.Singleton(probe.Oss.Object));
        services.Replace(ServiceDescriptor.Singleton(probe.References.Object));
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
        extraServices?.Invoke(services);
    }, settings: new Dictionary<string, string?>
    {
        ["AdminOidc:Enabled"] = "true",
        ["AdminOidc:RedirectUri"] = OidcTestAuthority.RedirectUri,
        ["AdminOidc:UseSessionForAdminApi"] = "true",
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

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? cookie = null,
        CancellationToken cancellation = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request, cancellation);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string method,
        string? cookie = null, string? csrf = null, HttpContent? body = null)
    {
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
        return callback.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")).Split(';')[0];
    }
}

// Test-only controller mapped through AddApplicationPart in ProbeFactory: its action starts
// the response, flushes, and then throws, so the Problem Details "already started" boundary
// is exercised on a real routed MVC endpoint (the metadata the branch predicate reads).
[ApiController, Route("api/test-started-response"), AllowAnonymous]
public sealed class StartedResponseProbeController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Started()
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json";
        await Response.Body.WriteAsync("partial-body"u8.ToArray(), HttpContext.RequestAborted);
        await Response.Body.FlushAsync(HttpContext.RequestAborted);
        throw new InvalidOperationException("secret-canary-started-response");
    }
}
