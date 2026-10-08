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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SignaCore.Client.AspNetCore;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed partial class AdminOidcTests(PostgreSqlFixture database) : ServiceMantleIntegrationTestBase(database)
{
    private WebApplicationFactory<Program> OidcFactory(OidcTestAuthority authority, ManualOidcTime? time = null,
        Action<IServiceCollection>? configure = null, string? root = null, IDataProtectionProvider? protection = null, string? lokiUri = null,
        bool sessionApi = true, bool sessionLogout = false, bool portalProxies = false, bool identityProxy = false)
        => CreateFactory(root, services =>
        {
            // The package's single backchannel client carries Discovery, JWKS, the token
            // endpoint, and the logout preparation; the in-process authority serves them all.
            services.AddHttpClient(SignaCoreHostedLoginDefaults.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => authority);
            // All proxy surfaces are active. Use actual fake HTTP transports for protocol tests
            // that formerly stopped at a disabled gate; specialized captures override these.
            services.AddHttpClient("IdentityService").ConfigurePrimaryHttpMessageHandler(() => new SessionProxyCapture());
            services.AddHttpClient("TeacherPortal").ConfigurePrimaryHttpMessageHandler(() => new PortalSessionCapture());
            services.AddHttpClient("AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => new PortalSessionCapture());
            if (time is not null) services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
            if (protection is not null) services.AddSingleton(protection);
            configure?.Invoke(services);
        }, new Dictionary<string, string?>
        {
            ["AdminOidc:Enabled"] = "true", ["AdminOidc:RedirectUri"] = OidcTestAuthority.RedirectUri,
            ["AdminOidc:UseSessionForAdminApi"] = sessionApi ? "true" : null,
            ["AdminOidc:UseSessionForLogout"] = sessionLogout ? "true" : null,
            ["AdminOidc:UseSessionForPortalProxies"] = portalProxies ? "true" : null,
            ["AdminOidc:UseSessionForIdentityProxy"] = identityProxy ? "true" : null,
            ["AdminOidc:PostLogoutRedirectUri"] = "https://admin.example.test" + AdminOidcSettings.LogoutReturnPath,
            ["TeacherPortal:Url"] = "https://teacher.example.test", ["AssistantPortal:Url"] = "https://assistant.example.test", ["AdminPortal:AdminUserIds:0"] = "FAKE-SUBJECT",
            ["IdentityService:Authority"] = OidcTestAuthority.Issuer, ["IdentityService:Issuer"] = OidcTestAuthority.Issuer,
            ["IdentityService:AppId"] = OidcTestAuthority.ClientId, ["IdentityService:AppSecret"] = OidcTestAuthority.Secret,
            ["IdentityService:RequireHttpsMetadata"] = "true", ["Loki:Uri"] = lokiUri ?? ""
        }.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value));

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
        // The pending sign-in itself lives in the server-side store: the only cookie the start
        // response sets is the package's per-state browser-binding cookie (0.1.14+), which the
        // callback must present together with the state before anything else is trusted.
        var cookies = string.Join("; ", response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';')[0]) : Array.Empty<string>());
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
        // The pending sign-in is server-side: the handshake may legitimately carry no cookie.
        var cookie = cookies ?? handshake.Cookies;
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request, cancellation);
    }
    private static void Failed(HttpResponseMessage response, string reason = "sign_in_failed")
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?authError=" + reason, response.Headers.Location!.OriginalString);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")));
    }
    private static string SessionCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")).Split(';')[0];
    private static async Task<string?> Status(HttpClient client, string? cookie = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (bearer is not null) request.Headers.Add("Authorization", "Bearer " + bearer);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return await response.Content.ReadAsStringAsync();
    }
    // The in-memory ticket store exposes no count; the one-shot sweep returns how many entries
    // it reclaimed, so an empty sweep is exactly "no live session exists".
    private static async Task<int> SweptTickets(WebApplicationFactory<Program> factory)
        => factory.Services.GetRequiredService<ITicketStore>().RemoveExpired(CancellationToken.None);

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
        // Since the 0.1.14 browser binding, the start response sets exactly one cookie: the
        // per-state login-binding cookie, scoped to the callback path and named after the
        // session cookie plus its suffix and the state.
        var binding = Assert.Single(handshake.Cookies.Split("; "));
        Assert.StartsWith(AdminOidcSettings.SessionCookie + "-login-binding." + handshake.Query["state"] + "=", binding);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", binding[(binding.IndexOf('=') + 1)..]);
        var code = authority.Code(handshake.Query, defect);
        using var response = await Callback(client, handshake, code);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/students", response.Headers.Location!.OriginalString);
        // client_secret_basic: the code redemption carries the client credentials only in the
        // Authorization header, never as form fields.
        var form = Assert.Single(authority.TokenForms);
        Assert.Equal(new[] { "code", "code_verifier", "grant_type", "redirect_uri" }, form.Keys.Order().ToArray());
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(OidcTestAuthority.RedirectUri, form["redirect_uri"]);
        var basic = Assert.Single(authority.AuthorizationHeaders);
        Assert.StartsWith("Basic ", basic);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(basic!["Basic ".Length..]));
        Assert.Equal(OidcTestAuthority.ClientId + ":" + OidcTestAuthority.Secret, decoded);
        // The cookie value is the opaque store key; the ticket keeps both tokens server-side.
        var cookieValue = SessionCookie(response);
        var stored = (await factory.Services.GetRequiredService<ITicketStore>()
            .RetrieveAsync(cookieValue[(cookieValue.IndexOf('=') + 1)..], CancellationToken.None))!;
        Assert.NotNull(stored);
        Assert.Equal(authority.LastAccessToken, stored.AccessToken);
        Assert.Equal(authority.LastIdToken, stored.IdToken);
        Assert.True(stored.ExpiresUtc > stored.IssuedUtc);
        Assert.Equal("fake-subject", stored.Principal.FindFirst("sub")!.Value);
        Assert.Equal(OidcTestAuthority.Issuer, stored.Principal.FindFirst("iss")!.Value);
        var status = await Status(client, cookieValue);
        Assert.Contains("\"authenticated\":true", status);
        Assert.Contains(display is null ? "\"displayName\":null" : "\"displayName\":\"fake-display\"", status);
        await Rejected(await Api(client, "/api/auth/session", authorization: ["Bearer " + authority.LastAccessToken]), 401, "unauthorized");
        Assert.Equal(HttpStatusCode.OK, (await Api(client, ProtectedApiRoute, cookieValue)).StatusCode);
        foreach (var path in new[] { "/api/teacher-portal/admin/users", "/api/assistant-portal/admin/users" })
            Assert.Equal(HttpStatusCode.OK, (await Api(client, path, cookieValue)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api(client, "/api/identity/admin/users", cookieValue)).StatusCode);
        var surfaces = cookieValue + status + response.Headers.Location + string.Join('\n', logs.Messages) + string.Join('\n', traces.Messages);
        foreach (var canary in new[] { OidcTestAuthority.Secret, authority.LastAccessToken!, authority.LastVerifier!, authority.LastIdToken!, code })
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

            var firstVerifier = authority.LastVerifier!;
            var firstIdToken = authority.LastIdToken!;
            using var replay = await Callback(client, handshake, code);
            Failed(replay);
            Assert.Equal(1, authority.Redeems);
            Assert.Equal(0, await SweptTickets(factory));
            var invalidHandshake = await Start(client);
            var invalidCode = authority.Code(invalidHandshake.Query, "signature");
            using var invalid = await Callback(client, invalidHandshake, invalidCode);
            Failed(invalid);
            Assert.Equal(2, authority.Redeems);
            Assert.Equal(0, await SweptTickets(factory));
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
                + await invalid.Content.ReadAsStringAsync();
            foreach (var canary in new[] { OidcTestAuthority.Secret, authority.LastAccessToken!,
                authority.LastVerifier!, authority.LastIdToken!, firstVerifier, firstIdToken, code, invalidCode })
                Assert.DoesNotContain(canary, surfaces);
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    // Not listed (recorded accepted differences of the package's validator): an unknown `kid`
    // still validates against the authority's published key set, `iat` is not separately
    // validated, exp/iat boundaries are judged with the package's fixed 30-second skew, and
    // an audience array containing the client id is accepted. None of these shapes is
    // producible by the SignaCore server, and issuer/audience/signature/nonce/lifetime stay
    // enforced.
    [InlineData("unsigned")][InlineData("signature")][InlineData("typ")][InlineData("alg")]
    [InlineData("issuer")][InlineData("aud")]
    [InlineData("sub-missing")][InlineData("sub-empty")][InlineData("sub-array")][InlineData("sub-duplicate")]
    [InlineData("exp-expired")][InlineData("nonce")][InlineData("nonce-missing")]
    public async Task InvalidIdToken_IsRejectedWithoutPublishingTicket(string defect)
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        Failed(await Callback(client, handshake, authority.Code(handshake.Query, defect)));
        Assert.Equal(0, await SweptTickets(factory));
        Assert.Contains("\"authenticated\":false", await Status(client));
    }

    [Theory]
    [InlineData("access-alg")][InlineData("access-typ")][InlineData("access-kid")]
    [InlineData("access-aud")][InlineData("access-subject")][InlineData("access-expired")]
    [InlineData("access-signature")][InlineData("access-opaque")]
    public async Task GatedCallback_InvalidOrUncorrelatedAccessTokenNeverSignsIn(string defect)
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        Failed(await Callback(client, handshake, authority.Code(handshake.Query, accessDefect: defect)));
        Assert.Equal(0, await SweptTickets(factory));
        Assert.Equal(1, authority.Redeems);
    }

    [Theory]
    [InlineData("state-missing", "sign_in_failed")][InlineData("state-duplicate", "sign_in_failed")]
    [InlineData("state-wrong", "sign_in_failed")]
    [InlineData("iss-missing", "sign_in_failed")][InlineData("iss-duplicate", "sign_in_failed")]
    [InlineData("iss-wrong", "sign_in_failed")]
    [InlineData("code-missing", "sign_in_failed")][InlineData("code-duplicate", "sign_in_failed")]
    [InlineData("code-error", "cancelled")]
    public async Task InvalidCallback_CannotBypassStateOrIssuerBindings(string defect, string reason)
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        var parameters = new Dictionary<string, string?> { ["state"] = handshake.Query["state"], ["iss"] = OidcTestAuthority.Issuer, ["code"] = code };
        if (defect.EndsWith("-missing")) parameters.Remove(defect.Split('-')[0]);
        if (defect == "state-wrong") parameters["state"] = new string('x', 43);
        if (defect == "iss-wrong") parameters["iss"] = "https://wrong.example.test";
        if (defect == "code-error") parameters["error"] = "access_denied";
        var query = QueryHelpers.AddQueryString(AdminOidcSettings.CallbackPath, parameters);
        if (defect.EndsWith("-duplicate")) { var key = defect.Split('-')[0]; query += "&" + key + "=" + Uri.EscapeDataString(parameters[key]!); }
        Failed(await Callback(client, handshake, code, query), reason);
        // No defect shape ever reaches the code redemption: the binding checks run first.
        Assert.Equal(0, authority.Redeems);
        Assert.Equal(0, await SweptTickets(factory));
    }

    [Theory]
    [InlineData("non-admin", "cancelled")]
    [InlineData("decision-exception", "cancelled")]
    [InlineData("decision-timeout", "cancelled")]
    [InlineData("removed-then-restored", "cancelled")]
    public async Task PreSignInWhitelistGate_NonAdminNeverGetsTicketOrCookie(string variant, string reason)
    {
        using var authority = new OidcTestAuthority();
        var admins = new MutableAdmins();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
            if (variant is "decision-exception" or "decision-timeout")
                services.AddOptions<SignaCoreHostedLoginOptions>().PostConfigure(options =>
                {
                    options.PreSignInAuthorizationTimeout = TimeSpan.FromMilliseconds(200);
                    options.PreSignInAuthorizationDecision = variant == "decision-exception"
                        ? new ThrowingPreSignInDecision()
                        : new HangingPreSignInDecision();
                });
        });
        using var client = Browser(factory);
        var handshake = await Start(client);
        if (variant == "removed-then-restored") admins.CurrentValue.AdminUserIds.Clear();
        using var response = await Callback(client, handshake, authority.Code(handshake.Query,
            subject: variant == "non-admin" ? "non-admin-subject" : "fake-subject"));
        Failed(response, reason);
        Assert.Equal(0, await SweptTickets(factory));
        Assert.Equal(1, authority.Redeems);
        if (variant == "removed-then-restored")
        {
            admins.CurrentValue.AdminUserIds.Add("fake-subject");
            var retry = await Start(client);
            using var signedIn = await Callback(client, retry, authority.Code(retry.Query));
            Assert.Equal("/students", signedIn.Headers.Location!.OriginalString);
        }
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
        Failed(await Callback(client, handshake, null, query), "cancelled");
        Assert.Equal(0, authority.Redeems);
        Assert.Equal(0, await SweptTickets(factory));
    }

    [Theory]
    [InlineData("500")][InlineData("json")][InlineData("issuer")][InlineData("jwks")]
    public async Task BadDiscovery_NoRedirectToAuthorityNoPending(string defect)
    {
        using var authority = new OidcTestAuthority { DiscoveryDefect = defect };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        Failed(await client.GetAsync("/api/auth/oidc/start"), "identity_unavailable");
        Assert.Equal(0, authority.Redeems);
    }

    [Theory]
    [InlineData("500", "sign_in_failed")][InlineData("json", "sign_in_failed")]
    public async Task BadTokenResponse_NoTicketOrCodeRetry(string failure, string reason)
    {
        using var authority = new OidcTestAuthority { TokenFailure = failure };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        Failed(await Callback(client, handshake, code), reason);
        Failed(await Callback(client, handshake, code), reason);
        Assert.Equal(1, authority.Redeems);
        Assert.Equal(0, await SweptTickets(factory));
    }

    [Fact]
    public async Task BadTokenResponse_TransportTimeoutStaysBoundedWithoutTicketOrRetry()
    {
        // A handler-thrown cancellation is classified by the HTTP stack into either closed
        // failure bucket depending on the wrapping layer, so this test pins the invariants
        // (bounded login redirect, no ticket, exactly one redemption attempt) instead of the
        // reason string.
        using var authority = new OidcTestAuthority { TokenFailure = "timeout" };
        using var factory = OidcFactory(authority);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await Callback(client, handshake, code);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/login?authError=", response.Headers.Location!.OriginalString);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out var values)
                && values.Any(value => value.StartsWith(AdminOidcSettings.SessionCookie + "=")));
        }
        Assert.Equal(1, authority.Redeems);
        Assert.Equal(0, await SweptTickets(factory));
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
        responses = await Task.WhenAll(starts.Select(start => Callback(client, start, authority.Code(start.Query))));
        Assert.Equal(new[] { "/one", "/two" }, responses.Select(response => response.Headers.Location!.OriginalString).Order().ToArray());
        Assert.Equal(3, authority.Redeems);
    }

    [Fact]
    public async Task PendingAndTicketExpire_AndRestartCannotReviveOpaqueCookie()
    {
        using var authority = new OidcTestAuthority();
        var time = new ManualOidcTime();
        using var factory = OidcFactory(authority, time);
        using var client = Browser(factory);
        var handshake = await Start(client);
        var code = authority.Code(handshake.Query);
        time.Advance(TimeSpan.FromMinutes(5));
        Failed(await Callback(client, handshake, code));
        Assert.Equal(0, authority.Redeems);
        time = new ManualOidcTime();
        using var live = OidcFactory(authority, time);
        using var browser = Browser(live);
        handshake = await Start(browser);
        var cookie = SessionCookie(await Callback(browser, handshake, authority.Code(handshake.Query)));
        Assert.Contains("\"authenticated\":true", await Status(browser, cookie));
        // A restart empties the in-process store; presenting the same opaque key to a fresh
        // process answers the fixed anonymous status, whatever keys protect anything else.
        using var restarted = OidcFactory(authority);
        using var restartedClient = Browser(restarted);
        Assert.Contains("\"authenticated\":false", await Status(restartedClient, cookie));
        // The session dies with the access token: the ticket expires exactly at its deadline.
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.Contains("\"authenticated\":false", await Status(browser, cookie));
        Assert.Equal(0, await SweptTickets(live));
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
        Assert.Equal(0, await SweptTickets(factory));
        Failed(await Callback(client, handshake, code));
        Assert.Equal(1, authority.Redeems);
    }

    [Fact]
    public async Task DisabledAndAnonymousSpaHealthContractsRemain()
    {
        var root = CreateTempContentRoot(true);
        try
        {
            using var authority = new OidcTestAuthority();
            using var factory = OidcFactory(authority, root: root);
            using var client = Browser(factory);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/api/auth/oidc/start")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(AdminOidcSettings.CallbackPath)).StatusCode);
            Assert.Contains("\"authenticated\":false", await Status(client));
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
        Assert.Equal(0, await SweptTickets(factory));
    }

    [Fact]
    public async Task SessionOnlyApiAndLogoutRejectLegacyJwtButClaimsCallbackStillWorks()
    {
        using var authority = new OidcTestAuthority();
        using var factory = OidcFactory(authority, sessionApi: false, configure: services => services.Configure<JwtBearerOptions>("Bearer", options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = OidcTestAuthority.Issuer };
            metadata.SigningKeys.Add(authority.SigningKey);
            options.Configuration = metadata;
        }));
        using var client = Browser(factory);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authority.LegacyBearer());
        await Rejected(await client.GetAsync(ProtectedApiRoute), 401, "unauthorized");
        // The package's logout endpoint owns its own gate: a browser credential never becomes
        // an authorization there, and the antiforgery requirement rejects the bare request.
        using var logout = await client.PostAsync(AdminOidcSettings.LogoutPath, null);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        await Rejected(await client.GetAsync("/api/auth/session"), 401, "unauthorized");
        var authentication = factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        // No default authenticate scheme exists: the session boundary resolves the ticket
        // directly, and challenges fall back to the package's forwarding scheme.
        Assert.Null(authentication.DefaultScheme);
        Assert.Equal(SignaCoreHostedLoginDefaults.AuthenticationScheme, authentication.DefaultChallengeScheme);
        var callback = await client.PostAsJsonAsync("/api/auth/callback", new { userId = "fake-user" });
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
    }

    [Fact]
    public async Task AbsentLegacyKeysRegisterOnlyPackageSchemes()
    {
        using var factory = CreateFactory();
        var schemes = (await factory.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync()).Select(s => s.Name).Order().ToArray();
        Assert.Equal(new[] { SignaCoreHostedLoginDefaults.AuthenticationScheme, SignaCoreHostedLoginDefaults.SessionAuthenticationScheme }.Order(), schemes);
    }

    [Theory]
    [InlineData("")][InlineData("http://localhost:5020/api/auth/oidc/callback")]
    [InlineData("https://user:secret@admin.example.test/api/auth/oidc/callback")]
    [InlineData("https://admin.example.test/wrong")][InlineData("https://admin.example.test/api/auth/oidc/callback?query=secret")]
    [InlineData("https://admin.example.test/api/auth/oidc/logout-callback")]
    public void EnabledInvalidRedirectConfigurationFailsWithoutEcho(string redirect)
    {
        using var factory = CreateFactory(settings: new Dictionary<string, string?>
        {
            ["AdminOidc:RedirectUri"] = redirect
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

internal sealed class ThrowingPreSignInDecision : ISignaCorePreSignInAuthorizationDecision
{
    public ValueTask<SignaCoreAuthorizationDecisionResult> DecideAsync(
        SignaCorePreSignInAuthorizationContext context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("fake.decision_failure");
}

internal sealed class HangingPreSignInDecision : ISignaCorePreSignInAuthorizationDecision
{
    public async ValueTask<SignaCoreAuthorizationDecisionResult> DecideAsync(
        SignaCorePreSignInAuthorizationContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return SignaCoreAuthorizationDecisionResult.Allowed;
    }
}
