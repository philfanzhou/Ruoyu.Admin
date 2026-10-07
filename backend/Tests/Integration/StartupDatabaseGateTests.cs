using System.Net;
using Admin.WebApi.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Npgsql;
using ServiceMantle;
using ServiceMantle.Bootstrap;
using ServiceMantle.Database.PostgreSql;
using ServiceMantle.Database.PostgreSql.Migration;
using ServiceMantle.Health;
using ServiceMantle.Migration;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed class StartupDatabaseGateTests(PostgreSqlFixture database) : ServiceMantleIntegrationTestBase(database)
{
    [Theory]
    [InlineData("Database:AllowCreate", "private-invalid-switch")]
    [InlineData("PostgreSql:Port", "private-invalid-port")]
    [InlineData("ConnectionStrings:AuditDb", "private-invalid-connection")]
    [InlineData("ConnectionStrings:AuditDb", "")]
    public async Task Host_InvalidConfiguration_RefusesBeforeAnyDatabaseSideEffect(string key, string value)
    {
        var target = "audit_invalid_" + Guid.NewGuid().ToString("N");
        var settings = new Dictionary<string, string?> { ["Database:Name"] = target, [key] = value };
        if (key == "ConnectionStrings:AuditDb") settings["PostgreSql:Host"] = "";
        var provider = PreparationProvider();
        var witness = new StartWitness();
        using var factory = CreateFactory(settings: settings, configureTestServices: services =>
        {
            services.RemoveAll<IDatabaseTargetPreparationProvider>();
            services.AddSingleton(provider.Object);
            services.AddSingleton<IHostedService>(witness);
        });
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(WellKnownDatabaseTargetPreparationErrorCodes.InvalidTarget, exception.ToString());
        Assert.DoesNotContain("private-", exception.ToString());
        Assert.DoesNotContain("Host=", exception.ToString());
        Assert.DoesNotContain("Password=", exception.ToString());
        Assert.False(witness.Started);
        provider.Verify(p => p.ObserveAsync(It.IsAny<BootstrapDatabaseConfiguration>(),
            It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(p => p.PrepareAsync(It.IsAny<DatabaseTargetPreparationRequest>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pg_database WHERE datname = $1";
        command.Parameters.AddWithValue(target);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false, WellKnownMigrationErrorCodes.FinalStateInvalid)]
    [InlineData(true, WellKnownMigrationErrorCodes.LockTimeout)]
    public void Host_FinalInspectionOrLockFailure_RefusesBeforeHostedServiceStart(bool lockFailure, string code)
    {
        var executor = new Mock<IDatabaseMigrationExecutor>(MockBehavior.Strict);
        executor.Setup(p => p.InspectAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(MigrationObservationState.Empty));
        executor.Setup(p => p.ExecuteAsync(It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        var receipt = new StartupDatabaseReceipt();
        var witness = new StartWitness();
        using var factory = CreateFactory(configureTestServices: services =>
        {
            services.RemoveAll<IDatabaseMigrationExecutor>();
            services.AddSingleton(executor.Object);
            services.RemoveAll<StartupDatabaseReceipt>();
            services.AddSingleton(receipt);
            services.AddSingleton<IHostedService>(witness);
            if (lockFailure)
            {
                var provider = new Mock<IDatabaseMigrationLockProvider>();
                provider.SetupGet(p => p.ProviderId).Returns(WellKnownDatabaseProviderIds.PostgreSql);
                provider.Setup(p => p.AcquireAsync(It.IsAny<ServiceId>(), It.IsAny<BootstrapDatabaseConfiguration>(),
                        It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                    .Throws(new DatabaseMigrationLockException(code, "The test lock wait expired."));
                services.RemoveAll<IDatabaseMigrationLockProvider>();
                services.AddSingleton(provider.Object);
            }
        });
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(code, exception.ToString());
        Assert.DoesNotContain(Database.ConnectionString, exception.ToString());
        Assert.Equal(ServiceMigrationReadinessState.Failed, receipt.State);
        Assert.Equal(code, receipt.ErrorCode);
        Assert.False(witness.Started);
        executor.Verify(p => p.ExecuteAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(lockFailure ? 0 : 1));
    }

    [Theory]
    [InlineData("unreachable", WellKnownDatabaseTargetPreparationErrorCodes.ServerUnreachable, 0)]
    [InlineData("authentication", WellKnownDatabaseTargetPreparationErrorCodes.AuthenticationFailed, 0)]
    [InlineData("permission", WellKnownDatabaseTargetPreparationErrorCodes.PermissionDenied, 0)]
    [InlineData("identity", WellKnownDatabaseTargetPreparationErrorCodes.InvalidTarget, 0)]
    [InlineData("prepareFailure", WellKnownDatabaseTargetPreparationErrorCodes.PreparationFailed, 1)]
    [InlineData("notConnectableAfterPreparation", WellKnownDatabaseTargetPreparationErrorCodes.NotConnectableAfterPreparation, 1)]
    public void Host_PreparationRefusal_FailsReceipt_DoesNotStartServicesOrExecutor(
        string scenario, string code, int preparations)
    {
        var provider = PreparationProvider();
        var observations = 0;
        provider.Setup(p => p.ObserveAsync(It.IsAny<BootstrapDatabaseConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(++observations == 1 && preparations == 1
                ? DatabaseTargetObservation.TargetMissing()
                : DatabaseTargetObservation.TargetUnreachable(code)));
        provider.Setup(p => p.PrepareAsync(It.IsAny<DatabaseTargetPreparationRequest>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() => scenario == "prepareFailure"
                ? throw new InvalidOperationException("private-driver-text SELECT secret-test-value")
                : ValueTask.FromResult(DatabaseTargetPreparationResult.Success(DatabaseTargetPreparationOutcome.Created)));
        var executor = new Mock<IDatabaseMigrationExecutor>(MockBehavior.Strict);
        var receipt = new StartupDatabaseReceipt();
        var witness = new StartWitness();
        using var factory = CreateFactory(
            settings: new Dictionary<string, string?> { ["Database:AllowCreate"] = "true" },
            configureTestServices: services =>
            {
                services.RemoveAll<IDatabaseTargetPreparationProvider>();
                services.AddSingleton(provider.Object);
                services.RemoveAll<IDatabaseMigrationExecutor>();
                services.AddSingleton(executor.Object);
                services.RemoveAll<StartupDatabaseReceipt>();
                services.AddSingleton(receipt);
                services.AddSingleton<IHostedService>(witness);
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(code, exception.ToString());
        Assert.DoesNotContain("private-driver-text", exception.ToString());
        Assert.DoesNotContain("SELECT", exception.ToString());
        Assert.DoesNotContain(Database.ConnectionString, exception.ToString());
        Assert.DoesNotContain(new NpgsqlConnectionStringBuilder(Database.ConnectionString).Password!, exception.ToString());
        Assert.Equal(ServiceMigrationReadinessState.Failed, receipt.State);
        Assert.Equal(code, receipt.ErrorCode);
        Assert.False(witness.Started);
        executor.VerifyNoOtherCalls();
        provider.Verify(p => p.PrepareAsync(It.IsAny<DatabaseTargetPreparationRequest>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(preparations));
        Assert.Equal(scenario == "notConnectableAfterPreparation" ? 2 : 1, observations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gate_CancellationBeforeOrDuringObservation_KeepsRunningAndSkipsLaterStages(bool during)
    {
        using var cancellation = new CancellationTokenSource();
        if (!during) cancellation.Cancel();
        var provider = PreparationProvider();
        provider.Setup(p => p.ObserveAsync(It.IsAny<BootstrapDatabaseConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns((BootstrapDatabaseConfiguration _, CancellationToken token) =>
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(DatabaseTargetObservation.TargetConnectable());
            });
        var executor = new Mock<IDatabaseMigrationExecutor>(MockBehavior.Strict);
        var services = new ServiceCollection().AddLogging();
        services.AddServiceMantlePostgreSqlDeploymentCapability();
        services.AddSingleton(executor.Object);
        services.AddServiceMantle(ServiceId.Parse("ruoyu-admin"), InstanceId.Parse("cancel-test"))
            .AddMigrationLockProvider<PostgreSqlMigrationLockProvider>()
            .AddStartupDatabaseGate(Options());
        services.AddSingleton(provider.Object);
        await using var serviceProvider = services.BuildServiceProvider();
        var receipt = serviceProvider.GetRequiredService<StartupDatabaseReceipt>();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            serviceProvider.GetRequiredService<StartupDatabaseGate>().RunAsync(
                Options(), receipt, ServiceId.Parse("ruoyu-admin"), cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(ServiceMigrationReadinessState.Running, receipt.State);
        Assert.Null(receipt.ErrorCode);
        executor.VerifyNoOtherCalls();
        provider.Verify(p => p.PrepareAsync(It.IsAny<DatabaseTargetPreparationRequest>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(p => p.ObserveAsync(It.IsAny<BootstrapDatabaseConfiguration>(),
            It.IsAny<CancellationToken>()), Times.Exactly(during ? 1 : 0));
    }

    [Fact]
    public async Task Host_ExistingDatabase_LowPrivilegeOwnerNeedsNeitherCreatedbNorMaintenanceAccess()
    {
        var role = "audit_role_" + Guid.NewGuid().ToString("N");
        var target = "audit_owned_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await ExecuteMaintenanceAsync($"CREATE ROLE \"{role}\" LOGIN NOSUPERUSER NOCREATEDB PASSWORD '{password}'");
        try
        {
            await ExecuteMaintenanceAsync($"CREATE DATABASE \"{target}\" OWNER \"{role}\"");
            await ExecuteMaintenanceAsync("REVOKE CONNECT ON DATABASE postgres FROM PUBLIC");
            using var factory = CreateFactory(settings: new Dictionary<string, string?>
            {
                ["Database:Name"] = target,
                ["PostgreSql:Username"] = role,
                ["PostgreSql:Password"] = password,
            });
            using var client = factory.CreateClient();
            Assert.Equal(ServiceMigrationReadinessState.Succeeded,
                factory.Services.GetRequiredService<StartupDatabaseReceipt>().State);
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }
        finally
        {
            await ExecuteMaintenanceAsync("GRANT CONNECT ON DATABASE postgres TO PUBLIC");
            NpgsqlConnection.ClearAllPools();
            await ExecuteMaintenanceAsync($"DROP DATABASE IF EXISTS \"{target}\" WITH (FORCE)");
            await ExecuteMaintenanceAsync($"DROP ROLE \"{role}\"");
        }
    }

    [Theory]
    [InlineData("unreachable", WellKnownDatabaseTargetPreparationErrorCodes.ConnectionFailed)]
    [InlineData("authentication", WellKnownDatabaseTargetPreparationErrorCodes.AuthenticationFailed)]
    [InlineData("permission", WellKnownDatabaseTargetPreparationErrorCodes.PermissionDenied)]
    public async Task Host_RealPostgreSqlRefusal_NoCreationFallbackOrServiceStart(string scenario, string code)
    {
        var target = "audit_denied_" + Guid.NewGuid().ToString("N");
        var role = "audit_denied_role_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var settings = new Dictionary<string, string?>
        {
            ["Database:Name"] = target,
            ["Database:AllowCreate"] = "true",
        };
        if (scenario == "unreachable") settings["PostgreSql:Port"] = "1";
        else if (scenario == "authentication") settings["PostgreSql:Password"] = password;
        else
        {
            await ExecuteMaintenanceAsync($"CREATE ROLE \"{role}\" LOGIN NOSUPERUSER NOCREATEDB PASSWORD '{password}'");
            settings["PostgreSql:Username"] = role;
            settings["PostgreSql:Password"] = password;
        }

        try
        {
            var witness = new StartWitness();
            using var factory = CreateFactory(settings: settings,
                configureTestServices: services => services.AddSingleton<IHostedService>(witness));
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains(code, exception.ToString());
            Assert.DoesNotContain(password, exception.ToString());
            Assert.DoesNotContain("Host=", exception.ToString());
            Assert.DoesNotContain("Password=", exception.ToString());
            Assert.False(witness.Started);
            await using var connection = new NpgsqlConnection(Database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM pg_database WHERE datname = $1";
            command.Parameters.AddWithValue(target);
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        finally
        {
            if (scenario == "permission") await ExecuteMaintenanceAsync($"DROP ROLE \"{role}\"");
        }
    }

    [Fact]
    public async Task Host_GateSucceededExactlyOnce_BeforeHostedServiceStarts()
    {
        var receipt = new StartupDatabaseReceipt();
        var witness = new StartWitness(() => Assert.Equal(ServiceMigrationReadinessState.Succeeded, receipt.State));
        using var factory = CreateFactory(configureTestServices: services =>
        {
            services.RemoveAll<StartupDatabaseReceipt>();
            services.AddSingleton(receipt);
            services.AddSingleton<IHostedService>(witness);
        });
        using var client = factory.CreateClient();
        Assert.True(witness.Started);
        Assert.Same(receipt, factory.Services.GetRequiredService<StartupDatabaseReceipt>());
        Assert.IsType<PostgreSqlDatabaseDeploymentCapabilityProvider>(
            Assert.Single(factory.Services.GetServices<IDatabaseDeploymentCapabilityProvider>()));
        // A duplicate direct invocation is rejected, proving Program did not run another gate.
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.Services.GetRequiredService<StartupDatabaseGate>()
            .RunAsync(factory.Services.GetRequiredService<StartupDatabaseGateOptions>(), receipt,
                ServiceId.Parse("ruoyu-admin")).AsTask());
    }

    private StartupDatabaseGateOptions Options() => new(
        new BootstrapDatabaseConfiguration(WellKnownDatabaseProviderIds.PostgreSql, null, Database.ConnectionString),
        DatabaseDeploymentMode.MultiInstance, TimeSpan.FromSeconds(30), enableTargetPreparation: true);

    private static Mock<IDatabaseTargetPreparationProvider> PreparationProvider()
    {
        var provider = new Mock<IDatabaseTargetPreparationProvider>(MockBehavior.Strict);
        provider.SetupGet(p => p.ProviderId).Returns(WellKnownDatabaseProviderIds.PostgreSql);
        provider.SetupGet(p => p.TargetKind).Returns(BootstrapDatabaseTargetKind.ServerDatabase);
        return provider;
    }

    private async Task ExecuteMaintenanceAsync(string sql)
    {
        var maintenance = PostgreSqlMaintenanceConnection.DeriveConnectionString(Database.ConnectionString);
        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StartWitness(Action? onStart = null) : IHostedService
    {
        public bool Started { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            onStart?.Invoke();
            Started = true;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
