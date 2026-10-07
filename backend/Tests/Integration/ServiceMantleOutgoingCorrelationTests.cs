using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Admin.WebApi.Authentication;
using Admin.WebApi.Services;
using Admin.WebApi.Tests.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    private static readonly string[] CorrelationClients =
    [nameof(IStudentHttpClient), nameof(IMistakeHttpClient), nameof(HomeworkReferenceClient),
        "IdentityService", "TeacherPortal", "AssistantPortal", StorageReferenceCollector.ClientName, SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName];
    private static readonly HttpRequestOptionsKey<CancellationTokenSource> CancelAtReturn = new("Test.CancelAtReturn");

    private WebApplicationFactory<Program> CorrelationFactory(OidcTestAuthority authority, CorrelationTransport capture, bool probe) =>
        OidcFactory(authority, configure: services => CaptureCorrelationClients(services, authority, capture, probe))
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("MistakeService:UseSessionToken", "true");
                builder.UseSetting("MistakeService:Url", "https://mistake.example.test");
            });

    private static void CaptureCorrelationClients(IServiceCollection services, OidcTestAuthority authority, CorrelationTransport capture, bool probe)
    {
        services.AddHttpClient<IStudentHttpClient, StudentHttpClient>().ConfigurePrimaryHttpMessageHandler(() => new TaggedTransport(nameof(IStudentHttpClient), capture));
        services.AddHttpClient<IMistakeHttpClient, MistakeHttpClient>().ConfigurePrimaryHttpMessageHandler(() => new TaggedTransport(nameof(IMistakeHttpClient), capture));
        services.AddHttpClient<HomeworkReferenceClient>().ConfigurePrimaryHttpMessageHandler(() => new TaggedTransport(nameof(HomeworkReferenceClient), capture));
        // The package's shared backchannel must still reach the authority for Discovery, JWKS,
        // and the token endpoint; every other destination (the logout preparation and the
        // synthetic probes) is captured like the other clients.
        services.AddHttpClient(SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new AuthoritySplitTransport(authority,
                new TaggedTransport(SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName, capture)));
        foreach (var name in CorrelationClients.Skip(3).Where(name => name != SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName))
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new TaggedTransport(name, capture));
        services.AddHttpClient("external").ConfigurePrimaryHttpMessageHandler(() => new TaggedTransport("external", capture));
        services.AddSingleton(capture);
        if (probe) services.AddSingleton<IAuthorizationMiddlewareResultHandler, CorrelationProbe>();
    }

    [Fact]
    public async Task Correlation_ProductionAllEightClients_PooledContextsExplicitHeadersAndBackground()
    {
        using var authority = new OidcTestAuthority(); var capture = new CorrelationTransport();
        using var factory = CorrelationFactory(authority, capture, true);
        using var browser = Browser(factory); var cookie = await LoginSession(browser, authority);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            var probe = "parallel-" + index; var id = "correlation-admin-" + index;
            using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedApiRoute + "?probe=" + probe);
            request.Headers.Add("Cookie", cookie); request.Headers.Add(CorrelationHeaderName, id);
            using var response = await browser.SendAsync(request); Assert.True(response.StatusCode == HttpStatusCode.OK, string.Join("\n", capture.Errors.Select(error => error.ToString())));
            Assert.Equal(id, response.Headers.GetValues(CorrelationHeaderName).Single());
            foreach (var name in CorrelationClients) Assert.Equal(new[] { id }, capture.Sends[name + "|" + probe].Correlation);
            Assert.Empty(capture.Sends["external|" + probe].Correlation);
        }));
        using var explicitRequest = new HttpRequestMessage(HttpMethod.Get, ProtectedApiRoute + "?probe=explicit&explicit=true");
        explicitRequest.Headers.Add("Cookie", cookie); explicitRequest.Headers.Add(CorrelationHeaderName, "incoming-explicit-admin");
        using var explicitResponse = await browser.SendAsync(explicitRequest); Assert.Equal(HttpStatusCode.OK, explicitResponse.StatusCode);
        foreach (var name in CorrelationClients) Assert.Equal(new[] { "outgoing-explicit-admin" }, capture.Sends[name + "|explicit"].Correlation);
        var clients = factory.Services.GetRequiredService<IHttpClientFactory>();
        foreach (var name in CorrelationClients.Where(name => name != nameof(IMistakeHttpClient)))
        {
            using var client = clients.CreateClient(name);
            using var response = await client.GetAsync("https://downstream.test/background?probe=background");
            Assert.Empty(capture.Sends[name + "|background"].Correlation);
        }
        using var mistake = clients.CreateClient(nameof(IMistakeHttpClient)); var before = capture.Sends.Count;
        await Assert.ThrowsAsync<MistakeDownstreamException>(() => mistake.GetAsync("https://mistake.example.test/api/mistakes/item?probe=background"));
        Assert.Equal(before, capture.Sends.Count);
        foreach (var (name, seconds) in new[] { (nameof(HomeworkReferenceClient), 30), ("IdentityService", 30), ("TeacherPortal", 10), ("AssistantPortal", 10), (StorageReferenceCollector.ClientName, 35), (SignaCore.Client.AspNetCore.SignaCoreHostedLoginDefaults.HttpClientName, 30) })
        {
            using var client = clients.CreateClient(name); Assert.Equal(TimeSpan.FromSeconds(seconds), client.Timeout);
        }
    }

    [Fact]
    public async Task Correlation_ProductionAllEightClients_EntryCompletionCancellationAndInnerFailure()
    {
        using var authority = new OidcTestAuthority(); var capture = new CorrelationTransport();
        using var factory = CorrelationFactory(authority, capture, true);
        using var browser = Browser(factory); var cookie = await LoginSession(browser, authority);
        foreach (var name in CorrelationClients)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedApiRoute + "?probe=cancel&client=" + name);
            request.Headers.Add("Cookie", cookie); request.Headers.Add(CorrelationHeaderName, "cancellation-admin");
            using var response = await browser.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("cancelled", await response.Content.ReadAsStringAsync());
            Assert.True(capture.Sends[name + "|cancel"].Content.Disposed);
            Assert.Equal(new[] { "cancellation-admin" }, capture.Sends[name + "|cancel"].Correlation);
        }
    }

    [Fact]
    public async Task Correlation_RealThreeProxies_ResolvedRejectedInputAndCredentialIsolation()
    {
        using var authority = new OidcTestAuthority(); var capture = new CorrelationTransport();
        using var factory = CorrelationFactory(authority, capture, false);
        using var browser = Browser(factory); var cookie = await LoginSession(browser, authority);
        foreach (var (name, route) in new[] { ("IdentityService", "/api/identity/users"), ("TeacherPortal", "/api/teacher-portal/admin/users"), ("AssistantPortal", "/api/assistant-portal/admin/users") })
        foreach (var mode in new[] { "valid", "missing", "invalid", "repeated" })
        {
            var probe = name + "-" + mode;
            using var request = new HttpRequestMessage(HttpMethod.Get, route + "?probe=" + probe);
            request.Headers.Add("Cookie", cookie); request.Headers.Add("X-Admin-AppSecret", "forged-browser-secret"); request.Headers.Add("X-CSRF-TOKEN", "browser-csrf");
            if (mode != "missing") request.Headers.TryAddWithoutValidation(CorrelationHeaderName, mode == "invalid" ? "bad, input" : "proxy-correlation-admin");
            if (mode == "repeated") request.Headers.TryAddWithoutValidation(CorrelationHeaderName, "second-input");
            using var response = await browser.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var resolved = response.Headers.GetValues(CorrelationHeaderName).Single();
            if (mode == "valid") Assert.Equal("proxy-correlation-admin", resolved); else Assert.Matches("^[0-9a-f]{32}$", resolved);
            var sent = capture.Sends[name + "|" + probe]; Assert.Equal(new[] { resolved }, sent.Correlation);
            Assert.Equal("Bearer " + authority.LastAccessToken, sent.Authorization);
            Assert.False(sent.Cookie); Assert.False(sent.Csrf); Assert.False(sent.Host);
            Assert.Equal(name == "IdentityService" ? OidcTestAuthority.Secret : null, sent.AppSecret);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    private sealed class CorrelationProbe : IAuthorizationMiddlewareResultHandler
    {
        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            try { await ProbeAsync(context, authorizeResult); }
            catch (Exception error)
            {
                context.RequestServices.GetRequiredService<CorrelationTransport>().Errors.Enqueue(error);
                throw;
            }
        }
        private static async Task ProbeAsync(HttpContext context, PolicyAuthorizationResult authorizeResult)
        {
            Assert.True(authorizeResult.Succeeded);
            var probe = context.Request.Query["probe"].ToString();
            // Mutating raw input cannot replace the real middleware's private resolved slot.
            context.Request.Headers[CorrelationHeaderName] = "mutated-after-resolution";
            var clients = context.RequestServices.GetRequiredService<IHttpClientFactory>();
            if (context.Request.Query.TryGetValue("client", out var selectedName))
            {
                var name = selectedName.ToString(); using var selected = clients.CreateClient(name); using var cancellation = new CancellationTokenSource();
                using var outgoing = new HttpRequestMessage(HttpMethod.Get, CorrelationUri(name, probe)); outgoing.Options.Set(CancelAtReturn, cancellation);
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selected.SendAsync(outgoing, cancellation.Token));
                Assert.Equal(cancellation.Token, error.CancellationToken);
                var capture = context.RequestServices.GetRequiredService<CorrelationTransport>(); var before = capture.Sends.Count;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selected.GetAsync(CorrelationUri(name, "entry"), cancellation.Token)); Assert.Equal(before, capture.Sends.Count);
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => selected.GetAsync(CorrelationUri(name, "failure"))); Assert.Equal("synthetic transport failure", failure.Message);
                await context.Response.WriteAsync("cancelled"); return;
            }
            foreach (var name in CorrelationClients.Append("external"))
            {
                using var client = clients.CreateClient(name); using var request = new HttpRequestMessage(HttpMethod.Get, CorrelationUri(name, probe));
                if (context.Request.Query.ContainsKey("explicit")) request.Headers.Add(CorrelationHeaderName, "outgoing-explicit-admin");
                if (name == nameof(IMistakeHttpClient))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "forged-outgoing-token");
                    request.Headers.Add("Cookie", "forged-cookie"); request.Headers.Add("X-CSRF-TOKEN", "forged-csrf"); request.Headers.Add("X-Admin-AppSecret", "forged-secret");
                }
                using var response = await client.SendAsync(request, context.RequestAborted);
                if (name == nameof(IMistakeHttpClient))
                {
                    var sent = context.RequestServices.GetRequiredService<CorrelationTransport>().Sends[name + "|" + probe];
                    // The server ticket's access token is the only credential the handler may use.
                    var session = await context.RequestServices.GetRequiredService<AdminSessionAccessor>().AuthenticateAsync(context);
                    Assert.Equal("Bearer " + session.AccessToken, sent.Authorization); Assert.False(sent.Cookie); Assert.False(sent.Csrf); Assert.Null(sent.AppSecret);
                }
            }
            Assert.Equal("student", (await context.RequestServices.GetRequiredService<IStudentHttpClient>().GetStudentAsync("student", context.RequestAborted)).Id);
            Assert.Empty(await context.RequestServices.GetRequiredService<HomeworkReferenceClient>().GetAllImagePathsAsync(context.RequestAborted));
            await context.Response.WriteAsync("sent");
        }
    }

    private sealed class AuthoritySplitTransport(OidcTestAuthority authority, TaggedTransport fallback) : DelegatingHandler(fallback)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => request.RequestUri!.AbsolutePath is "/.well-known/openid-configuration" or "/keys" or "/token"
                ? authority.DispatchAsync(request, cancellationToken)
                : base.SendAsync(request, cancellationToken);
    }

    private static string CorrelationUri(string name, string probe) => (name == nameof(IMistakeHttpClient) ? "https://mistake.example.test/api/mistakes/item" : "https://downstream.test/probe") + "?probe=" + probe;
    private sealed record CorrelationSend(string[] Correlation, string? Authorization, bool Cookie, bool Csrf, bool Host, string? AppSecret, CorrelationContent Content);
    private sealed class CorrelationTransport
    {
        internal ConcurrentDictionary<string, CorrelationSend> Sends { get; } = new();
        internal ConcurrentQueue<Exception> Errors { get; } = new();
        internal async Task<HttpResponseMessage> Send(string name, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query); var probe = query.TryGetValue("probe", out var marker) ? marker.ToString() : "typed";
            if (probe == "failure") throw new InvalidOperationException("synthetic transport failure");
            var content = new CorrelationContent();
            Sends[name + "|" + probe] = new(request.Headers.TryGetValues(CorrelationHeaderName, out var values) ? values.ToArray() : [],
                request.Headers.Authorization?.ToString(), request.Headers.Contains("Cookie"), request.Headers.Contains("X-CSRF-TOKEN"), request.Headers.Contains("Host"),
                request.Headers.TryGetValues("X-Admin-AppSecret", out var secret) ? secret.Single() : null, content);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content }; response.Headers.TryAddWithoutValidation("Set-Cookie", "downstream-private=do-not-replay");
            if (request.Options.TryGetValue(CancelAtReturn, out var cancellation)) cancellation.Cancel();
            return response;
        }
    }
    private sealed class TaggedTransport(string name, CorrelationTransport capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => capture.Send(name, request, cancellationToken);
    }
    private sealed class CorrelationContent : StringContent
    {
        internal CorrelationContent() : base("{\"success\":true,\"data\":{\"id\":\"student\",\"paths\":[],\"hasMore\":false}}", System.Text.Encoding.UTF8, "application/json") { }
        internal bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}
