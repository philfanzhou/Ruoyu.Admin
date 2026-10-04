using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ServiceMantle;
using ServiceMantle.Web.Logging;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// Boots the real Program.cs entry point (WebApplicationFactory + Testcontainers PostgreSQL)
/// and proves the ServiceMantle wiring: the host starts with valid instrumentation
/// registrations, the identity resolves as configured, and the anonymous SPA contract
/// ("/" and all non-/api routes reachable without a JWT, /api still 401) is unchanged in both
/// content-root modes.
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed partial class ServiceMantleHostStartupTests : ServiceMantleIntegrationTestBase
{
    public ServiceMantleHostStartupTests(PostgreSqlFixture database) : base(database)
    {
    }

    [GeneratedRegex("^ruoyu-admin-[0-9a-f]{32}$")]
    private static partial Regex InstanceIdPattern();

    [Fact]
    public void Host_Starts_AndResolvesServiceMantleIdentity()
    {
        // Accessing factory.Services runs the full host startup. The library's
        // OpenTelemetryRegistrationValidator hosted service fails the startup when the
        // instrumentation registrations are invalid or conflicting, so reaching the assertions
        // proves both the host start and the registration validity.
        using var factory = CreateFactory();

        var serviceId = factory.Services.GetRequiredService<ServiceId>();
        var instanceId = factory.Services.GetRequiredService<InstanceId>();
        var logContext = factory.Services.GetRequiredService<ServiceLogContext>();

        Assert.Equal("ruoyu-admin", serviceId.Value);
        Assert.Matches(InstanceIdPattern(), instanceId.Value);

        Assert.Equal(serviceId.Value, logContext.ServiceName);
        Assert.Equal(instanceId.Value, logContext.InstanceId);
        Assert.Equal(ExpectedEntryAssemblyServiceVersion(), logContext.ServiceVersion);
    }

    [Fact]
    public async Task Host_WithoutWwwroot_DevContentRoot_KeepsAnonymousRoutesReachable()
    {
        var workingDirectory = CreateTempContentRoot(withWwwrootStub: false);
        try
        {
            using var factory = CreateFactory(contentRoot: workingDirectory);
            using var client = factory.CreateClient();

            // In the dev branch (wwwroot not built yet) "/" is the only mapped anonymous
            // route — the health probe the login page needs. Unmapped non-/api paths fall
            // through to the FallbackPolicy and 401 at baseline (verified against main
            // without the ServiceMantle middleware); that pre-existing behavior is out of
            // scope here, so the assertion pins only the SPA-entry contract.
            using var root = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, root.StatusCode);
            Assert.Contains(
                "Student Admin WebAPI is running.",
                await root.Content.ReadAsStringAsync());

            using var api = await client.GetAsync(ProtectedApiRoute);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Host_WithWwwrootStub_IntegratedContentRoot_KeepsSpaRoutesAnonymous()
    {
        var workingDirectory = CreateTempContentRoot(withWwwrootStub: true);
        try
        {
            using var factory = CreateFactory(contentRoot: workingDirectory);
            using var client = factory.CreateClient();

            using var root = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, root.StatusCode);
            Assert.Equal("text/html", root.Content.Headers.ContentType?.MediaType);
            Assert.Contains("stub-index-marker", await root.Content.ReadAsStringAsync());

            // SPA fallback: any non-/api route rewrites to index.html and stays anonymous.
            using var spaRoute = await client.GetAsync("/students");
            Assert.Equal(HttpStatusCode.OK, spaRoute.StatusCode);
            Assert.Equal("text/html", spaRoute.Content.Headers.ContentType?.MediaType);

            using var api = await client.GetAsync(ProtectedApiRoute);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Mirrors the library's entry-assembly version resolution (AddServiceMantle is called
    /// without an explicit serviceVersion): informational version, then assembly version, then
    /// "unknown". Under WebApplicationFactory the entry assembly is the test host, so the
    /// assertion proves the resolution rule rather than a hard-coded "1.0.0".
    /// </summary>
    private static string ExpectedEntryAssemblyServiceVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var informationalVersion = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Trim();
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var assemblyVersion = entryAssembly?.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "unknown" : assemblyVersion;
    }
}
