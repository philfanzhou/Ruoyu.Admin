using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// One shared PostgreSQL container for the ServiceMantle integration collection. The real host
/// startup path runs the database migration orchestration (target preparation plus the
/// shared ServiceMantle orchestrator around AuditMigrationExecutor), so the tests exercise
/// the actual EF/Npgsql stack instead of an in-memory fake.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private readonly string? _previousConsulHost;
    private readonly string? _previousConsulPort;

    public PostgreSqlFixture()
    {
        // The Consul KV source connects eagerly while Program.cs runs (unreachable -> fallback
        // to appsettings, by design). Point it at a closed local port through the supported
        // environment overrides so that fallback is deterministic and fast on every machine.
        _previousConsulHost = Environment.GetEnvironmentVariable("CONSUL_HOST");
        _previousConsulPort = Environment.GetEnvironmentVariable("CONSUL_PORT");
        Environment.SetEnvironmentVariable("CONSUL_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("CONSUL_PORT", "1");

        // The password is randomly generated per container; it never leaves the test process
        // and is not a credential of any real environment. The image is passed via the
        // constructor because the parameterless PostgreSqlBuilder constructor is obsolete
        // (CS0618); with the image pinned there, .WithImage is no longer needed.
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("ruoyu_admin")
            .WithUsername("postgres")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();
    }

    public Task InitializeAsync() => _container.StartAsync();

    /// <summary>
    /// The container's connection string for direct read-only Npgsql access in tests
    /// (row-count guards for the health probe). The randomly generated password never
    /// leaves the test process.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        Environment.SetEnvironmentVariable("CONSUL_HOST", _previousConsulHost);
        Environment.SetEnvironmentVariable("CONSUL_PORT", _previousConsulPort);
    }

    /// <summary>
    /// Configuration overrides pointing <c>PostgreSql:*</c> / <c>Database:Name</c> at the
    /// container, in the shape <c>SharedPostgreSqlConnectionStringFactory</c> reads.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ToConfigOverrides()
    {
        var connection = new DbConnectionStringBuilder
        {
            ConnectionString = _container.GetConnectionString()
        };
        return new Dictionary<string, string?>
        {
            ["PostgreSql:Host"] = Convert.ToString(connection["Host"]),
            ["PostgreSql:Port"] = Convert.ToString(connection["Port"]),
            ["PostgreSql:Username"] = Convert.ToString(connection["Username"]),
            ["PostgreSql:Password"] = Convert.ToString(connection["Password"]),
            ["Database:Name"] = "ruoyu_admin"
        };
    }
}

/// <summary>
/// All container-backed tests live in this single collection: xunit runs collections in
/// parallel, and grouping them keeps isolated schema/outage scenarios from disturbing other
/// tests. Startup migrations also use a real advisory lock. The rest of the suite can still be run without Docker via
/// <c>dotnet test --filter "FullyQualifiedName!~Admin.WebApi.Tests.Integration"</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServiceMantleIntegrationCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "service-mantle-integration";
}

public abstract class ServiceMantleIntegrationTestBase
{
    protected const string CorrelationHeaderName = "x-correlation-id";
    protected const string ProtectedApiRoute = "/api/admin/oss-audit/records";

    // Mirrors the mock entries of appsettings.Testing.json. The identity values are fake
    // gateway credentials for the startup guard only; the OSS values are the same local mock
    // endpoints used for development. None of them are real credentials.
    private static readonly IReadOnlyDictionary<string, string?> RequiredSettings =
        new Dictionary<string, string?>
        {
            ["IdentityService:AppId"] = "integration-test-app-id",
            ["IdentityService:AppSecret"] = "integration-test-app-secret",
            ["IdentityService:Authority"] = "http://127.0.0.1:5002",
            ["IdentityService:Issuer"] = "http://127.0.0.1:5002",
            ["IdentityService:AdditionalValidIssuers"] = "IdentityIssuer",
            ["IdentityService:Audience"] = "PlatformAudience",
            ["IdentityService:RequireHttpsMetadata"] = "false",
            ["IdentityService:ClockSkewSeconds"] = "30",
            ["Oss:InternalEndpoint"] = "127.0.0.1:8333",
            ["Oss:InternalSecure"] = "false",
            ["Oss:AccessKey"] = "mock_access_key",
            ["Oss:SecretKey"] = "mock_secret_key",
            ["Oss:BucketName"] = "ruoyu-study",
            ["Oss:PublicBaseUrl"] = "https://oss.example.com/oss"
        };

    protected ServiceMantleIntegrationTestBase(PostgreSqlFixture database)
    {
        Database = database;
    }

    protected PostgreSqlFixture Database { get; }

    /// <summary>
    /// Builds a factory around the real Program.cs entry point. Accessing <see cref="WebApplicationFactory{TEntryPoint}.Services"/>
    /// or creating a client runs the full host startup, including the migration orchestration.
    /// All overrides go through <c>UseSetting</c>: with the minimal-hosting replay used by
    /// WebApplicationFactory, settings registered this way take precedence over the appsettings
    /// sources, while ConfigureAppConfiguration sources added from the test host are applied
    /// below them.
    /// </summary>
    protected WebApplicationFactory<Program> CreateFactory(
        string? contentRoot = null,
        Action<IServiceCollection>? configureTestServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(webHostBuilder =>
        {
            webHostBuilder.UseEnvironment("Testing");
            if (contentRoot is not null)
            {
                webHostBuilder.UseSetting(WebHostDefaults.ContentRootKey, contentRoot);
            }

            foreach (var (key, value) in Database.ToConfigOverrides())
            {
                webHostBuilder.UseSetting(key, value);
            }

            foreach (var (key, value) in RequiredSettings)
            {
                webHostBuilder.UseSetting(key, value);
            }

            webHostBuilder.UseSetting("Loki:Uri", "");
            if (settings is not null)
            {
                foreach (var (key, value) in settings) webHostBuilder.UseSetting(key, value);
            }

            if (configureTestServices is not null)
            {
                webHostBuilder.ConfigureTestServices(configureTestServices);
            }
        });
    }

    /// <summary>
    /// An explicit temporary content root, so the SPA branch under test is chosen by this test
    /// and not by any leftover wwwroot in the local Admin.WebApi directory.
    /// </summary>
    protected static string CreateTempContentRoot(bool withWwwrootStub)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ruoyu-admin-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        if (withWwwrootStub)
        {
            Directory.CreateDirectory(Path.Combine(path, "wwwroot"));
            File.WriteAllText(
                Path.Combine(path, "wwwroot", "index.html"),
                "<!doctype html><html><head><title>stub</title></head><body>stub-index-marker</body></html>");
        }

        return path;
    }

    protected static string? Field(
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        string name)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Key, name, StringComparison.Ordinal))
            {
                return field.Value?.ToString();
            }
        }

        return null;
    }
}

/// <summary>
/// Records the ILogger scopes opened during a request. The ServiceMantle request scope is an
/// <c>IReadOnlyList&lt;KeyValuePair&lt;string, object?&gt;&gt;</c> of named fields; replacing the logger factory
/// with a plain one captures that scope state directly — the same scope surface the Serilog
/// pipeline consumes. Actual Console/Loki delivery is verified separately in AdminLoggingTests.
/// </summary>
public sealed class RequestScopeCapture : ILoggerProvider
{
    private readonly object _gate = new();

    public List<IReadOnlyList<KeyValuePair<string, object?>>> Scopes { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(RequestScopeCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> fields)
            {
                lock (owner._gate)
                {
                    owner.Scopes.Add(fields);
                }
            }

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
