using Ruoyu.Admin.Consul;
using ServiceMantle.Bootstrap;
using ServiceMantle.Database.PostgreSql;
using ServiceMantle.Migration;

namespace Admin.WebApi.Database;

/// <summary>Consumer configuration boundary; database I/O and state belong to the shared gate.</summary>
internal static class AuditDatabaseStartupConfiguration
{
    internal static StartupDatabaseGateOptions Read(IConfiguration configuration)
    {
        var raw = configuration["Database:AllowCreate"];
        var allowCreate = false;
        if (!string.IsNullOrWhiteSpace(raw) && !bool.TryParse(raw, out allowCreate))
        {
            throw InvalidTarget();
        }

        try
        {
            var connectionString = SharedPostgreSqlConnectionStringFactory.BuildOrFallback(
                configuration, configuration.GetConnectionString("AuditDb"));
            // Parsing/derivation does no I/O. Never preserve an inner driver exception: its
            // message may contain an invalid configuration value or a connection secret.
            return PostgreSqlStartupDatabaseGateOptions.Create(
                new BootstrapDatabaseConfiguration(
                    WellKnownDatabaseProviderIds.PostgreSql, null, connectionString!),
                allowTargetCreation: allowCreate);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw InvalidTarget();
        }
    }

    private static InvalidOperationException InvalidTarget() => new(
        "AuditDb configuration is invalid; refusing to start: " +
        WellKnownDatabaseTargetPreparationErrorCodes.InvalidTarget + ".");
}
