using Admin.WebApi.Database;
using Microsoft.Extensions.Configuration;
using Npgsql;
using ServiceMantle.Bootstrap;
using ServiceMantle.Database.PostgreSql;
using Xunit;

namespace Admin.WebApi.Tests.Database;

public sealed class AuditDatabaseStartupConfigurationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void ValidConfiguration_FixesMultiInstanceAndBudgets_ChangesOnlyMaintenanceDatabase(
        string? allow, bool expected)
    {
        var config = Configuration(allow);
        var options = AuditDatabaseStartupConfiguration.Read(config);
        Assert.Equal(DatabaseDeploymentMode.MultiInstance, options.DeploymentMode);
        Assert.True(options.EnableTargetPreparation);
        Assert.Equal(expected, options.AllowTargetCreation);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LockWaitBudget);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PreparationTimeout);
        var maintenance = new NpgsqlConnectionStringBuilder(options.MaintenanceConnectionString);
        Assert.Equal("postgres", maintenance.Database);
        maintenance.Database = "ruoyu_admin";
        Assert.Equal(new NpgsqlConnectionStringBuilder(options.Database.ConnectionString).ConnectionString,
            maintenance.ConnectionString);
    }

    [Theory]
    [InlineData("invalid-private-value", "Host=localhost;Database=ruoyu_admin")]
    [InlineData("1", "Host=localhost;Database=ruoyu_admin")]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "private-invalid-connection")]
    [InlineData(null, "Host=localhost;Port=private-invalid-port;Password=private-test-value")]
    public void InvalidConfiguration_FailsWithSafeCodeWithoutInnerException(string? allow, string? connection)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AuditDatabaseStartupConfiguration.Read(Configuration(allow, connection)));
        Assert.Contains(WellKnownDatabaseTargetPreparationErrorCodes.InvalidTarget, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("private", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", exception.ToString());
        Assert.DoesNotContain("Password=", exception.ToString());
    }

    [Fact]
    public async Task SharedDeploymentCapability_UnusedSingleInstanceIdentityExcludesCredentials()
    {
        var provider = new PostgreSqlDatabaseDeploymentCapabilityProvider();
        Assert.Equal(DatabaseDeploymentSupport.SingleAndMultiInstance, provider.Capability.Support);
        var target = AuditDatabaseStartupConfiguration.Read(Configuration(null)).Database;
        var identity = await provider.GetCanonicalTargetIdentityAsync(target, default);
        var changedCredentials = new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            Username = "another-owner",
            Password = "another-mock-value",
        };
        var equivalent = new BootstrapDatabaseConfiguration(
            WellKnownDatabaseProviderIds.PostgreSql, null, changedCredentials.ConnectionString);
        Assert.Equal(identity, await provider.GetCanonicalTargetIdentityAsync(equivalent, default));
        Assert.Matches("^[0-9A-F]{64}$", identity);
        Assert.DoesNotContain("mock-value", identity);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetCanonicalTargetIdentityAsync(target, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static IConfiguration Configuration(string? allow,
        string? connection = "Host=localhost;Port=5432;Database=ruoyu_admin;Username=test;Password=mock-value") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AllowCreate"] = allow,
            ["ConnectionStrings:AuditDb"] = connection,
        }).Build();
}
