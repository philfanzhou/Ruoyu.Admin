using System.Net.Http.Json;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Admin.WebApi.Authentication;
using Admin.WebApi.Tests.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed partial class AdminOidcTests(PostgreSqlFixture database) : ServiceMantleIntegrationTestBase(database)
{
    private WebApplicationFactory<Program> OidcFactory(OidcTestAuthority authority, ManualOidcTime? time = null,
        Action<IServiceCollection>? configure = null, string? root = null, IDataProtectionProvider? protection = null, string? lokiUri = null,
        bool sessionApi = false, bool sessionLogout = false, bool portalProxies = false, bool identityProxy = false)
        => CreateFactory(root, services =>
        {
            services.Configure<OpenIdConnectOptions>(AdminOidcSettings.OidcScheme, options =>
                options.Backchannel = new HttpClient(authority, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(2) });
            if (time is not null) services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
            if (protection is not null) services.AddSingleton(protection);
            configure?.Invoke(services);
        }, new Dictionary<string, string?>
        {
            ["AdminOidc:Enabled"] = "true", ["AdminOidc:RedirectUri"] = OidcTestAuthority.RedirectUri,
            ["AdminOidc:UseSessionForAdminApi"] = sessionApi.ToString(),
            ["AdminOidc:UseSessionForLogout"] = sessionLogout.ToString(),
            ["AdminOidc:PostLogoutRedirectUri"] = "https://admin.example.test/api/auth/oidc/logout-callback",
            ["AdminOidc:UseSessionForPortalProxies"] = portalProxies.ToString(),
            ["TeacherPortal:Url"] = "https://teacher.example.test", ["AssistantPortal:Url"] = "https://assistant.example.test", ["AdminPortal:AdminUserIds:0"] = "FAKE-SUBJECT",
            ["AdminOidc:UseSessionForIdentityProxy"] = identityProxy.ToString(),
            ["IdentityService:Authority"] = OidcTestAuthority.Issuer, ["IdentityService:Issuer"] = OidcTestAuthority.Issuer,
            ["IdentityService:AppId"] = OidcTestAuthority.ClientId, ["IdentityService:AppSecret"] = OidcTestAuthority.Secret,
            ["IdentityService:RequireHttpsMetadata"] = "true", ["Loki:Uri"] = lokiUri ?? ""
        });

    private static HttpClient Browser(WebApplicationFactory<Program> factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://admin.example.test"), AllowAutoRedirect = false, HandleCookies = false
    });
    private sealed record Handshake(Dictionary<string, string> Query, string Cookies, HttpResponseMessage Response);
    private static async Task<Handshake> Start(HttpClient client, string target = "/students")
    {
        var response = await client.GetAsync("/api/auth/oidc/start?returnUrl=" + Uri.EscapeDataString(target));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(OidcTestAuthority.Issuer + "/authorize", response.Headers.Location!.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
        var cookies = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        return new(query, cookies, response);
    }
    private static Task<HttpResponseMessage> Callback(HttpClient client, Handshake handshake, string? code, string? query = null,
        string? cookies = null, CancellationToken cancellation = default)
    {
        query ??= QueryHelpers.AddQueryString(AdminOidcSettings.CallbackPath, new Dictionary<string, string?>
        {
            ["code"] = code, ["state"] = handshake.Query["state"], ["iss"] = OidcTestAuthority.Issuer
        });
        var request = new HttpRequestMessage(HttpMethod.Get, query);
        request.Headers.Add("Cookie", cookies ?? handshake.Cookies);
        return client.SendAsync(request, cancellation);
    }
    private static void Failed(HttpResponseMessage response, string reason = "sign_in_failed")
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?authError=" + reason, response.Headers.Location!.OriginalString);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }
    private static string SessionCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")).Split(';')[0];
    private static async Task<string> Status(HttpClient client, string? cookie = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (bearer is not null) request.Headers.Add("Authorization", "Bearer " + bearer);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return await response.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData("valid", "fake-display")]
    [InlineData("display-missing", null)]
    public async Task FullCodeFlow_UsesExactProtocolAndServerTicketOnly(string defect, string? display)
    {
        using var authority = new OidcTestAuthority();
        var logs = new OidcLogCapture();
        var traces = new OidcTraceCapture();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.RemoveAll<ILoggerFactory>();
            services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace);
            services.AddSingleton<ILoggerFactory>(provider => new LoggerFactory([logs], provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>()));
            services.AddOpenTelemetry().WithTracing(builder => builder.AddProcessor(traces));
        });
        using var client = Browser(factory);
        var handshake = await Start(client);
        Assert.Equal(new[] { "client_id", "code_challenge", "code_challenge_method", "nonce", "redirect_uri", "response_type", "scope", "state" }, handshake.Query.Keys.Order().ToArray());
        Assert.Equal("code", handshake.Query["response_type"]);
        Assert.Equal("openid profile", handshake.Query["scope"]);
        Assert.Equal("S256", handshake.Query["code_challenge_method"]);
        Assert.Equal(OidcTestAuthority.RedirectUri, handshake.Query["redirect_uri"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", handshake.Query["state"]);
        Assert.Matches("^[A-Za-z0-9._~-]{22,128}$", handshake.Query["nonce"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", handshake.Query["code_challenge"]);
        foreach (var cookie in handshake.Response.Headers.GetValues("Set-Cookie"))
        {
            Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("samesite=lax", cookie.ToLowerInvariant()); Assert.Contains("secure", cookie.ToLowerInvariant());
        }
        var code = authority.Code(handshake.Query, defect);
        using var response = await Callback(client, handshake, code);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/students", response.Headers.Location!.OriginalString);
        var form = Assert.Single(authority.TokenForms);
        Assert.Equal(new[] { "client_id", "client_secret", "code", "code_verifier", "grant_type", "redirect_uri" }, form.Keys.Order().ToArray());
        Assert.Equal(OidcTestAuthority.Secret, form["client_secret"]);
        Assert.Equal(OidcTestAuthority.ClientId, form["client_id"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(OidcTestAuthority.RedirectUri, form["redirect_uri"]);
        Assert.Null(Assert.Single(authority.AuthorizationHeaders));
        var cookieValue = SessionCookie(response);
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AdminOidcSettings.SessionScheme);
        Assert.False(cookieOptions.SlidingExpiration);
        Assert.IsType<MemoryTicketStore>(cookieOptions.SessionStore);
        var referenceTicket = cookieOptions.TicketDataFormat.Unprotect(cookieValue[(cookieValue.IndexOf('=') + 1)..])!;
        Assert.NotNull(referenceTicket);
        Assert.Empty(referenceTicket.Properties.GetTokens());
        var reference = Assert.Single(referenceTicket.Principal.Claims).Value;
        var stored = await factory.Services.GetRequiredService<MemoryTicketStore>().RetrieveAsync(reference);
        Assert.NotNull(stored);
        Assert.Equal(OidcTestAuthority.AccessToken, stored.Properties.GetTokenValue("access_token"));
        Assert.Equal(authority.LastIdToken, stored.Properties.GetTokenValue("id_token"));
        Assert.NotNull(stored.Properties.GetTokenValue("expires_at"));
        Assert.Equal("fake-subject", stored.Principal.FindFirst("sub")!.Value);
        Assert.Equal(OidcTestAuthority.Issuer, stored.Principal.FindFirst("iss")!.Value);
        Assert.False(stored.Principal.IsInRole("admin"));
        var status = await Status(client, cookieValue);
        Assert.Contains("\"authenticated\":true", status);
        Assert.Contains(display is null ? "\"displayName\":null" : "\"displayName\":\"fake-display\"", status);
        Assert.Contains("\"authenticated\":false", await Status(client, bearer: OidcTestAuthority.AccessToken));
        foreach (var path in new[] { ProtectedApiRoute, "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
        {
            using var apiRequest = new HttpRequestMessage(HttpMethod.Get, path);
            apiRequest.Headers.Add("Cookie", cookieValue);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(apiRequest)).StatusCode);
        }
        await Rejected(await Api(client, "/api/identity/admin/users", cookieValue), 503, "session_identity_proxy_disabled");
        var surfaces = cookieValue + status + response.Headers.Location + string.Join('\n', logs.Messages) + string.Join('\n', traces.Messages);
        foreach (var canary in new[] { OidcTestAuthority.Secret, OidcTestAuthority.AccessToken, authority.LastVerifier!, authority.LastIdToken!, code })
            Assert.DoesNotContain(canary, surfaces);
        Failed(await Callback(client, handshake, code));
        Assert.Equal(1, authority.Redeems);
    }

    [Fact]
    public async Task RealLoggingPipeline_OidcSecretsNeverReachConsoleLokiOrTraces()
    {
        var batches = new ConcurrentQueue<string>();
        var lokiBuilder = WebApplication.CreateBuilder();
        lokiBuilder.Logging.ClearProviders();
        lokiBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var loki = lokiBuilder.Build();
        loki.MapPost("/loki/api/v1/push", async context =>
        {
            batches.Enqueue(await new StreamReader(context.Request.Body).ReadToEndAsync());
            context.Response.StatusCode = 204;
        });
        await loki.StartAsync();
        var address = loki.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            using var authority = new OidcTestAuthority();
            var traces = new OidcTraceCapture();
            using var factory = OidcFactory(authority, lokiUri: address, configure: services =>
            {
                // Keep the real ServiceMantle ILoggerFactory and both consumers.
                services.AddControllers().AddApplicationPart(typeof(LoggingProbeController).Assembly);
                services.AddOpenTelemetry().WithTracing(builder => builder.AddProcessor(traces));
            });
            using var client = Browser(factory);
            var handshake = await Start(client);
            var code = authority.Code(handshake.Query);
            using var response = await Callback(client, handshake, code);
            Assert.Equal("/students", response.Headers.Location!.OriginalString);
            var cookie = SessionCookie(response);
            var status = await Status(client, cookie);
            Assert.Contains("\"authenticated\":true", status);
            var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(AdminOidcSettings.SessionScheme);
            var reference = options.TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!;
            Assert.Empty(reference.Properties.GetTokens());
            Assert.Single(reference.Principal.Claims);
            Assert.Equal(1, factory.Services.GetRequiredService<MemoryTicketStore>().Count);

            var firstVerifier = authority.LastVerifier!;
            var firstIdToken = authority.LastIdToken!;
            using var replay = await Callback(client, handshake, code);
            Failed(replay);
            Assert.Equal(1, authority.Redeems);
            Assert.Equal(1, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
            var invalidHandshake = await Start(client);
            var invalidCode = authority.Code(invalidHandshake.Query, "signature");
            using var invalid = await Callback(client, invalidHandshake, invalidCode);
            Failed(invalid);
            Assert.Equal(2, authority.Redeems);
            Assert.Equal(1, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
            // Exercise the category at Warning too, so Serilog's normal ASP.NET override
            // cannot accidentally stand in for the OIDC-specific MEL suppression.
            factory.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics")
                .LogWarning("request.query.canary {Query}", "?code=" + code);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/logging-probe")).StatusCode);
            factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Admin.OidcProbe")
                .LogInformation("oidc.probe.completed");
            // Wait for the marker after every protocol request to reach the real async sink.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!batches.Any(batch => batch.Contains("oidc.probe.completed")))
                await Task.Delay(20, timeout.Token);
            var remote = string.Join('\n', batches.SelectMany(batch => JsonDocument.Parse(batch).RootElement
                .GetProperty("streams").EnumerateArray().SelectMany(stream => stream.GetProperty("values")
                    .EnumerateArray().Select(value => value[1].GetString()!).ToArray())));
            foreach (var logs in new[] { output.ToString(), remote })
            {
                Assert.Contains("probe.application", logs);
                Assert.Contains("oidc.probe.completed", logs);
                Assert.DoesNotContain("request.query.canary", logs);
                Assert.DoesNotContain(LoggingProbeController.Canary, logs);
            }
            Assert.Contains(traces.Messages, trace => trace.Contains("/api/auth/session"));
            Assert.DoesNotContain(traces.Messages, trace => trace.Contains("/api/auth/oidc"));
            var surfaces = output + remote + string.Join('\n', traces.Messages) + status
                + handshake.Response.Headers + response.Headers + replay.Headers + invalid.Headers
                + await response.Content.ReadAsStringAsync() + await replay.Content.ReadAsStringAsync()
                + await invalid.Content.ReadAsStringAsync()
                + JsonSerializer.Serialize(reference.Properties.Items)
                + string.Join(";", reference.Principal.Claims.Select(claim => claim.Type + "=" + claim.Value));
            foreach (var canary in new[] { OidcTestAuthority.Secret, OidcTestAuthority.AccessToken,
                authority.LastVerifier!, authority.LastIdToken!, firstVerifier, firstIdToken, code, invalidCode })
                Assert.DoesNotContain(canary, surfaces);
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData("unsigned")][InlineData("signature")][InlineData("kid")][InlineData("typ")][InlineData("alg")]
    [InlineData("issuer")][InlineData("aud")][InlineData("aud-array")][InlineData("aud-single-array")]
    [InlineData("sub-missing")][InlineData("sub-empty")][InlineData("sub-array")][InlineData("sub-duplicate")]
    [InlineData("iat-missing")][InlineData("iat-future")][InlineData("iat-string")]
    [InlineData("exp-before-iat")][InlineData("exp-expired")][InlineData("nonce")][InlineData("nonce-missing")]
    public async Task InvalidIdToken_IsRejectedWithoutPublishingTicket(string defect)
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        Failed(await Callback(client, handshake, authority.Code(handshake.Query, defect)));
        Assert.Equal(0, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
        Assert.Contains("\"authenticated\":false", await Status(client));
    }

    [Theory]
    [InlineData("state-missing")][InlineData("state-duplicate")][InlineData("state-wrong")]
    [InlineData("iss-missing")][InlineData("iss-duplicate")][InlineData("iss-wrong")]
    [InlineData("code-missing")][InlineData("code-duplicate")][InlineData("code-error")]
    [InlineData("correlation-missing")][InlineData("nonce-cookie-missing")]
    public async Task InvalidCallback_CannotBypassStateIssuerOrCookieBindings(string defect)
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        var parameters = new Dictionary<string, string?> { ["state"] = handshake.Query["state"], ["iss"] = OidcTestAuthority.Issuer, ["code"] = code };
        if (defect.EndsWith("-missing") && !defect.Contains("cookie") && !defect.Contains("correlation")) parameters.Remove(defect.Split('-')[0]);
        if (defect == "state-wrong") parameters["state"] = new string('x', 43);
        if (defect == "iss-wrong") parameters["iss"] = "https://wrong.example.test";
        if (defect == "code-error") parameters["error"] = "access_denied";
        var query = QueryHelpers.AddQueryString(AdminOidcSettings.CallbackPath, parameters);
        if (defect.EndsWith("-duplicate")) { var key = defect.Split('-')[0]; query += "&" + key + "=" + Uri.EscapeDataString(parameters[key]!); }
        var cookies = handshake.Cookies;
        if (defect == "correlation-missing") cookies = string.Join("; ", cookies.Split("; ").Where(value => !value.Contains("Correlation")));
        if (defect == "nonce-cookie-missing") cookies = string.Join("; ", cookies.Split("; ").Where(value => !value.Contains("Nonce")));
        Failed(await Callback(client, handshake, code, query, cookies));
        Assert.Equal(defect == "nonce-cookie-missing" ? 1 : 0, authority.Redeems);
        Assert.Equal(0, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
    }

    [Fact]
    public async Task Cancelled_RequiresIssuerCorrelationAndConsumesPendingOnce()
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var query = QueryHelpers.AddQueryString(AdminOidcSettings.CallbackPath, new Dictionary<string, string?>
        {
            ["error"] = "access_denied", ["error_description"] = "fictitious-error-canary", ["state"] = handshake.Query["state"], ["iss"] = OidcTestAuthority.Issuer
        });
        Failed(await Callback(client, handshake, null, query), "cancelled");
        Failed(await Callback(client, handshake, null, query));
        Assert.Equal(0, authority.Redeems);
        Assert.Equal(0, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
    }

    [Theory]
    [InlineData("500")][InlineData("json")][InlineData("issuer")][InlineData("timeout")][InlineData("jwks")]
    public async Task BadDiscovery_NoRedirectToAuthorityNoPending(string defect)
    {
        using var authority = new OidcTestAuthority { DiscoveryDefect = defect };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        Failed(await client.GetAsync("/api/auth/oidc/start"), "identity_unavailable");
        Assert.Equal(0, factory.Services.GetRequiredService<CompactStateDataFormat>().Count);
        Assert.Equal(0, authority.Redeems);
    }

    [Theory]
    [InlineData("500")][InlineData("json")][InlineData("timeout")]
    public async Task BadTokenResponse_NoTicketOrCodeRetry(string failure)
    {
        using var authority = new OidcTestAuthority { TokenFailure = failure };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        Failed(await Callback(client, handshake, code));
        Failed(await Callback(client, handshake, code));
        Assert.Equal(1, authority.Redeems);
        Assert.Equal(0, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
    }

    [Fact]
    public async Task ParallelRepeatRedeemsOnce_IndependentHandshakesBothSucceed()
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        var responses = await Task.WhenAll(Callback(client, handshake, code), Callback(client, handshake, code));
        Assert.Single(responses.Where(response => response.Headers.Location!.OriginalString == "/students"));
        Assert.Equal(1, authority.Redeems);
        var starts = await Task.WhenAll(Start(client, "/one"), Start(client, "/two"));
        Assert.NotEqual(starts[0].Query["state"], starts[1].Query["state"]);
        Assert.NotEqual(starts[0].Cookies, starts[1].Cookies);
        responses = await Task.WhenAll(starts.Select(start => Callback(client, start, authority.Code(start.Query))));
        Assert.Equal(new[] { "/one", "/two" }, responses.Select(response => response.Headers.Location!.OriginalString).Order().ToArray());
        Assert.Equal(3, authority.Redeems);
    }

    [Fact]
    public async Task PendingAndTicketExpire_AndRestartCannotReviveOpaqueCookie()
    {
        using var authority = new OidcTestAuthority();
        var time = new ManualOidcTime();
        var protection = new EphemeralDataProtectionProvider();
        using var factory = OidcFactory(authority, time, protection: protection);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        time.Advance(TimeSpan.FromMinutes(5));
        Failed(await Callback(client, handshake, code));
        Assert.Equal(0, authority.Redeems);
        time = new ManualOidcTime();
        using var live = OidcFactory(authority, time, protection: protection);
        using var browser = Browser(live);
        handshake = await Start(browser);
        var cookie = SessionCookie(await Callback(browser, handshake, authority.Code(handshake.Query)));
        Assert.Contains("\"authenticated\":true", await Status(browser, cookie));
        using var restarted = OidcFactory(authority, protection: protection);
        using var restartedClient = Browser(restarted);
        var reference = restarted.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AdminOidcSettings.SessionScheme)
            .TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..]);
        Assert.NotNull(reference); // Same DP keys: the absent server ticket, not a key rotation, invalidates it.
        Assert.Contains("\"authenticated\":false", await Status(restartedClient, cookie));
        time.Advance(TimeSpan.FromHours(8));
        Assert.Contains("\"authenticated\":false", await Status(browser, cookie));
        Assert.Equal(0, live.Services.GetRequiredService<MemoryTicketStore>().Count);
    }

    [Fact]
    public async Task CancellationDuringRedeem_DoesNotPublishTicketOrReplayCode()
    {
        using var authority = new OidcTestAuthority { HoldToken = true };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        using var cancellation = new CancellationTokenSource();
        var callback = Callback(client, handshake, code, cancellation: cancellation.Token);
        await authority.TokenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callback);
        Assert.Equal(0, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
        Failed(await Callback(client, handshake, code));
        Assert.Equal(1, authority.Redeems);
    }

    [Fact]
    public async Task DisabledAndAnonymousSpaHealthContractsRemain()
    {
        var root = CreateTempContentRoot(true);
        try
        {
            using var factory = CreateFactory(root);
            using var client = Browser(factory);
            foreach (var path in new[] { "/api/auth/oidc/start", AdminOidcSettings.CallbackPath, "/api/auth/session" })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("{\"error\":\"oidc_disabled\"}", await response.Content.ReadAsStringAsync());
            }
            foreach (var path in new[] { "/", "/students", "/health/live" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
            // Readiness now reports the real evidence source (#54): the fixture database is
            // reachable and this process completed its initialization, so ready answers 200.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ProtectedApiRoute)).StatusCode);
            var responseCallback = await client.PostAsJsonAsync("/api/auth/callback", new { userId = "fake-user" });
            Assert.Equal(HttpStatusCode.OK, responseCallback.StatusCode);
            Assert.Equal("{\"roles\":[]}", await responseCallback.Content.ReadAsStringAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FixedExternalRedirectAndSecureCookiesIgnoreHostAndForwardedHeaders()
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://forged.example.test"), AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "another-forged.example.test");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        var handshake = await Start(client);
        Assert.Equal(OidcTestAuthority.RedirectUri, handshake.Query["redirect_uri"]);
        foreach (var cookie in handshake.Response.Headers.GetValues("Set-Cookie")) Assert.Contains("secure", cookie.ToLowerInvariant());
        var response = await Callback(client, handshake, authority.Code(handshake.Query));
        Assert.Equal(OidcTestAuthority.RedirectUri, Assert.Single(authority.TokenForms)["redirect_uri"]);
        Assert.Contains("secure", response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("adminSession=")).ToLowerInvariant());
    }

    [Fact]
    public async Task StartCancellationCreatesNoPendingOrTicket()
    {
        using var authority = new OidcTestAuthority { HoldDiscovery = true };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetAsync("/api/auth/oidc/start", cancellation.Token);
        await authority.DiscoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, factory.Services.GetRequiredService<CompactStateDataFormat>().Count);
        Assert.Equal(0, factory.Services.GetRequiredService<MemoryTicketStore>().Count);
    }

    [Fact]
    public async Task ExistingJwtAndLegacyClaimsCallbackStillWork()
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority, configure: services => services.Configure<JwtBearerOptions>("Bearer", options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = OidcTestAuthority.Issuer };
            metadata.SigningKeys.Add(authority.SigningKey);
            options.Configuration = metadata;
        }));
        using var client = Browser(factory);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authority.LegacyBearer());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ProtectedApiRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Contains("\"authenticated\":false", await Status(client));
        var authentication = factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal("Bearer", authentication.DefaultScheme);
        var callback = await client.PostAsJsonAsync("/api/auth/callback", new { userId = "fake-user" });
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
    }

    [Theory]
    [InlineData("")][InlineData("http://localhost:5020/api/auth/oidc/callback")]
    [InlineData("https://user:secret@admin.example.test/api/auth/oidc/callback")]
    [InlineData("https://admin.example.test/wrong")][InlineData("https://admin.example.test/api/auth/oidc/callback?query=secret")]
    public void EnabledInvalidRedirectConfigurationFailsWithoutEcho(string redirect)
    {
        using var factory = CreateFactory(settings: new Dictionary<string, string?>
        {
            ["AdminOidc:Enabled"] = "true", ["AdminOidc:RedirectUri"] = redirect
        });
        var failure = Assert.Throws<InvalidOperationException>(() => factory.Services);
        Assert.Equal("AdminOidc:RedirectUri", failure.Message);
    }
}

internal sealed class OidcLogCapture : ILoggerProvider
{
    internal ConcurrentQueue<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }
    private sealed class Logger(OidcLogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Messages.Enqueue(formatter(state, exception) + exception?.ToString());
    }
}
internal sealed class OidcTraceCapture : BaseProcessor<Activity>
{
    internal ConcurrentQueue<string> Messages { get; } = [];
    public override void OnEnd(Activity activity) => Messages.Enqueue(string.Join(";", activity.TagObjects.Select(p => p.Key + "=" + p.Value))
        + string.Join(";", activity.Events.Select(e => e.Name + string.Join(";", e.Tags.Select(p => p.Key + "=" + p.Value)))));
}
