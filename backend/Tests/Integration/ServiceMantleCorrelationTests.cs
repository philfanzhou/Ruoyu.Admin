using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ServiceMantle.Web.Logging;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// Per-request behavior of the correlation middleware wired as the first middleware in
/// Program.cs: a single shape-valid inbound x-correlation-id is echoed verbatim; missing,
/// overlong, illegal, comma-joined, or repeated inputs are discarded whole and replaced by a
/// generated 32-character lowercase hex id; and for one and the same request the response
/// header and the request log scope carry the same id alongside the ServiceMantle identity
/// fields.
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed partial class ServiceMantleCorrelationTests : ServiceMantleIntegrationTestBase
{
    private const string ValidValue = "e2e-correlation-0123456789abcdef";

    public ServiceMantleCorrelationTests(PostgreSqlFixture database) : base(database)
    {
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex GeneratedIdPattern();

    [Fact]
    public async Task ValidHeader_IsEchoedVerbatim()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationHeaderName, ValidValue);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ValidValue, response.Headers.GetValues(CorrelationHeaderName).Single());
    }

    [Fact]
    public async Task MissingHeader_GeneratesNewId()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Matches(GeneratedIdPattern(), root.Headers.GetValues(CorrelationHeaderName).Single());

        // The header is injected before authentication runs: even the 401 of an
        // unauthenticated /api request carries the correlation id.
        using var api = await client.GetAsync(ProtectedApiRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Matches(GeneratedIdPattern(), api.Headers.GetValues(CorrelationHeaderName).Single());
    }

    [Theory]
    [InlineData(" ")]        // whitespace-only is rejected whole
    [InlineData("a b")]      // illegal character
    [InlineData("a,b")]      // comma-joined value
    [InlineData(".leading")] // first character is not alphanumeric
    public async Task RejectedValue_IsReplacedByGeneratedId(string value)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationHeaderName, value);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(GeneratedIdPattern(), response.Headers.GetValues(CorrelationHeaderName).Single());
    }

    [Fact]
    public async Task OverlongValue_IsReplacedByGeneratedId()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CorrelationHeaderName,
            new string('a', 65)); // one character beyond the 64-character maximum

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(GeneratedIdPattern(), response.Headers.GetValues(CorrelationHeaderName).Single());
    }

    [Fact]
    public async Task RepeatedHeader_IsDiscardedWhole()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationHeaderName, ValidValue);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CorrelationHeaderName, "another-valid-id-1234");

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(GeneratedIdPattern(), response.Headers.GetValues(CorrelationHeaderName).Single());
    }

    [Fact]
    public async Task ResponseHeader_AndLogScope_CarrySameCorrelationId()
    {
        var capture = new RequestScopeCapture();
        using var factory = CreateFactory(configureTestServices: services =>
        {
            // Swap the Serilog logger factory for a plain per-factory one (see
            // RequestScopeCapture): the request scope state is recorded directly instead of
            // going through the real Console/Loki pipeline (covered separately by AdminLoggingTests).
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(
                _ => LoggerFactory.Create(logging => logging.AddProvider(capture)));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationHeaderName, ValidValue);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationHeaderName).Single();
        Assert.Equal(ValidValue, correlationId);

        // The scope opened by the middleware during this request carries the same id, together
        // with the identity fields of the host's ServiceLogContext.
        var scope = capture.Scopes.Single(fields => Field(fields, "CorrelationId") == correlationId);
        var logContext = factory.Services.GetRequiredService<ServiceLogContext>();
        Assert.Equal(logContext.ServiceName, Field(scope, "ServiceName"));
        Assert.Equal(logContext.ServiceVersion, Field(scope, "ServiceVersion"));
        Assert.Equal(logContext.InstanceId, Field(scope, "InstanceId"));
    }
}
