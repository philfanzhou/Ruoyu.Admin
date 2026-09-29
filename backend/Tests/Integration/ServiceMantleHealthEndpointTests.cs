using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// ServiceMantle health endpoints mapped anonymously at the root route group in Program.cs.
/// /health/live always answers 200 {"status":"live"} without a JWT; /health/ready and the
/// /health alias are honestly fail-closed 503 health.probe_failed because Admin registers no
/// IServiceHealthSnapshotSource yet (this locks the delivered fail-closed contract); /api/*
/// keeps requiring authentication (FallbackPolicy untouched); and the SPA fallback rewrite
/// excludes /health so the endpoints answer JSON even in the integrated (wwwroot) deployment
/// mode, while every other non-/api route keeps serving the SPA anonymously.
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed partial class ServiceMantleHealthEndpointTests : ServiceMantleIntegrationTestBase
{
    private const string LiveBody = "{\"status\":\"live\"}";

    private const string NotReadyBody =
        "{\"status\":\"not_ready\",\"phase\":null,\"migrationStatus\":null,\"databaseStatus\":null,\"errorCode\":\"health.probe_failed\"}";

    public ServiceMantleHealthEndpointTests(PostgreSqlFixture database) : base(database)
    {
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex GeneratedIdPattern();

    [Fact]
    public async Task LiveEndpoint_IsAnonymous_ReturnsLiveJsonWithCorrelationId()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(LiveBody, await response.Content.ReadAsStringAsync());

        // The correlation middleware is the first middleware in the pipeline, so the health
        // response carries the same x-correlation-id response header as every other response.
        Assert.Matches(GeneratedIdPattern(), response.Headers.GetValues(CorrelationHeaderName).Single());
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task ReadinessEndpoints_FailClosed_ReturnProbeFailed(string route)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(route);

        // Admin registers no IServiceHealthSnapshotSource (and no readiness contributor), so
        // readiness is unavailable instead of a fake "ready": 503 with the fixed not_ready
        // payload. The body is asserted verbatim to lock the fail-closed contract.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotReadyBody, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProtectedApiRoutes_StillRequireAuthentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // The FallbackPolicy (RequireAuthenticatedUser) is untouched: unauthenticated /api
        // requests still 401, while the health endpoints on the same client stay anonymous.
        using var api = await client.GetAsync(ProtectedApiRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task IntegratedContentRoot_HealthEndpointsBypassSpaFallback()
    {
        var workingDirectory = CreateTempContentRoot(withWwwrootStub: true);
        try
        {
            using var factory = CreateFactory(contentRoot: workingDirectory);
            using var client = factory.CreateClient();

            // /health/* answers the library JSON instead of being rewritten to the SPA
            // index.html by the MapWhen fallback branch.
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("application/json", live.Content.Headers.ContentType?.MediaType);
            Assert.Equal(LiveBody, await live.Content.ReadAsStringAsync());

            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal(NotReadyBody, await ready.Content.ReadAsStringAsync());

            // SPA contract unchanged: "/" and any other non-/api, non-/health route still
            // serves the stub index.html anonymously.
            using var root = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, root.StatusCode);
            Assert.Equal("text/html", root.Content.Headers.ContentType?.MediaType);
            Assert.Contains("stub-index-marker", await root.Content.ReadAsStringAsync());

            using var spaRoute = await client.GetAsync("/students");
            Assert.Equal(HttpStatusCode.OK, spaRoute.StatusCode);
            Assert.Contains("stub-index-marker", await spaRoute.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DevContentRoot_RootStaysAnonymous_AndHealthEndpointsReachable()
    {
        var workingDirectory = CreateTempContentRoot(withWwwrootStub: false);
        try
        {
            using var factory = CreateFactory(contentRoot: workingDirectory);
            using var client = factory.CreateClient();

            using var root = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, root.StatusCode);
            Assert.Contains("Student Admin WebAPI is running.", await root.Content.ReadAsStringAsync());

            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(LiveBody, await live.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
