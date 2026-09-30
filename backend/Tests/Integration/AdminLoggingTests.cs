using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Admin.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceMantle.Logging.Pipeline;
using ServiceMantle.Web.Logging;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed class AdminLoggingTests(PostgreSqlFixture database) : ServiceMantleIntegrationTestBase(database)
{
    [Fact]
    public async Task RealHost_ConsoleAndLokiShareFilteredSanitizedRequestEvents()
    {
        var batches = new ConcurrentQueue<string>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lokiBuilder = WebApplication.CreateBuilder();
        lokiBuilder.Logging.ClearProviders();
        lokiBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var loki = lokiBuilder.Build();
        loki.MapPost("/loki/api/v1/push", async context =>
        {
            batches.Enqueue(await new StreamReader(context.Request.Body).ReadToEndAsync());
            received.TrySetResult();
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
            using var factory = CreateFactory(configureTestServices: services =>
                services.AddControllers().AddApplicationPart(typeof(LoggingProbeController).Assembly),
                settings: new Dictionary<string, string?> { ["Loki:Uri"] = address });
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/logging-probe");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var correlation = response.Headers.GetValues(CorrelationHeaderName).Single();
            await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Batching is asynchronous; wait for the request event, not just startup events.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!batches.Any(batch => batch.Contains("probe.application")))
                await Task.Delay(20, timeout.Token);
            var lines = batches.SelectMany(batch => JsonDocument.Parse(batch).RootElement
                .GetProperty("streams").EnumerateArray().SelectMany(stream =>
                {
                    Assert.Equal("Ruoyu.Admin", stream.GetProperty("stream").GetProperty("service").GetString());
                    Assert.True(stream.GetProperty("stream").TryGetProperty("level", out _));
                    return stream.GetProperty("values").EnumerateArray().Select(value => value[1].GetString()!).ToArray();
                })).ToArray();
            var remote = string.Join('\n', lines);
            var console = output.ToString();
            foreach (var actual in new[] { console, remote })
            {
                Assert.Contains("probe.application", actual);
                Assert.Contains("probe.aspnet.warning", actual);
                Assert.Contains("probe.ef.warning", actual);
                Assert.DoesNotContain("probe.aspnet.information", actual);
                Assert.DoesNotContain("probe.ef.information", actual);
                Assert.DoesNotContain(LoggingProbeController.Canary, actual);
                var requestLine = actual.Split('\n').First(line => line.Contains("probe.application"));
                Assert.Contains(correlation, requestLine);
                Assert.Contains("ruoyu-admin", requestLine);
                Assert.Contains(factory.Services.GetRequiredService<ServiceLogContext>().ServiceVersion, requestLine);
                Assert.Contains(factory.Services.GetRequiredService<ServiceLogContext>().InstanceId, requestLine);
                Assert.DoesNotContain("MachineName", requestLine);
                Assert.DoesNotContain("ThreadId", requestLine);
            }
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task UnreachableLoki_RetriesWithoutBlockingBusinessRequest()
    {
        var attempts = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var loki = builder.Build();
        loki.MapPost("/loki/api/v1/push", context =>
        {
            Interlocked.Increment(ref attempts);
            context.Response.StatusCode = 503;
            return Task.CompletedTask;
        });
        await loki.StartAsync();
        var address = loki.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var factory = CreateFactory(settings: new Dictionary<string, string?> { ["Loki:Uri"] = address });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (Volatile.Read(ref attempts) < 2) await Task.Delay(20, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Theory]
    [InlineData("relative/canary", "loki.invalid_endpoint")]
    [InlineData("http://user:canary@127.0.0.1:3100", "loki.invalid_endpoint")]
    [InlineData("https://example.test?token=canary", "loki.invalid_endpoint")]
    public async Task InvalidLoki_FailsWithSafeCode(string uri, string expected)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Loki:Uri"] = uri;
        builder.AddAdminLogging();
        using var host = builder.Build();
        var failure = await Assert.ThrowsAsync<SerilogConfigurationException>(() => host.StartAsync());
        Assert.Equal(expected, failure.ErrorCode);
        Assert.DoesNotContain("canary", failure.ToString());
    }

    [Fact]
    public async Task EmptyLoki_IsDisabledAndHostStarts()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}

[ApiController, AllowAnonymous, Route("api/logging-probe")]
public sealed class LoggingProbeController(ILoggerFactory factory) : ControllerBase
{
    public const string Canary = "fictitious-logging-password-canary";
    [HttpGet]
    public IActionResult Get()
    {
        factory.CreateLogger("Admin.LoggingProbe").LogInformation("probe.application {Password}", Canary);
        factory.CreateLogger("Microsoft.AspNetCore.Hosting").LogInformation("probe.aspnet.information");
        factory.CreateLogger("Microsoft.AspNetCore.Hosting").LogWarning("probe.aspnet.warning");
        factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogInformation("probe.ef.information");
        factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogWarning("probe.ef.warning");
        return Ok();
    }
}
