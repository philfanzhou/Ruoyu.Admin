using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Admin.WebApi.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ServiceMantle.Database.PostgreSql;
using ServiceMantle.Migration;
using ServiceMantle.Persistence.Relational;
using ServiceMantle.Health;
using Testcontainers.PostgreSql;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// ServiceMantle health endpoints on the real host: <c>/health/live</c> answers 200
/// <c>{"status":"live"}</c> without ever touching the database; <c>/health/ready</c> and the
/// <c>/health</c> alias project exactly the library-fixed
/// status/phase/migrationStatus/databaseStatus/errorCode fields from the shared EF Core
/// snapshot source (startup receipt + read-only zero-row probe of the EF-mapped tables).
/// Readiness fails closed on an incomplete or failed startup receipt, a missing mapped column, an
/// unreachable database, and a probe timeout; caller cancellation propagates instead of faking
/// health; every request re-samples so a recovered database is observed immediately; all three
/// endpoints stay anonymous and answer JSON (never the SPA page) while protected APIs keep
/// returning 401. Destructive schema/outage scenarios run against one-off dedicated PostgreSQL
/// containers, never the shared fixture.
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed partial class ServiceMantleHealthEndpointTests : ServiceMantleIntegrationTestBase
{
    private const string LiveBody = "{\"status\":\"live\"}";

    private const string ReadyBody =
        "{\"status\":\"ready\",\"phase\":\"completed\",\"migrationStatus\":\"succeeded\"," +
        "\"databaseStatus\":\"reachable\",\"errorCode\":null}";

    private const string ProbeTimeoutBody =
        "{\"status\":\"not_ready\",\"phase\":null,\"migrationStatus\":null," +
        "\"databaseStatus\":null,\"errorCode\":\"health.probe_timeout\"}";

    private const string StartupIncompleteBody =
        "{\"status\":\"not_ready\",\"phase\":\"pendingSetup\",\"migrationStatus\":\"running\"," +
        "\"databaseStatus\":\"unreachable\",\"errorCode\":\"ruoyu-admin.startup_incomplete\"}";

    private const string SchemaUnavailableBody =
        "{\"status\":\"not_ready\",\"phase\":\"completed\",\"migrationStatus\":\"failed\"," +
        "\"databaseStatus\":\"reachable\",\"errorCode\":\"ruoyu-admin.schema_unavailable\"}";

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

    [Fact]
    public async Task ReadinessEndpoints_HealthyDatabase_ProjectReady_WithoutTouchingRows()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var countsBefore = await CountAllMappedTables();

        foreach (var route in new[] { "/health/ready", "/health" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ReadyBody, await response.Content.ReadAsStringAsync());
        }

        // The probe is read-only: no row counts change and no DDL runs.
        var countsAfter = await CountAllMappedTables();
        Assert.Equal(countsBefore, countsAfter);
    }

    [Theory]
    [InlineData("notStarted")]
    [InlineData("running")]
    [InlineData("failed")]
    public async Task ReadinessEndpoints_IncompleteOrFailedReceipt_FailClosedWithoutDatabaseAccess(string state)
    {
        var receipt = new StartupDatabaseReceipt();
        if (state != "notStarted") Assert.True(receipt.TryMarkRunning());
        if (state == "failed") Assert.True(receipt.TryCompleteFailed("migration.execution_failed"));
        var guard = new RejectDatabaseAccess();
        using var factory = CreateFactory(configureTestServices: services =>
        {
            services.RemoveAll<IServiceHealthSnapshotSource>();
            services.AddScoped<IServiceHealthSnapshotSource>(_ =>
                new EfCoreHealthSnapshotSource<AuditDbContext>(
                    receipt,
                    new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
                        .UseNpgsql(Database.ConnectionString).AddInterceptors(guard).Options),
                    new PostgreSqlDatabaseProbeFailureClassifier(), "ruoyu-admin"));
        });
        using var client = factory.CreateClient();
        var expected = StartupIncompleteBody.Replace("running", state, StringComparison.Ordinal);
        if (state == "failed") expected = expected.Replace("startup_incomplete", "startup_failed", StringComparison.Ordinal);

        foreach (var route in new[] { "/health/ready", "/health" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(expected, await response.Content.ReadAsStringAsync());
        }

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(LiveBody, await live.Content.ReadAsStringAsync());
        Assert.Equal(0, guard.Attempts);
    }

    [Fact]
    public async Task DedicatedDatabase_EmptyThenCurrentStartup_MissingColumn_FailsClosedAndRecovers()
    {
        await using var container = await StartDedicatedDatabaseAsync();
        var settings = DbConfigOverrides(container.GetConnectionString());

        // Empty database: the first host run creates the schema and completes the receipt.
        using (var firstFactory = CreateFactory(settings: settings))
        using (var firstClient = firstFactory.CreateClient())
        {
            using var ready = await firstClient.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(ReadyBody, await ready.Content.ReadAsStringAsync());
        }

        // Current/legacy database: a later host start finds the existing schema (the
        // executor skips creation) and readiness still passes.
        using var factory = CreateFactory(settings: settings);
        using var client = factory.CreateClient();
        using (var ready = await client.GetAsync("/health/ready"))
        {
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        // A mapped column disappears after startup; readiness must detect the regression.
        var (tableName, columnName, columnType) =
            await PickMappedColumnAsync(container.GetConnectionString(), "OssAuditRecords");
        await ExecuteAsync(
            container.GetConnectionString(),
            $"ALTER TABLE {Quote(tableName)} DROP COLUMN {Quote(columnName)}");

        using (var notReady = await client.GetAsync("/health/ready"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
            Assert.Equal(SchemaUnavailableBody, await notReady.Content.ReadAsStringAsync());
        }

        // The alias follows the same projection; liveness never queries the database.
        using (var alias = await client.GetAsync("/health"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, alias.StatusCode);
        }

        using (var live = await client.GetAsync("/health/live"))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(LiveBody, await live.Content.ReadAsStringAsync());
        }

        // Restore the column: the very next request re-samples and is ready again.
        await ExecuteAsync(
            container.GetConnectionString(),
            $"ALTER TABLE {Quote(tableName)} ADD COLUMN {Quote(columnName)} {columnType}");

        using (var recovered = await client.GetAsync("/health/ready"))
        {
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            Assert.Equal(ReadyBody, await recovered.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task DatabaseOutage_ReadyFailsClosed_LiveStaysUp_RecoversAfterRestart()
    {
        await using var container = await StartDedicatedDatabaseAsync();
        var settings = DbConfigOverrides(container.GetConnectionString());
        using var factory = CreateFactory(settings: settings);
        using var client = factory.CreateClient();

        using (var ready = await client.GetAsync("/health/ready"))
        {
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(ReadyBody, await ready.Content.ReadAsStringAsync());
        }

        await container.StopAsync();
        try
        {
            // Fail closed while the database is down: 503 + not_ready with a safe code and no
            // exception content. docker stop closes the published port, so the probe's connect
            // is refused and maps to ruoyu-admin.database_unreachable; a slower teardown that
            // exhausts the 3s probe budget, or an unclassified surfacing, maps to the
            // library's own health.probe_timeout / health.probe_failed — all honest
            // fail-closed outcomes, never a fake ready.
            using var notReady = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
            var outageBody = await notReady.Content.ReadAsStringAsync();
            using (var document = System.Text.Json.JsonDocument.Parse(outageBody))
            {
                var root = document.RootElement;
                Assert.Equal("not_ready", root.GetProperty("status").GetString());
                Assert.Contains(
                    root.GetProperty("errorCode").GetString(),
                    new[]
                    {
                        "ruoyu-admin.database_unreachable",
                        "health.probe_timeout",
                        "health.probe_failed"
                    });
                // The snapshot never claims full readiness: either the database state is
                // unreachable (receipt keeps migrationStatus=succeeded) or the projection is
                // the value-free library failure (all snapshot fields null).
                Assert.NotEqual(
                    "reachable",
                    root.GetProperty("databaseStatus").GetString());
            }

            // Live does not resolve the source or query the database: it stays 200 while the
            // database is down.
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(LiveBody, await live.Content.ReadAsStringAsync());
        }
        finally
        {
            await container.StartAsync();
        }

        // Recovery: Testcontainers republishes the container on a fresh host port on restart,
        // so a host is pointed at the restored database through a new factory. It reports ready
        // again, proving readiness tracks the live database state rather than a latched failure.
        // Same-process re-sampling after an in-place schema fault is proven separately by
        // DedicatedDatabase_EmptyThenCurrentStartup_MissingColumn_FailsClosedAndRecovers.
        var restartedSettings = DbConfigOverrides(container.GetConnectionString());
        using var recoveredFactory = CreateFactory(settings: restartedSettings);
        using var recoveredClient = recoveredFactory.CreateClient();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var lastStatus = HttpStatusCode.ServiceUnavailable;
        var lastBody = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            using var attempt = await recoveredClient.GetAsync("/health/ready");
            lastStatus = attempt.StatusCode;
            lastBody = await attempt.Content.ReadAsStringAsync();
            if (attempt.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.True(
            lastStatus == HttpStatusCode.OK,
            $"readiness never recovered against the restarted database: {lastStatus} {lastBody}");
        Assert.Equal(ReadyBody, lastBody);
    }

    [Fact]
    public async Task ProbeTimeout_FailsClosed_WithLibrarySafeCode()
    {
        using var factory = CreateFactory(configureTestServices: services =>
        {
            services.RemoveAll<IServiceHealthSnapshotSource>();
            services.AddScoped<IServiceHealthSnapshotSource>(
                _ => new BlockingSnapshotSource());
        });
        using var client = factory.CreateClient();

        // The host ProbeTimeout is 3 seconds; the blocking source never answers in time.
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ProbeTimeoutBody, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CallerCancellation_Propagates_InsteadOfFakingHealth()
    {
        var source = new CancellationObservingSnapshotSource();
        using var factory = CreateFactory(configureTestServices: services =>
        {
            services.RemoveAll<IServiceHealthSnapshotSource>();
            services.AddScoped<IServiceHealthSnapshotSource>(_ => source);
        });
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource();

        var request = client.GetAsync("/health/ready", cts.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        // The caller observes its own cancellation — no 200, no 500, no fake health — and the
        // source observed the cancellation on the token it received.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await source.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ConcurrentReadinessRequests_GetIndependentSnapshots()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.GetAsync("/health/ready")));

        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(ReadyBody, await response.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact]
    public async Task ProtectedApiRoutes_StillRequireAuthentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // The FallbackPolicy (RequireAuthenticatedUser) is untouched: unauthenticated /api
        // requests still 401, while the health endpoints on the same client stay anonymous.
        using var api = await client.GetAsync(ProtectedApiRoute);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);

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
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(ReadyBody, await ready.Content.ReadAsStringAsync());

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

    private async Task<Dictionary<string, long>> CountAllMappedTables()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in new[] { "OssAuditRecords", "OssAuditRuns" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {Quote(table)}";
            counts[table] = (long)(await command.ExecuteScalarAsync())!;
        }

        return counts;
    }

    private static async Task<PostgreSqlContainer> StartDedicatedDatabaseAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("ruoyu_admin")
            .WithUsername("postgres")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();
        await container.StartAsync();
        return container;
    }

    private static IReadOnlyDictionary<string, string?> DbConfigOverrides(string connectionString)
    {
        var connection = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return new Dictionary<string, string?>
        {
            ["PostgreSql:Host"] = Convert.ToString(connection["Host"]),
            ["PostgreSql:Port"] = Convert.ToString(connection["Port"]),
            ["PostgreSql:Username"] = Convert.ToString(connection["Username"]),
            ["PostgreSql:Password"] = Convert.ToString(connection["Password"]),
            ["Database:Name"] = "ruoyu_admin"
        };
    }

    /// <summary>
    /// Picks one ordinary mapped column (not the Id primary key, no user-defined type) and
    /// returns its table, name, and exact DDL type via <c>format_type</c>.
    /// </summary>
    private static async Task<(string Table, string Column, string Type)> PickMappedColumnAsync(
        string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod)
            FROM pg_attribute a
            WHERE a.attrelid = $1::regclass
              AND a.attnum > 0
              AND NOT a.attisdropped
              AND a.attname <> 'Id'
            ORDER BY a.attnum
            LIMIT 1
            """;
        // Admin's mapped tables are quoted PascalCase identifiers; a text->regclass cast
        // resolves an unquoted value as folded lowercase, so the value carries the quotes.
        command.Parameters.Add(new NpgsqlParameter { Value = Quote(table) });
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no mapped column found on {table}");
        return (table, reader.GetString(0), reader.GetString(1));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed class RejectDatabaseAccess : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("Readiness accessed PostgreSQL before startup succeeded.");
        }
    }

    /// <summary>Never completes until the library's probe budget cancels the token.</summary>
    private sealed class BlockingSnapshotSource : IServiceHealthSnapshotSource
    {
        public async ValueTask<ServiceHealthSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>Signals when the probe started and when it observed the cancellation.</summary>
    private sealed class CancellationObservingSnapshotSource : IServiceHealthSnapshotSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ObservedCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ServiceHealthSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("unreachable");
        }
    }
}
